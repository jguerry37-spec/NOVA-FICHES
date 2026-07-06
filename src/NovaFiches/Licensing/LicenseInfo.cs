namespace TopoRapportWin.Licensing;

/// <summary>Contenu signé d'une licence Nova-Fiches (voir LicensePayloadFormat pour le format canonique).</summary>
public sealed record LicensePayload(
    string LicensedTo,
    DateTime IssuedAtUtc,
    DateTime? ExpiresAtUtc,
    string? MachineId
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
