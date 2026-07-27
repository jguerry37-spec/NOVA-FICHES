namespace TopoRapportWin.Licensing;

/// <summary>Contenu signé d'une licence Nova-Fiches (voir LicensePayloadFormat pour le format canonique).</summary>
/// <param name="Features">
/// Clés des modules complémentaires activés par cette licence (ex. "fiches-signaletiques", "devis").
/// Liste vide pour une licence standard. Absente du payload canonique d'une licence émise avant
/// l'introduction de ce champ - ParseCanonicalPayload la reconstitue alors comme un tableau vide.
/// </param>
public sealed record LicensePayload(
    string LicensedTo,
    DateTime IssuedAtUtc,
    DateTime? ExpiresAtUtc,
    string? MachineId,
    string[] Features
);

public enum LicenseStatus
{
    Valid,
    NotActivated,
    Corrupted,
    InvalidSignature,
    Expired,
    MachineMismatch
}

public sealed record LicenseValidationResult(LicenseStatus Status, LicensePayload? Payload, string Message)
{
    public bool IsValid => Status == LicenseStatus.Valid;
}
