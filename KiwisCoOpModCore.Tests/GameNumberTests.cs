using System;
using System.Globalization;
using KiwisCoOpModCore;
using Xunit;

namespace KiwisCoOpModCore.Tests
{
    // Numbers sent to the game use "." for decimals on a comma-decimal PC (UPGRADE_PLAN.md B1).
    public class GameNumberTests : IDisposable
    {
        private readonly CultureInfo previousCulture = CultureInfo.CurrentCulture;

        public GameNumberTests()
        {
            CultureInfo.CurrentCulture = new CultureInfo("fr-FR");
        }

        public void Dispose()
        {
            CultureInfo.CurrentCulture = previousCulture;
        }

        [Fact]
        public void CurrentCulture_UsesCommaDecimals()
        {
            // Guards the other tests: they would pass trivially if fr-FR fell back to the invariant culture.
            Assert.Equal("12,5", 12.5f.ToString());
        }

        [Fact]
        public void Vector_UsesDotDecimals()
        {
            Assert.Equal("12.5 -0.25 1024", new Vector(12.5f, -0.25f, 1024f).ToString());
        }

        [Fact]
        public void Angle_UsesDotDecimals()
        {
            Assert.Equal("-89.5 179.75 0", new Angle(-89.5f, 179.75f, 0f).ToString());
        }

        [Fact]
        public void TeleportCommand_UsesDotDecimals()
        {
            // Built like AlyxGamemode's teleport and setlocation commands.
            Vector origin = new(12.5f, -3.75f, 64.125f);
            Angle angles = new(0.5f, 90.25f, 0f);

            Assert.Equal("kcom_teleportangles 12.5 -3.75 64.125 0.5 90.25 0", "kcom_teleportangles " + origin + " " + angles);
        }

        [Fact]
        public void Format_UsesDotDecimals()
        {
            Assert.Equal("12.5", GameNumber.Format(12.5f));
            Assert.Equal("-1234567", GameNumber.Format(-1234567));
        }

        [Theory]
        [InlineData("12.5", 12.5f)]
        [InlineData("-0.25", -0.25f)]
        [InlineData("1024", 1024f)]
        [InlineData("1e-05", 0.00001f)]
        public void ParseFloat_ReadsDotDecimals(string text, float expected)
        {
            Assert.Equal(expected, GameNumber.ParseFloat(text));
        }

        [Fact]
        public void ParseFloat_RoundTripsFormat()
        {
            float value = 123.456789f;

            Assert.Equal(value, GameNumber.ParseFloat(GameNumber.Format(value)));
        }

        [Fact]
        public void ParseFloat_RejectsCommaDecimals()
        {
            // With the default style (AllowThousands), the invariant culture would read "12,5" as 125.
            Assert.Throws<FormatException>(() => GameNumber.ParseFloat("12,5"));
        }

        [Fact]
        public void ParseInt_ReadsInvariantDigits()
        {
            Assert.Equal(4, GameNumber.ParseInt("4"));
        }
    }
}
