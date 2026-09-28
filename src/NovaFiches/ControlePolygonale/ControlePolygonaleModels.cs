namespace TopoRapportWin.ControlePolygonale;

public sealed record CpolyXyRow(
    string Point, double? XTheo, double? YTheo, double? XCtrl, double? YCtrl,
    double? Dx, double? Dy, double? DeltaXy, bool Complete);

public sealed record CpolyZRow(
    string Point, double? ZTheo, double? ZCtrl, double? Dz, bool Complete);

public sealed record CpolyGeoBaseRow(string Point, double X, double Y, double Z);

public sealed record CpolyXyStats(
    int Count, int Complete, int Missing,
    double? MaxAbsDx, double? MaxAbsDy, double? MaxDeltaXy, string? MaxDeltaXyPoint, double? MeanDeltaXy);

public sealed record CpolyZStats(
    int Count, int Complete, int Missing,
    double? MaxAbsDz, string? MaxAbsDzPoint, double? MeanAbsDz);

public sealed record CpolyXySheet(string Name, IReadOnlyList<CpolyXyRow> Rows, CpolyXyStats Stats);

public sealed record CpolyZSheet(string Name, IReadOnlyList<CpolyZRow> Rows, CpolyZStats Stats);

public sealed record CpolyWorkbook(IReadOnlyList<CpolyXySheet> XySheets, IReadOnlyList<CpolyZSheet> ZSheets);
