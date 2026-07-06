using TopoRapportWin;
using Xunit;

namespace NovaFiches.Tests;

/// <summary>
/// Tests de caractérisation (golden master) sur AutoCadExportService.ExportFromNovaState :
/// le contrat JSON stateJson/payloadJson n'est pas formellement documenté ailleurs, ces
/// tests figent le comportement actuel observé dans le code pour détecter toute régression
/// avant un futur refactor, plutôt que de viser une couverture exhaustive immédiate.
/// </summary>
public class AutoCadExportServiceTests
{
    private static string NewTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "nf-autocad-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    [Fact]
    public void ExportFromNovaState_StandardCase_WritesFourFilesWithExpectedContent()
    {
        var dir = NewTempDir();
        try
        {
            const string stateJson = """
                {
                  "infosDossier": {
                    "repCHA": "02782",
                    "repElements": "IMPLANTATION N10",
                    "repZone": "Paris 16",
                    "repSiteAddress": "104 Avenue du Président Kennedy",
                    "repSiteContact": "Nom Prenom",
                    "repType": "TYPE",
                    "repIndice": "A",
                    "repDate": "2026-07-06",
                    "metaCoordSys": "RGF93 CC49",
                    "metaAltSys": "IGN 69",
                    "repClient": "LOGISUR",
                    "metaIntervenant": "J. Guerry",
                    "repPhase": "REC"
                  }
                }
                """;
            const string payloadJson = """
                {
                  "Implantation": [
                    { "Id": "P1", "X": 700000.123, "Y": 6280000.456, "Z": 125.5 },
                    { "Id": "P2", "X": 700010.0, "Y": 6280010.0, "Z": 126.0 }
                  ],
                  "Ligne": [],
                  "Leve": []
                }
                """;

            AutoCadExportService.ExportFromNovaState(stateJson, payloadJson, dir);

            var implantationFile = Path.Combine(dir, "NOVA_02782_IMPLANTATION.txt");
            var ligneFile = Path.Combine(dir, "NOVA_02782_LIGNE.txt");
            var leveFile = Path.Combine(dir, "NOVA_02782_LEVE.txt");
            var cartoucheFile = Path.Combine(dir, "NOVA_02782_CARTOUCHE.txt");

            Assert.True(File.Exists(implantationFile));
            Assert.True(File.Exists(ligneFile));
            Assert.True(File.Exists(leveFile));
            Assert.True(File.Exists(cartoucheFile));

            var implantationLines = File.ReadAllLines(implantationFile);
            Assert.Equal(2, implantationLines.Length);
            Assert.Equal("P1\t700000.123\t6280000.456\t125.500", implantationLines[0]);
            Assert.Equal("P2\t700010.000\t6280010.000\t126.000", implantationLines[1]);

            var cartoucheLines = File.ReadAllLines(cartoucheFile);
            Assert.Contains("CODE_CHANTIER=02782", cartoucheLines);
            Assert.Contains("ADRESSE-PROJET=104 Avenue du Président Kennedy", cartoucheLines);
            Assert.Contains("ADRESSE-PROJET-2=Paris 16", cartoucheLines);
            Assert.Contains("IND=A", cartoucheLines);
            Assert.Contains("DATE=06/07/2026", cartoucheLines); // NormalizeDate: yyyy-MM-dd -> dd/MM/yyyy
            Assert.Contains("PLANI=RGF93 CC49", cartoucheLines);
            Assert.Contains("ALTIMETRIE=IGN 69", cartoucheLines);
            Assert.Contains("INTERVENANT=J. Guerry", cartoucheLines);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void ExportFromNovaState_PointAtOrigin_IsSkipped()
    {
        var dir = NewTempDir();
        try
        {
            const string stateJson = "{}";
            const string payloadJson = """
                {
                  "Implantation": [
                    { "Id": "ZERO", "X": 0, "Y": 0, "Z": 0 },
                    { "Id": "REAL", "X": 1.0, "Y": 2.0, "Z": 3.0 }
                  ]
                }
                """;

            AutoCadExportService.ExportFromNovaState(stateJson, payloadJson, dir);

            var lines = File.ReadAllLines(Path.Combine(dir, "NOVA_NO_CHA_IMPLANTATION.txt"));

            // Le point (0,0,0) est traité comme "point vide" et exclu (cf. Math.Abs(x) < double.Epsilon ...).
            Assert.Single(lines);
            Assert.StartsWith("REAL\t", lines[0]);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void ExportFromNovaState_EmptyInputs_FallsBackToDefaultsWithoutCrashing()
    {
        var dir = NewTempDir();
        try
        {
            AutoCadExportService.ExportFromNovaState("{}", "{}", dir);

            var cartoucheFile = Path.Combine(dir, "NOVA_NO_CHA_CARTOUCHE.txt");
            Assert.True(File.Exists(cartoucheFile));

            var cartoucheLines = File.ReadAllLines(cartoucheFile);
            Assert.Contains("CODE_CHANTIER=", cartoucheLines);
            Assert.Contains("IND=A", cartoucheLines); // valeur par défaut documentée dans le code
            Assert.Contains("PHASE=EXE", cartoucheLines); // valeur par défaut documentée dans le code

            var implantationFile = Path.Combine(dir, "NOVA_NO_CHA_IMPLANTATION.txt");
            Assert.True(File.Exists(implantationFile));
            Assert.Empty(File.ReadAllLines(implantationFile));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void ExportFromNovaState_ChaWithSpacesAndSlash_IsSanitizedInFileName()
    {
        var dir = NewTempDir();
        try
        {
            const string stateJson = """{ "infosDossier": { "repCHA": "02782 / rev A" } }""";

            AutoCadExportService.ExportFromNovaState(stateJson, "{}", dir);

            // Sanitize() : remplace les caractères invalides Windows par '_' ('/' -> '_'),
            // découpe sur les espaces puis rejoint avec '_' (donc le '_' issu du '/' devient
            // lui-même un segment, entouré de deux séparateurs '_' supplémentaires), et met
            // en majuscules. "02782 / rev A" -> "02782___REV_A".
            var files = Directory.GetFiles(dir).Select(Path.GetFileName).ToArray();
            var cartoucheFile = files.SingleOrDefault(f => f!.EndsWith("_CARTOUCHE.txt", StringComparison.Ordinal));
            Assert.NotNull(cartoucheFile);
            Assert.Equal("NOVA_02782___REV_A_CARTOUCHE.txt", cartoucheFile);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
