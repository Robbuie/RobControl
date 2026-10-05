using RobControl.Core.Trending;
using Xunit;

namespace RobControl.Tests;

public class TrendParserTests
{
    [Fact]
    public void RegistersComeOutOfTheSyntheticListing()
    {
        IReadOnlyDictionary<int, double> values = RegisterFileParser.Parse(Fixtures.ReadText(Fixtures.Synthetic, "MD/NUMREG.VA"));
        Assert.Equal(0, values[1]);
        Assert.Equal(1287, values[2]);
        Assert.Equal(14.5, values[3]);
    }

    [Theory]
    [InlineData("  [7] = -2.5E+01  ''", 7, -25)]
    [InlineData("[12]=3", 12, 3)]
    [InlineData("   [ 9 ] =  0.125000  'x'", 9, 0.125)]
    public void RegisterLineVariants(string line, int n, double expected) =>
        Assert.Equal(expected, RegisterFileParser.Parse(line)[n]);

    [Fact]
    public void UninitialisedRegistersAreSkippedNotFatal() =>
        Assert.Empty(RegisterFileParser.Parse("  [5] = *Uninit*  ''"));

    [Fact]
    public void IoComesOutOfTheSyntheticPage()
    {
        IReadOnlyDictionary<string, double> io = IoStateParser.Parse(Fixtures.ReadText(Fixtures.Synthetic, "MD/IOSTATE.DG"));
        Assert.Equal(1, io["DI[1]"]);
        Assert.Equal(0, io["DI[2]"]);
        Assert.Equal(0, io["DO[1]"]);
        Assert.Equal(1, io["UI[1]"]);
    }

    [Theory]
    [InlineData("DO[12] = ON", "DO[12]", 1)]
    [InlineData("GI[ 3]   45", "GI[3]", 45)]
    [InlineData("<td>RI[2]</td> OFF", "RI[2]", 0)]
    public void IoVariants(string text, string key, double expected) =>
        Assert.Equal(expected, IoStateParser.Parse(text)[key]);

    [Theory]
    [InlineData("1287  'Weld count'", 1287)]
    [InlineData("TRUE", 1)]
    [InlineData("off", 0)]
    [InlineData("'3.5'", 3.5)]
    public void Scalars(string text, double expected)
    {
        Assert.True(ScalarParser.TryParse(text, out double v));
        Assert.Equal(expected, v);
    }

    [Theory]
    [InlineData("NaN")]
    [InlineData("'hello'")]
    [InlineData("")]
    public void NonNumbersAreNot(string text) => Assert.False(ScalarParser.TryParse(text, out _));
}
