using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using TopoRapportWin.Licensing;
using Xunit;

namespace NovaFiches.Tests;

/// <summary>
/// Teste uniquement LicenseService.Validate(json, publicKey) : cette surcharge ne touche
/// jamais le disque (contrairement à LoadAndValidate()/TryInstall(), qui lisent/écrivent
/// %LOCALAPPDATA%\NOVATLAS\Nova-Fiches\license.json — un chemin partagé avec la vraie
/// installation de l'application sur la machine, qu'un test ne doit jamais toucher).
/// Toutes les paires de clés utilisées ici sont jetables, générées dans le test : jamais
/// la clé privée de production (tools/license-gen/keys/private.key).
/// </summary>
public class LicenseServiceTests
{
    private static (string PublicKeyBase64, ECDsa Key) NewTestKeyPair()
    {
        var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        return (Convert.ToBase64String(ecdsa.ExportSubjectPublicKeyInfo()), ecdsa);
    }

    private static string SignLicense(ECDsa signingKey, string licensedTo, DateTime issuedAtUtc, DateTime? expiresAtUtc, string? machineId)
    {
        var canonicalPayload = LicensePayloadFormat.BuildCanonicalPayload(licensedTo, issuedAtUtc, expiresAtUtc, machineId);
        var payloadBytes = Encoding.UTF8.GetBytes(canonicalPayload);
        var signature = signingKey.SignData(payloadBytes, HashAlgorithmName.SHA256);

        return JsonSerializer.Serialize(new
        {
            payload = Convert.ToBase64String(payloadBytes),
            signature = Convert.ToBase64String(signature)
        });
    }

    [Fact]
    public void Validate_ValidSignature_ReturnsValid()
    {
        var (publicKey, key) = NewTestKeyPair();
        using var _ = key;
        var license = SignLicense(key, "Client Test", DateTime.UtcNow, null, null);

        var result = LicenseService.Validate(license, publicKey);

        Assert.Equal(LicenseStatus.Valid, result.Status);
        Assert.True(result.IsValid);
        Assert.Equal("Client Test", result.Payload?.LicensedTo);
    }

    [Fact]
    public void Validate_TamperedPayload_ReturnsInvalidSignature()
    {
        var (publicKey, key) = NewTestKeyPair();
        using var _ = key;
        var license = SignLicense(key, "Client Test", DateTime.UtcNow, null, null);

        using var doc = JsonDocument.Parse(license);
        var payload = doc.RootElement.GetProperty("payload").GetString()!;
        var tamperedBytes = Convert.FromBase64String(payload);
        tamperedBytes[0] ^= 0xFF; // altère un octet du payload signé
        var tampered = JsonSerializer.Serialize(new
        {
            payload = Convert.ToBase64String(tamperedBytes),
            signature = doc.RootElement.GetProperty("signature").GetString()
        });

        var result = LicenseService.Validate(tampered, publicKey);

        Assert.Equal(LicenseStatus.InvalidSignature, result.Status);
        Assert.False(result.IsValid);
    }

    [Fact]
    public void Validate_WrongPublicKey_ReturnsInvalidSignature()
    {
        var (_, key) = NewTestKeyPair();
        using var __ = key;
        var (otherPublicKey, otherKey) = NewTestKeyPair();
        using var ___ = otherKey;

        var license = SignLicense(key, "Client Test", DateTime.UtcNow, null, null);

        // Validé avec la clé PUBLIQUE d'une AUTRE paire : doit échouer.
        var result = LicenseService.Validate(license, otherPublicKey);

        Assert.Equal(LicenseStatus.InvalidSignature, result.Status);
    }

    [Fact]
    public void Validate_ExpiredLicense_ReturnsExpired()
    {
        var (publicKey, key) = NewTestKeyPair();
        using var _ = key;
        var license = SignLicense(key, "Client Test", DateTime.UtcNow.AddYears(-1), DateTime.UtcNow.AddDays(-1), null);

        var result = LicenseService.Validate(license, publicKey);

        Assert.Equal(LicenseStatus.Expired, result.Status);
        Assert.False(result.IsValid);
    }

    [Fact]
    public void Validate_NotYetExpired_ReturnsValid()
    {
        var (publicKey, key) = NewTestKeyPair();
        using var _ = key;
        var license = SignLicense(key, "Client Test", DateTime.UtcNow, DateTime.UtcNow.AddDays(30), null);

        var result = LicenseService.Validate(license, publicKey);

        Assert.Equal(LicenseStatus.Valid, result.Status);
    }

    [Fact]
    public void Validate_MalformedJson_ReturnsCorrupted()
    {
        var (publicKey, _) = NewTestKeyPair();

        var result = LicenseService.Validate("ceci n'est pas du JSON {{{", publicKey);

        Assert.Equal(LicenseStatus.Corrupted, result.Status);
        Assert.False(result.IsValid);
    }

    [Fact]
    public void Validate_EmptyString_ReturnsCorrupted()
    {
        var (publicKey, _) = NewTestKeyPair();

        var result = LicenseService.Validate("", publicKey);

        Assert.Equal(LicenseStatus.Corrupted, result.Status);
    }

    [Fact]
    public void Validate_MachineMismatch_ReturnsMachineMismatch()
    {
        var (publicKey, key) = NewTestKeyPair();
        using var _ = key;
        // Un identifiant machine qui ne peut pas correspondre au poste réel exécutant le test
        // (un vrai hash SHA256 fait 64 caractères hexadécimaux).
        var license = SignLicense(key, "Client Test", DateTime.UtcNow, null, "0000000000000000000000000000000000000000000000000000000000bad");

        var result = LicenseService.Validate(license, publicKey);

        Assert.Equal(LicenseStatus.MachineMismatch, result.Status);
    }

    [Fact]
    public void Validate_MachineMatch_ReturnsValid()
    {
        var (publicKey, key) = NewTestKeyPair();
        using var _ = key;
        var currentMachineId = LicenseService.GetCurrentMachineId();
        var license = SignLicense(key, "Client Test", DateTime.UtcNow, null, currentMachineId);

        var result = LicenseService.Validate(license, publicKey);

        Assert.Equal(LicenseStatus.Valid, result.Status);
    }
}
