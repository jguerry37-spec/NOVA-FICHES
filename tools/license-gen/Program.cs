using System.Security.Cryptography;
using System.Text;
using System.Windows.Forms;
using Microsoft.Win32;

namespace LicenseGen;

// Outil interne Novatlas — NE FAIT PAS PARTIE de l'application livrée aux utilisateurs.
// Détient la clé privée : ne jamais committer un fichier de clé privée généré par cet outil,
// ne jamais copier ce dossier dans le publish de Nova-Fiches.
internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        // Aucun argument (double-clic sur license-gen.exe, ou lancement sans commande) :
        // écran de génération de licence. Les commandes CLI (genkey/issue/machineid)
        // restent disponibles pour le scripting et ne changent pas de comportement.
        if (args.Length == 0)
        {
            Application.SetHighDpiMode(HighDpiMode.SystemAware);
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new LicenseGenForm());
            return 0;
        }

        try
        {
            switch (args[0].ToLowerInvariant())
            {
                case "genkey":
                    return GenKey(args);
                case "issue":
                    return Issue(args);
                case "machineid":
                    Console.WriteLine(MachineId.GetCurrentMachineIdHash());
                    return 0;
                default:
                    PrintUsage();
                    return 1;
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Erreur : {ex.Message}");
            return 1;
        }
    }

    private static void PrintUsage()
    {
        Console.WriteLine("""
            Usage :
              license-gen genkey --out <fichier-cle-privee.txt>
                  Génère une nouvelle paire de clés ECDSA P-256.
                  Écrit la clé PRIVÉE (à garder secrète, hors dépôt Git) dans <fichier>.
                  Affiche la clé PUBLIQUE (base64) à coller dans LicenseService.cs.

              license-gen issue --key <fichier-cle-privee.txt> --to "<Nom client>" --out <license.json> [--expires yyyy-MM-dd] [--machine <hash>] [--features cle1,cle2,...]
                  Génère un fichier de licence signé.
                  --features active des modules complémentaires (manager) en plus des modules
                  standards, toujours disponibles pour toute licence. Omis ou vide = licence
                  standard, aucun module complémentaire.

              license-gen machineid
                  Affiche l'identifiant machine (haché) du poste courant, au même
                  format que celui utilisé par l'application pour lier une licence
                  à un poste précis.
            """);
    }

    private static int GenKey(string[] args)
    {
        var outPath = GetOption(args, "--out") ?? throw new ArgumentException("--out requis.");

        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var privateKeyBase64 = Convert.ToBase64String(ecdsa.ExportECPrivateKey());
        var publicKeyBase64 = Convert.ToBase64String(ecdsa.ExportSubjectPublicKeyInfo());

        File.WriteAllText(outPath, privateKeyBase64);

        Console.WriteLine("Clé privée écrite dans : " + Path.GetFullPath(outPath));
        Console.WriteLine("NE JAMAIS committer ce fichier dans Git. Faites-en une copie de sauvegarde sécurisée hors dépôt.");
        Console.WriteLine();
        Console.WriteLine("Clé PUBLIQUE à coller dans src/NovaFiches/Licensing/LicenseService.cs :");
        Console.WriteLine(publicKeyBase64);
        return 0;
    }

    private static int Issue(string[] args)
    {
        var keyPath = GetOption(args, "--key") ?? throw new ArgumentException("--key requis.");
        var to = GetOption(args, "--to") ?? throw new ArgumentException("--to requis.");
        var outPath = GetOption(args, "--out") ?? throw new ArgumentException("--out requis.");
        var expiresRaw = GetOption(args, "--expires");
        var machine = GetOption(args, "--machine");
        var featuresRaw = GetOption(args, "--features");
        var features = string.IsNullOrWhiteSpace(featuresRaw)
            ? Array.Empty<string>()
            : featuresRaw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        DateTime? expiresAtUtc = null;
        if (!string.IsNullOrWhiteSpace(expiresRaw))
        {
            expiresAtUtc = DateTime.Parse(expiresRaw).ToUniversalTime();
        }

        var result = IssueLicense(new IssueLicenseRequest(keyPath, to, expiresAtUtc, machine, features));
        File.WriteAllText(outPath, result.LicenseJson);

        Console.WriteLine("Licence générée : " + Path.GetFullPath(outPath));
        Console.WriteLine($"  Client : {to}");
        Console.WriteLine($"  Émise (UTC) : {result.IssuedAtUtc:yyyy-MM-dd HH:mm:ss}");
        Console.WriteLine($"  Expire (UTC) : {(expiresAtUtc.HasValue ? expiresAtUtc.Value.ToString("yyyy-MM-dd") : "jamais")}");
        Console.WriteLine($"  Machine liée : {machine ?? "(aucune - licence portable sur tout poste)"}");
        Console.WriteLine($"  Modules complémentaires : {(features.Length > 0 ? string.Join(", ", features) : "(aucun - licence standard)")}");
        return 0;
    }

    /// <summary>
    /// Coeur de la signature d'une licence, partagé entre la commande CLI "issue" et
    /// LicenseGenForm (interface graphique), pour ne jamais avoir deux implémentations
    /// du même format canonique/signature à maintenir en parallèle dans cet outil.
    /// </summary>
    internal static IssueLicenseResult IssueLicense(IssueLicenseRequest request)
    {
        using var ecdsa = ECDsa.Create();
        ecdsa.ImportECPrivateKey(Convert.FromBase64String(File.ReadAllText(request.PrivateKeyPath).Trim()), out _);

        var issuedAtUtc = DateTime.UtcNow;
        var payload = LicensePayloadFormat.BuildCanonicalPayload(request.LicensedTo, issuedAtUtc, request.ExpiresAtUtc, request.MachineId, request.Features);
        var payloadBytes = Encoding.UTF8.GetBytes(payload);
        var signature = ecdsa.SignData(payloadBytes, HashAlgorithmName.SHA256);

        var licenseJson = LicensePayloadFormat.BuildLicenseFile(payloadBytes, signature);
        return new IssueLicenseResult(licenseJson, issuedAtUtc);
    }

    private static string? GetOption(string[] args, string name)
    {
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
                return args[i + 1];
        }
        return null;
    }
}

internal readonly record struct IssueLicenseRequest(
    string PrivateKeyPath,
    string LicensedTo,
    DateTime? ExpiresAtUtc,
    string? MachineId,
    IReadOnlyList<string> Features);

internal readonly record struct IssueLicenseResult(string LicenseJson, DateTime IssuedAtUtc);
