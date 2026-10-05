using RobControl.Core.Backup;
using Xunit;

namespace RobControl.Tests;

public class ArchiveNamesTests
{
    [Theory]
    [InlineData("MAIN.TP")]
    [InlineData("SUMMARY.DG")]
    [InlineData("-BCKED8-.VA")]
    [InlineData("SYSMAST.SV")]
    public void OrdinaryControllerNamesAreSafe(string name) => Assert.True(ArchiveNames.IsSafeFileName(name, out _));

    [Theory]
    [InlineData("../evil.TP")]
    [InlineData("..\\evil.TP")]
    [InlineData("C:\\Windows\\x.dll")]
    [InlineData("/etc/passwd")]
    [InlineData("a/b.TP")]
    [InlineData("..")]
    [InlineData("CON")]
    [InlineData("nul.txt")]
    [InlineData("trailing.")]
    [InlineData("bad\u0001name")]
    [InlineData("")]
    public void HostileOrUnwritableNamesAreRefused(string name)
    {
        Assert.False(ArchiveNames.IsSafeFileName(name, out string? problem));
        Assert.NotNull(problem);
    }

    [Fact]
    public void DevicePrefixIsStripped() => Assert.Equal("MAIN.TP", ArchiveNames.StripDevice("md:MAIN.TP", "md:"));

    [Theory]
    [InlineData("R2-14", "R2-14")]
    [InlineData("Cell 3 / Robot 2", "Cell 3 _ Robot 2")]
    [InlineData("CON", "_CON")]
    [InlineData("name.", "name")]
    public void RobotNamesAreMadeSafeNotRefused(string name, string folder) =>
        Assert.Equal(folder, ArchiveNames.RobotFolder(name));

    [Fact]
    public void DeviceFolderAndLabel()
    {
        Assert.Equal("MD", ArchiveNames.DeviceFolder("md:"));
        Assert.Equal("MD:", ArchiveNames.DeviceLabel("md:"));
    }
}
