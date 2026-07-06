using Xunit;
using System;

namespace NovaFiches.Tests
{
    /// <summary>
    /// Tests des validations input critiques pour Nova-Fiches.
    /// </summary>
    public class ValidationTests
    {
        private static class CoordinateValidator
        {
            /// <summary>
            /// Valide qu'une coordonnée n'est pas aberrante.
            /// Plage France métropole + buffer: ±5 000 000 (Lambert, RGF93, CC)
            /// </summary>
            public static (bool isValid, string? error) ValidateCoordinate(double? x, double? y, double? z = null)
            {
                const double MAX_BOUNDS = 5000000;
                const double MIN_BOUNDS = -5000000;

                if (x.HasValue)
                {
                    if (x < MIN_BOUNDS || x > MAX_BOUNDS)
                        return (false, $"X hors limites: {x}");
                }

                if (y.HasValue)
                {
                    if (y < MIN_BOUNDS || y > MAX_BOUNDS)
                        return (false, $"Y hors limites: {y}");
                }

                if (z.HasValue)
                {
                    // Z doit être entre -500m (fosse/mine) et +5000m (montagne)
                    if (z < -500 || z > 5000)
                        return (false, $"Z hors limites: {z}");
                }

                return (true, null);
            }

            /// <summary>
            /// Valide un nom de pieu générique.
            /// Exemples valides: T62, ABC-12, Z3-P12, etc.
            /// </summary>
            public static bool ValidatePieuxName(string name)
            {
                if (string.IsNullOrWhiteSpace(name))
                    return false;

                // Accepter alphanumériques + tirets + points (avant mesure)
                // Mais rejeter vides, très longs (>50 chars)
                return name.Length <= 50 && 
                       System.Text.RegularExpressions.Regex.IsMatch(name, @"^[A-Za-z0-9\-._]+$");
            }

            /// <summary>
            /// Valide le nom d'un fichier avant export.
            /// Rejette les path traversal, caractères dangereux.
            /// </summary>
            public static (bool isValid, string? error) ValidateFileName(string fileName)
            {
                if (string.IsNullOrWhiteSpace(fileName))
                    return (false, "Nom de fichier vide");

                // Rejeter path traversal
                if (fileName.Contains("..") || fileName.Contains("/") || fileName.Contains("\\"))
                    return (false, "Path traversal détecté");

                // Caractères interdits Windows
                var invalidChars = System.IO.Path.GetInvalidFileNameChars();
                if (fileName.IndexOfAny(invalidChars) >= 0)
                    return (false, "Caractères invalides dans le nom");

                return (true, null);
            }
        }

        // ==================== TESTS ====================

        [Theory]
        [InlineData(100.0, 200.0, 50.0, true)]  // Coordonnées valides
        [InlineData(400000.0, 5000000.0, 100.0, true)]  // Limites France
        [InlineData(9999999.0, 100.0, 50.0, false)]  // X hors limites
        [InlineData(100.0, -9999999.0, 50.0, false)]  // Y hors limites
        public void TestValidateCoordinate_Various(double x, double y, double z, bool expectedValid)
        {
            // Arrange & Act
            var (isValid, error) = CoordinateValidator.ValidateCoordinate(x, y, z);

            // Assert
            Assert.Equal(expectedValid, isValid);
            if (!expectedValid)
                Assert.NotNull(error);
        }

        [Theory]
        [InlineData(100.0, 200.0, 0.0, true)]  // Z = 0 est valide
        [InlineData(100.0, 200.0, -100.0, true)]  // Z négatif (fosse) ok
        [InlineData(100.0, 200.0, 3000.0, true)]  // Z positif (montagne) ok
        [InlineData(100.0, 200.0, -600.0, false)]  // Z trop bas
        [InlineData(100.0, 200.0, 6000.0, false)]  // Z trop haut
        public void TestValidateCoordinate_ZBounds(double x, double y, double z, bool expectedValid)
        {
            // Act
            var (isValid, error) = CoordinateValidator.ValidateCoordinate(x, y, z);

            // Assert
            Assert.Equal(expectedValid, isValid);
        }

        [Theory]
        [InlineData("T62", true)]
        [InlineData("ABC-12", true)]
        [InlineData("Z3-P12", true)]
        [InlineData("P.1.A", true)]
        [InlineData("", false)]  // Vide
        [InlineData(null, false)]  // Null
        [InlineData("X".PadRight(51, 'A'), false)]  // Trop long (>50)
        public void TestValidatePieuxName(string? name, bool expected)
        {
            // Act & Assert
            if (name == null)
                Assert.False(CoordinateValidator.ValidatePieuxName(name ?? ""));
            else
                Assert.Equal(expected, CoordinateValidator.ValidatePieuxName(name));
        }

        [Theory]
        [InlineData("report_2026_07_03.txt", true)]
        [InlineData("points_topo.csv", true)]
        [InlineData("../../../etc/passwd", false)]  // Path traversal
        [InlineData("file\\..\\bad.txt", false)]  // Path traversal Windows
        [InlineData("bad<file>.txt", false)]  // Caractère invalide
        [InlineData("file|name.txt", false)]  // Pipe interdit
        [InlineData("", false)]  // Vide
        public void TestValidateFileName(string fileName, bool expected)
        {
            // Act
            var (isValid, error) = CoordinateValidator.ValidateFileName(fileName);

            // Assert
            Assert.Equal(expected, isValid);
            if (!expected)
                Assert.NotNull(error);
        }

        [Fact]
        public void TestValidateCoordinate_NullValues_AllowedButCaught()
        {
            // Arrangement: coordonnées partielles (c'est valide)
            var (isValid, error) = CoordinateValidator.ValidateCoordinate(x: 100.0, y: null, z: 50.0);

            // Assert: null values sont autorisés (cas courant en 2D)
            Assert.True(isValid);
            Assert.Null(error);
        }
    }
}
