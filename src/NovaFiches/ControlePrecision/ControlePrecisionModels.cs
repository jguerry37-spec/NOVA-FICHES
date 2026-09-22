namespace TopoRapportWin.ControlePrecision;

public sealed record CpPoint(string Id, double X, double Y, double Z);

public sealed record CpMatchedPoint(
    string Id, CpPoint Leve, CpPoint Controle,
    double DX, double DY, double DZ,
    double PlaniDev, double AltiDev, double ThreeDDev);

public sealed record CpStats(
    int N, double Min, double Max, double Mean, double MeanAbs, double Variance, double StdDev,
    double Median, double Mad, double Q1, double P33, double P67, double Q3,
    double CiLower, double CiUpper);

// Condition 1 (arrêté 2003, art. annexe) : la moyenne des écarts doit être <= S1 = baseTol * f,
// avec f = 1 + 1/(2*C²).
public sealed record CpCond1(bool Ok, double MeanDev, double Seuil);

// Condition 2 : le nombre d'écarts dépassant T1 = k*baseTol*f ne doit pas excéder N' (table de
// l'annexe, fonction du nombre total de points N).
public sealed record CpCond2(bool Ok, int CountOverT1, double SeuilT1, int NPrimeMax);

// Condition 3 : aucun écart ne doit dépasser T2 = 1.5*T1.
public sealed record CpCond3(bool Ok, int CountOverT2, double SeuilT2);

public sealed record CpConditionSet(CpCond1 Cond1, CpCond2 Cond2, CpCond3 Cond3, bool OverallOk);

public sealed record CpAnalysisResult(
    CpStats DxStats, CpStats DyStats, CpStats DzStats,
    CpStats PlaniStats, CpStats AltiStats, CpStats ThreeDStats,
    CpConditionSet? AltiConditions, CpConditionSet? PlaniConditions, CpConditionSet? ThreeDConditions,
    bool OverallConformity,
    (double X, double Y, double Z)? BarycentreLeve,
    (double X, double Y, double Z)? BarycentreControle);
