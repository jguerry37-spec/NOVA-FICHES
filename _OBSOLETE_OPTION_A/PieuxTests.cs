using Xunit;
using System;
using System.Collections.Generic;
using System.Linq;

namespace NovaFiches.Tests
{
    /// <summary>
    /// Tests de la logique métier pour les pieux.
    /// RÈGLE: Les noms de pieux sont génériques :
    ///   T62.1 → pieu T62
    ///   ABC-12.3 → pieu ABC-12
    ///   Z3-P12.2 → pieu Z3-P12
    /// </summary>
    public class PieuxTests
    {
        private class PieuxPoint
        {
            public string FullName { get; set; } = "";
            public double X { get; set; }
            public double Y { get; set; }
            public double Z { get; set; }
            public bool IsActive { get; set; } = true;
        }

        private static class PieuxGrouper
        {
            /// <summary>
            /// Extrait le nom générique d'un pieu (racine avant dernier indice).
            /// T62.1 → T62
            /// ABC-12.3 → ABC-12
            /// Z3-P12.2 → Z3-P12
            /// </summary>
            public static string ExtractPieuxName(string fullName)
            {
                if (string.IsNullOrWhiteSpace(fullName))
                    return fullName;

                // Chercher le dernier point
                int lastDotIndex = fullName.LastIndexOf('.');
                if (lastDotIndex > 0)
                {
                    // Vérifier que ce qui suit est numérique (indice de mesure)
                    string suffix = fullName.Substring(lastDotIndex + 1);
                    if (suffix.All(char.IsDigit))
                    {
                        return fullName.Substring(0, lastDotIndex);
                    }
                }

                // Pas de format .digit → retourner tel quel
                return fullName;
            }

            /// <summary>
            /// Regroupe les points par pieu et calcule le centre.
            /// Retourne un dictionnaire pieu → liste de points
            /// </summary>
            public static Dictionary<string, List<PieuxPoint>> GroupPieuxPoints(List<PieuxPoint> points)
            {
                var grouped = new Dictionary<string, List<PieuxPoint>>();

                foreach (var point in points)
                {
                    string pieuxName = ExtractPieuxName(point.FullName);

                    if (!grouped.ContainsKey(pieuxName))
                    {
                        grouped[pieuxName] = new List<PieuxPoint>();
                    }

                    grouped[pieuxName].Add(point);
                }

                return grouped;
            }

            /// <summary>
            /// Calcule le centre (X, Y) d'un pieu à partir de ses points.
            /// Seuls les points actifs sont considérés.
            /// </summary>
            public static (double CenterX, double CenterY, int CountActive) CalculatePieuxCenter(
                List<PieuxPoint> points)
            {
                var activePoints = points.Where(p => p.IsActive).ToList();

                if (activePoints.Count == 0)
                    return (0, 0, 0);

                double sumX = activePoints.Sum(p => p.X);
                double sumY = activePoints.Sum(p => p.Y);

                double centerX = sumX / activePoints.Count;
                double centerY = sumY / activePoints.Count;

                return (centerX, centerY, activePoints.Count);
            }
        }

        // ==================== TESTS ====================

        [Theory]
        [InlineData("T62.1", "T62")]       // Cas standard
        [InlineData("ABC-12.3", "ABC-12")] // Avec tiret
        [InlineData("Z3-P12.2", "Z3-P12")] // Tiret + lettre
        [InlineData("P.1.A", "P.1.A")]     // Pas de .digit → inchangé
        [InlineData("T62", "T62")]         // Sans indice
        [InlineData("TEST.10", "TEST")]    // Grand indice
        public void TestExtractPieuxName(string input, string expected)
        {
            // Act
            string result = PieuxGrouper.ExtractPieuxName(input);

            // Assert
            Assert.Equal(expected, result);
        }

        [Fact]
        public void TestExtractPieuxName_EmptyOrNull()
        {
            // Act & Assert
            Assert.Equal("", PieuxGrouper.ExtractPieuxName(""));
            Assert.Equal("", PieuxGrouper.ExtractPieuxName(null ?? ""));
        }

        [Fact]
        public void TestGroupPieuxPoints_SimpleCase()
        {
            // Arrange
            var points = new List<PieuxPoint>
            {
                new() { FullName = "T62.1", X = 100.0, Y = 200.0, Z = 50.0 },
                new() { FullName = "T62.2", X = 101.0, Y = 201.0, Z = 50.5 },
                new() { FullName = "T63.1", X = 200.0, Y = 300.0, Z = 55.0 }
            };

            // Act
            var grouped = PieuxGrouper.GroupPieuxPoints(points);

            // Assert
            Assert.Equal(2, grouped.Count);  // 2 pieux distincts
            Assert.True(grouped.ContainsKey("T62"));
            Assert.True(grouped.ContainsKey("T63"));
            Assert.Equal(2, grouped["T62"].Count);
            Assert.Equal(1, grouped["T63"].Count);
        }

        [Fact]
        public void TestGroupPieuxPoints_MixedNames()
        {
            // Arrange: noms variés
            var points = new List<PieuxPoint>
            {
                new() { FullName = "ABC-12.1", X = 100.0, Y = 200.0 },
                new() { FullName = "ABC-12.2", X = 101.0, Y = 201.0 },
                new() { FullName = "Z3-P12.1", X = 500.0, Y = 600.0 }
            };

            // Act
            var grouped = PieuxGrouper.GroupPieuxPoints(points);

            // Assert
            Assert.Equal(2, grouped.Count);
            Assert.Equal(2, grouped["ABC-12"].Count);
            Assert.Equal(1, grouped["Z3-P12"].Count);
        }

        [Fact]
        public void TestCalculatePieuxCenter_AllActive()
        {
            // Arrange: 3 points actifs
            var points = new List<PieuxPoint>
            {
                new() { FullName = "T62.1", X = 100.0, Y = 200.0, IsActive = true },
                new() { FullName = "T62.2", X = 104.0, Y = 204.0, IsActive = true },
                new() { FullName = "T62.3", X = 106.0, Y = 206.0, IsActive = true }
            };

            // Act
            var (centerX, centerY, count) = PieuxGrouper.CalculatePieuxCenter(points);

            // Assert: moyenne = (100+104+106)/3 = 103.33, (200+204+206)/3 = 203.33
            Assert.Equal((100 + 104 + 106) / 3.0, centerX);
            Assert.Equal((200 + 204 + 206) / 3.0, centerY);
            Assert.Equal(3, count);
        }

        [Fact]
        public void TestCalculatePieuxCenter_WithInactive()
        {
            // Arrange: 4 points, 1 inactif
            var points = new List<PieuxPoint>
            {
                new() { FullName = "T62.1", X = 100.0, Y = 200.0, IsActive = true },
                new() { FullName = "T62.2", X = 104.0, Y = 204.0, IsActive = true },
                new() { FullName = "T62.3", X = 9999.0, Y = 9999.0, IsActive = false },  // Erreur
            };

            // Act
            var (centerX, centerY, count) = PieuxGrouper.CalculatePieuxCenter(points);

            // Assert: devrait ignorer le point inactif
            Assert.Equal((100 + 104) / 2.0, centerX);
            Assert.Equal((200 + 204) / 2.0, centerY);
            Assert.Equal(2, count);
        }

        [Fact]
        public void TestCalculatePieuxCenter_AllInactive()
        {
            // Arrange
            var points = new List<PieuxPoint>
            {
                new() { FullName = "T62.1", X = 100.0, Y = 200.0, IsActive = false },
                new() { FullName = "T62.2", X = 104.0, Y = 204.0, IsActive = false }
            };

            // Act
            var (centerX, centerY, count) = PieuxGrouper.CalculatePieuxCenter(points);

            // Assert: pas de point actif → centre = 0, 0
            Assert.Equal(0, centerX);
            Assert.Equal(0, centerY);
            Assert.Equal(0, count);
        }

        [Fact]
        public void TestFullPieuxWorkflow()
        {
            // Arrange: cas complet
            var points = new List<PieuxPoint>
            {
                new() { FullName = "T62.1", X = 100.0, Y = 200.0, Z = 50.0, IsActive = true },
                new() { FullName = "T62.2", X = 104.0, Y = 204.0, Z = 50.5, IsActive = true },
                new() { FullName = "T62.3", X = 9999.0, Y = 9999.0, Z = 99.0, IsActive = false },
                new() { FullName = "T63.1", X = 500.0, Y = 600.0, Z = 55.0, IsActive = true }
            };

            // Act
            var grouped = PieuxGrouper.GroupPieuxPoints(points);
            var centersT62 = PieuxGrouper.CalculatePieuxCenter(grouped["T62"]);
            var centersT63 = PieuxGrouper.CalculatePieuxCenter(grouped["T63"]);

            // Assert
            Assert.Equal(2, grouped.Count);
            Assert.Equal(2, centersT62.CountActive);  // T62 a 2 points actifs
            Assert.Equal(1, centersT63.CountActive);  // T63 a 1 point actif
            Assert.Equal((100 + 104) / 2.0, centersT62.CenterX);  // Centre T62
            Assert.Equal(500.0, centersT63.CenterX);  // Centre T63
        }
    }
}
