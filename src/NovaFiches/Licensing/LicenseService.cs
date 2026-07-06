using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace TopoRapportWin.Licensing;

/// <summary>
/// Validation hors-ligne d'une licence Nova-Fiches signée par ECDSA P-256.
/// Aucune vérification réseau : la confiance repose uniquement sur la clé publique
/// embarquée ci-dessous et la clé privée correspondante détenue par tools/license-gen
/// (jamais embarquée dans l'application livrée).
/// </summary>
internal static class LicenseService
{
    // Clé publique ECDSA P-256 (format SubjectPublicKeyInfo, base64). Générée une seule
    // fois via `license-gen genkey` — voir tools/license-gen/README.md. Remplacer cette
    // valeur si la clé est un jour régénérée (invalide alors toutes les licences déjà émises).
    private const string PublicKeyBase64 =
        "MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAEvZZ2dIRa8lSy3hhHOoVfk1Aic9OSXq6HxqvKpkGeI75poJBOQhyh24nD87sxcSkua3QAPy33vcYTxSwk0BNreQ==";

    public static string LicensePath => Path.Combine(AppLog.AppDataDir, "license.json");

    public static LicenseValidationResult LoadAndValidate()
    {
        if (!File.Exists(LicensePath))
        {
            return new LicenseValidationResult(LicenseStatus.NotActivated, null, "Aucune licence activée sur ce poste.");
        }

        try
        {
            var fileJson = File.ReadAllText(LicensePath);
            return Validate(fileJson);
        }
        catch (Exception ex)
        {
            AppLog.Error("License: lecture license.json impossible", ex);
            return new LicenseValidationResult(LicenseStatus.Corrupted, null, "Fichier de licence illisible ou corrompu.");
        }
    }

    /// <summary>
    /// Valide le contenu JSON d'un fichier de licence (déjà lu en mémoire), sans toucher au disque.
    /// Utilisé à la fois pour la licence installée et pour valider un fichier candidat avant installation.
    /// </summary>
    public static LicenseValidationResult Validate(string licenseFileJson)
    {
        byte[] payloadBytes;
        byte[] signature;
        try
        {
            using var doc = JsonDocument.Parse(licenseFileJson);
            payloadBytes = Convert.FromBase64String(doc.RootElement.GetProperty("payload").GetString() ?? "");
            signature = Convert.FromBase64String(doc.RootElement.GetProperty("signature").GetString() ?? "");
        }
        catch (Exception ex)
        {
            AppLog.Error("License: format JSON invalide", ex);
            return new LicenseValidationResult(LicenseStatus.Corrupted, null, "Format de licence invalide.");
        }

        using var ecdsa = ECDsa.Create();
        ecdsa.ImportSubjectPublicKeyInfo(Convert.FromBase64String(PublicKeyBase64), out _);

        if (!ecdsa.VerifyData(payloadBytes, signature, HashAlgorithmName.SHA256))
        {
            return new LicenseValidationResult(LicenseStatus.InvalidSignature, null, "Signature de licence invalide (fichier altéré ou non émis par Novatlas).");
        }

        LicensePayload payload;
        try
        {
            var canonicalJson = Encoding.UTF8.GetString(payloadBytes);
            payload = LicensePayloadFormat.ParseCanonicalPayload(canonicalJson);
        }
        catch (Exception ex)
        {
            AppLog.Error("License: payload signé mais illisible", ex);
            return new LicenseValidationResult(LicenseStatus.Corrupted, null, "Contenu de licence illisible malgré une signature valide.");
        }

        if (payload.ExpiresAtUtc.HasValue && payload.ExpiresAtUtc.Value < DateTime.UtcNow)
        {
            return new LicenseValidationResult(LicenseStatus.Expired, payload, $"Licence expirée le {payload.ExpiresAtUtc.Value:yyyy-MM-dd}.");
        }

        if (!string.IsNullOrWhiteSpace(payload.MachineId))
        {
            var currentMachineId = MachineId.GetCurrentMachineIdHash();
            if (!string.Equals(payload.MachineId, currentMachineId, StringComparison.OrdinalIgnoreCase))
            {
                return new LicenseValidationResult(LicenseStatus.MachineMismatch, payload, "Cette licence est liée à un autre poste.");
            }
        }

        return new LicenseValidationResult(LicenseStatus.Valid, payload, $"Licence valide — {payload.LicensedTo}.");
    }

    /// <summary>
    /// Valide un fichier de licence candidat puis l'installe (copie) à l'emplacement
    /// attendu si (et seulement si) il est valide. Ne touche jamais à une licence déjà
    /// installée si le candidat est invalide.
    /// </summary>
    public static LicenseValidationResult TryInstall(string candidateFilePath)
    {
        string json;
        try
        {
            json = File.ReadAllText(candidateFilePath);
        }
        catch (Exception ex)
        {
            AppLog.Error("License: lecture du fichier candidat impossible", ex);
            return new LicenseValidationResult(LicenseStatus.Corrupted, null, "Impossible de lire ce fichier.");
        }

        var result = Validate(json);
        if (!result.IsValid)
        {
            return result;
        }

        Directory.CreateDirectory(AppLog.AppDataDir);
        File.WriteAllText(LicensePath, json);
        AppLog.Info($"License: licence installée ({result.Payload?.LicensedTo}).");
        return result;
    }

    public static string GetCurrentMachineId() => MachineId.GetCurrentMachineIdHash();
}
