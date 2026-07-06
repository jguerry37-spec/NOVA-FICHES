using Xunit;
using System;

namespace NovaFiches.Tests
{
    /// <summary>
    /// Tests critiques pour les conversions de coordonnées LandXML.
    /// RÈGLE MÉTIER : LandXML Leica = Y X Z (Northing Easting Height)
    /// Nova-Fiches interne = X Y Z (Easting Northing Height)
    /// </summary>
    public class CoordinateTests
    {
        private static class CoordinateConverter
        {
            /// <summary>
            /// Convertit LandXML (Y X Z) en interne (X Y Z)
            /// </summary>
            public static (double X, double Y, double Z) ConvertYxzToXyz(double y, double x, double z)
            {
                return (X: x, Y: y, Z: z);  // Inversion Y et X
            }

            /// <summary>
            /// Inverse : interne (X Y Z) en LandXML (Y X Z)
            /// </summary>
            public static (double Y, double X, double Z) ConvertXyzToYxz(double x, double y, double z)
            {
                return (Y: y, X: x, Z: z);  // Inversion X et Y
            }
        }

        // ==================== TESTS ====================

        [Fact]
        public void TestConvertYxzToXyz_SimpleCase()
        {
            // Arrange
            double landxmlY = 100.0;  // Northing
            double landxmlX = 200.0;  // Easting
            double landxmlZ = 50.0;   // Height

            // Act
            var (x, y, z) = CoordinateConverter.ConvertYxzToXyz(landxmlY, landxmlX, landxmlZ);

            // Assert: X/Y doivent être inversés
            Assert.Equal(200.0, x);  // Easting = 200
            Assert.Equal(100.0, y);  // Northing = 100
            Assert.Equal(50.0, z);   // Height = 50
        }

        [Theory]
        [InlineData(100.0, 200.0, 0.0)]    // Z = 0 (littoral)
        [InlineData(100.0, 200.0, -50.0)]  // Z négatif (bassin)
        [InlineData(100.0, 200.0, 2500.0)] // Z positif (montagne)
        public void TestConvertYxzToXyz_VariousZ(double y, double x, double z)
        {
            // Act
            var (resultX, resultY, resultZ) = CoordinateConverter.ConvertYxzToXyz(y, x, z);

            // Assert
            Assert.Equal(x, resultX);
            Assert.Equal(y, resultY);
            Assert.Equal(z, resultZ);
        }

        [Fact]
        public void TestConvertXyzToYxz_ReverseConversion()
        {
            // Arrange (interne)
            double internalX = 700000.0;  // Easting RGF93/CC49
            double internalY = 6250000.0; // Northing
            double internalZ = 125.0;     // Hauteur

            // Act
            var (y, x, z) = CoordinateConverter.ConvertXyzToYxz(internalX, internalY, internalZ);

            // Assert: Doit correspondre à LandXML Y X Z
            Assert.Equal(6250000.0, y);  // Northing
            Assert.Equal(700000.0, x);   // Easting
            Assert.Equal(125.0, z);      // Height
        }

        [Fact]
        public void TestCoordinateConversion_RoundTrip()
        {
            // Arrange: partant de coordonnées LandXML
            double origY = 5000000.0;
            double origX = 450000.0;
            double origZ = 150.0;

            // Act: convertir dans les deux sens
            var (x, y, z) = CoordinateConverter.ConvertYxzToXyz(origY, origX, origZ);
            var (backY, backX, backZ) = CoordinateConverter.ConvertXyzToYxz(x, y, z);

            // Assert: doit revenir aux coordonnées originales
            Assert.Equal(origY, backY);
            Assert.Equal(origX, backX);
            Assert.Equal(origZ, backZ);
        }

        [Theory]
        [InlineData(100.0, 200.0, 50.0)]      // Cas nominal
        [InlineData(-100.0, -200.0, -10.0)]   // Négatifs (peu courant mais possible)
        [InlineData(1234567.89, 234567.89, 123.456)]  // Décimales
        public void TestConvertYxzToXyz_PreservesValues(double y, double x, double z)
        {
            // Act
            var (resultX, resultY, resultZ) = CoordinateConverter.ConvertYxzToXyz(y, x, z);

            // Assert: Pas de perte de précision
            Assert.Equal(x, resultX);
            Assert.Equal(y, resultY);
            Assert.Equal(z, resultZ);
        }

        [Fact]
        public void TestConvertYxzToXyz_LargeDecimalPlaces()
        {
            // Arrange: coordonnées avec beaucoup de décimales (très réaliste)
            double y = 6250123.456789;
            double x = 450234.123456;
            double z = 125.789;

            // Act
            var (resultX, resultY, resultZ) = CoordinateConverter.ConvertYxzToXyz(y, x, z);

            // Assert
            Assert.Equal(x, resultX);
            Assert.Equal(y, resultY);
            Assert.Equal(z, resultZ);
        }
    }
}
