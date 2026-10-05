using RobControl.Core.Controllers;
using Xunit;

namespace RobControl.Tests;

public class ControllerIdentityParserTests
{
    [Fact]
    public void SyntheticSummaryGivesEverything()
    {
        ControllerIdentity id = ControllerIdentityParser.Parse(Fixtures.ReadText(Fixtures.Synthetic, "MD/SUMMARY.DG"));
        Assert.Equal(ControllerGeneration.R30iBPlus, id.Generation);
        Assert.False(id.GenerationInferred);
        Assert.Equal("V9.40P/23", id.SoftwareVersion);
        Assert.Equal(9, id.VersionMajor);
        Assert.Equal(40, id.VersionMinor);
        Assert.Equal("SpotTool+", id.Application);
        Assert.Equal("R-2000iC/210F", id.RobotModel);
        Assert.Equal("F123456", id.FNumber);
    }

    [Theory]
    [InlineData("V9.40P/23", ControllerGeneration.R30iBPlus)]
    [InlineData("V8.30P/16", ControllerGeneration.R30iB)]
    [InlineData("V7.70P/41", ControllerGeneration.R30iA)]
    [InlineData("V10.10P/05", ControllerGeneration.R50iA)]
    public void GenerationIsInferredFromVersionWhenNoCabinetIsNamed(string text, ControllerGeneration expected)
    {
        ControllerIdentity id = ControllerIdentityParser.Parse("FTP server ready " + text);
        Assert.Equal(expected, id.Generation);
        Assert.True(id.GenerationInferred);
        Assert.StartsWith("probably", id.Describe(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("R-30iB Mate Plus", ControllerGeneration.R30iBPlus)]
    [InlineData("R-30iB Compact Plus", ControllerGeneration.R30iBPlus)]
    [InlineData("R-30iB", ControllerGeneration.R30iB)]
    [InlineData("R-30iA Mate", ControllerGeneration.R30iA)]
    [InlineData("R30iB", ControllerGeneration.R30iB)]
    public void CabinetNamesAreRecognised(string text, ControllerGeneration expected) =>
        Assert.Equal(expected, ControllerIdentityParser.Parse("Controller: " + text + " running").Generation);

    [Fact]
    public void SpotToolPlusWinsOverSpotTool() =>
        Assert.Equal("SpotTool+", ControllerIdentityParser.Parse("SpotTool+ V9.40P/23").Application);

    [Fact]
    public void AStatedGenerationBeatsAnInferredOneWhenMerging()
    {
        ControllerIdentity inferred = ControllerIdentityParser.Parse("V8.30P/16");
        ControllerIdentity stated = ControllerIdentityParser.Parse("R-30iB Plus");
        ControllerIdentity merged = inferred.Merge(stated);
        Assert.Equal(ControllerGeneration.R30iBPlus, merged.Generation);
        Assert.False(merged.GenerationInferred);
        Assert.Equal("V8.30P/16", merged.SoftwareVersion);
    }

    [Fact]
    public void EmptyTextIsUnknownNotAnError()
    {
        Assert.True(ControllerIdentityParser.Parse(null).IsEmpty);
        Assert.True(ControllerIdentityParser.Parse("   ").IsEmpty);
        Assert.Equal("Not identified yet", ControllerIdentity.Unknown.Describe());
    }
}
