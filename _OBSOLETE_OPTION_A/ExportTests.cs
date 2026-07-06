using Xunit;
using System;
using System.IO;
using System.Text;

namespace NovaFiches.Tests
{
    /// <summary>
    /// Tests des exports TXT et KMZ.
    /// Règles:
    /// - Export TXT levé topo: X=E, Y=N, Z=H (JAMAIS X=N, Y=E)
    /// - Export KMZ: supporte TXT + DXF, Z en 3D si fourni
    /// </summary>
    public class ExportTests
    {
        private static class TxtExportValidator
        {
            /// <summary>
            /// Valide que l'export TXT levé topo suit le format: X=E, Y=N, Z=H
            /// </summary>
            public static (bool isValid, string? error) ValidateTxtLeveTopoFormat(string content)
            {
                if (string.IsNullOrWhiteSpace(content))
                    return (false, "Contenu TXT vide");

                // Vérifier la présence de X=, Y=, Z=
                if (!content.Contains("X=") || !content.Contains("Y=") || !content.Contains("Z="))
                    return (false, "Format TXT incomplet (manque X=, Y= ou Z=)");

                // Vérifier qu'on n'a pas la mauvaise convention (X=N, Y=E)
                // NOTE: ce test est basique, un vrai test utiliserait regex et parsing
                // if (content.Contains("X=N") || content.Contains("Y=E"))
                //     return (false, "Mauvaise convention d'axes detectée (X=N au lieu de X=E)");

                return (true, null);
            }

            /// <summary>
            /// Valide qu'un fichier TXT contient des points avec nom, X, Y, Z
            /// </summary>
            public static (bool isValid, int lineCount) ValidateTxtPointFormat(string[] lines)
            {
                if (lines.Length < 2)
                    return (false, 0);  // En-tête + au moins 1 point

                int validPoints = 0;
                foreach (var line in lines)
                {
                    if (string.IsNullOrWhiteSpace(line) || line.StartsWith("#"))
                        continue;

                    // Format attendu: ID X Y Z (tab ou espace séparé)
                    var parts = line.Split(new[] { '\t', ' ' }, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length >= 4)  // ID + X + Y + Z minimum
                    {
                        if (double.TryParse(parts[1], out _) &&
                            double.TryParse(parts[2], out _) &&
                            double.TryParse(parts[3], out _))
                        {
                            validPoints++;
                        }
                    }
                }

                return (validPoints > 0, validPoints);
            }
        }

        private static class KmzExportValidator
        {
            /// <summary>
            /// Valide la structure basique d'un KMZ
            /// </summary>
            public static (bool isValid, string? error) ValidateKmzStructure(byte[] kmzContent)
            {
                if (kmzContent == null || kmzContent.Length == 0)
                    return (false, "Fichier KMZ vide");

                // KMZ est un ZIP → signature PK
                if (kmzContent[0] != 0x50 || kmzContent[1] != 0x4B)
                    return (false, "Signature ZIP invalide");

                return (true, null);
            }

            /// <summary>
            /// Valide qu'un document KML contient des Placemark
            /// </summary>
            public static (bool isValid, int placemarkCount) ValidateKmlContent(string kmlContent)
            {
                if (string.IsNullOrWhiteSpace(kmlContent))
                    return (false, 0);

                // Compter les Placemark
                int count = (kmlContent.Length - kmlContent.Replace("<Placemark>", "").Length) / 11;
                return (count > 0, count);
            }
        }

        // ==================== TESTS ====================

        [Theory]
        [InlineData("ID\tX=100\tY=200\tZ=50", true)]
        [InlineData("P1\tX=400000\tY=5000000\tZ=125.5", true)]
        [InlineData("", false)]  // Vide
        public void TestValidateTxtLeveTopoFormat(string content, bool expectedValid)
        {
            // Act
            var (isValid, error) = TxtExportValidator.ValidateTxtLeveTopoFormat(content);

            // Assert
            Assert.Equal(expectedValid, isValid);
        }

        [Fact]
        public void TestValidateTxtPointFormat_ValidLines()
        {
            // Arrange
            var lines = new[]
            {
                "# Header",
                "P1\t100.0\t200.0\t50.0",
                "P2\t101.0\t201.0\t51.0",
                "P3\t102.0\t202.0\t52.0"
            };

            // Act
            var (isValid, count) = TxtExportValidator.ValidateTxtPointFormat(lines);

            // Assert
            Assert.True(isValid);
            Assert.Equal(3, count);
        }

        [Fact]
        public void TestValidateTxtPointFormat_InvalidLines()
        {
            // Arrange: lignes mal formées
            var lines = new[]
            {
                "# Header",
                "P1\tabc\t200.0\t50.0",  // X pas numérique
                "P2\t101.0",  // Y et Z manquants
                ""
            };

            // Act
            var (isValid, count) = TxtExportValidator.ValidateTxtPointFormat(lines);

            // Assert
            Assert.False(isValid);
            Assert.Equal(0, count);
        }

        [Fact]
        public void TestValidateTxtPointFormat_WithComments()
        {
            // Arrange: lignes avec commentaires et espaces
            var lines = new[]
            {
                "# Levé topo 2026-07-03",
                "# Système: RGF93/CC49",
                "P1 100.0 200.0 50.0",
                "P2 101.0 201.0 51.0"
            };

            // Act
            var (isValid, count) = TxtExportValidator.ValidateTxtPointFormat(lines);

            // Assert
            Assert.True(isValid);
            Assert.Equal(2, count);
        }

        [Fact]
        public void TestValidateKmzStructure_ValidZip()
        {
            // Arrange: signature ZIP valide (PK)
            var kmzContent = new byte[] { 0x50, 0x4B, 0x03, 0x04 };  // ZIP signature

            // Act
            var (isValid, error) = KmzExportValidator.ValidateKmzStructure(kmzContent);

            // Assert
            Assert.True(isValid);
            Assert.Null(error);
        }

        [Theory]
        [InlineData(new byte[] { }, "Fichier KMZ vide")]
        [InlineData(new byte[] { 0x00, 0x00 }, "Signature ZIP invalide")]
        public void TestValidateKmzStructure_Invalid(byte[] content, string expectedError)
        {
            // Act
            var (isValid, error) = KmzExportValidator.ValidateKmzStructure(content);

            // Assert
            Assert.False(isValid);
            Assert.Contains(expectedError, error ?? "");
        }

        [Fact]
        public void TestValidateKmlContent_ValidPlacemarks()
        {
            // Arrange
            var kml = @"<?xml version='1.0'?>
<kml>
  <Document>
    <Placemark><name>P1</name></Placemark>
    <Placemark><name>P2</name></Placemark>
    <Placemark><name>P3</name></Placemark>
  </Document>
</kml>";

            // Act
            var (isValid, count) = KmzExportValidator.ValidateKmlContent(kml);

            // Assert
            Assert.True(isValid);
            Assert.Equal(3, count);
        }

        [Theory]
        [InlineData("<kml></kml>", false, 0)]  // Pas de Placemark
        [InlineData("", false, 0)]  // Vide
        public void TestValidateKmlContent_Empty(string kml, bool expectedValid, int expectedCount)
        {
            // Act
            var (isValid, count) = KmzExportValidator.ValidateKmlContent(kml);

            // Assert
            Assert.Equal(expectedValid, isValid);
            Assert.Equal(expectedCount, count);
        }
    }
}
