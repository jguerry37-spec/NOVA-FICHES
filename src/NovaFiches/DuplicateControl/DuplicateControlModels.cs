namespace TopoRapportWin.DuplicateControl;

// Role : "input" (fichier d'entrée) ou "control" (fichier de contrôle unique).
public sealed record DcPoint(string Id, double X, double Y, double Z, bool HasZ, string? Code, string SourceFile, string Role);

public sealed record DcDuplicateGroup(string Id, IReadOnlyList<DcPoint> Occurrences);

// OrphanInputs : points d'entrée dont l'ID n'a pas de correspondance dans le contrôle - passent
// tels quels dans le fichier final (y compris, en V1, si le même ID apparaît dans 2 fichiers
// d'entrée différents sans être dans le contrôle : les deux lignes se retrouvent alors dans le
// fichier final telles quelles - limite actée avec l'utilisateur, voir le plan).
// OrphanControls : points du contrôle sans aucune correspondance dans les entrées - passent
// eux aussi tels quels dans le fichier final.
public sealed record DcAnalysisResult(
    IReadOnlyList<DcDuplicateGroup> Duplicates,
    IReadOnlyList<DcPoint> OrphanInputs,
    IReadOnlyList<DcPoint> OrphanControls);
