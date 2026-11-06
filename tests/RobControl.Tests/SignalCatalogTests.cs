using System.Net;
using RobControl.Core.Insight;
using RobControl.Core.Transports.Http;
using RobControl.Core.Trending;
using Xunit;

namespace RobControl.Tests;

public class SignalCatalogTests
{
    [Fact]
    public void RegistersKeepTheirCommentsAndValues()
    {
        IReadOnlyList<SignalEntry> registers = SignalCatalog.Registers(Fixtures.ReadText(Fixtures.Synthetic, "MD/NUMREG.VA"));
        SignalEntry weld = Assert.Single(registers, r => r.Address.Text == "R[2]");
        Assert.Equal("Weld count", weld.Comment);
        Assert.Equal("1287", weld.Value);
        Assert.Null(Assert.Single(registers, r => r.Address.Text == "R[1]").Comment);
        Assert.Equal("Tip dress interval", Assert.Single(registers, r => r.Address.Text == "R[3]").Comment);
    }

    [Fact]
    public void IoKeepsItsCommentsAndState()
    {
        IReadOnlyList<SignalEntry> io = SignalCatalog.Io(Fixtures.ReadText(Fixtures.Synthetic, "MD/IOSTATE.DG"));
        SignalEntry part = Assert.Single(io, e => e.Address.Text == "DI[1]");
        Assert.Equal("Part present", part.Comment);
        Assert.Equal("ON", part.Value);
        Assert.Equal("*IMSTP", Assert.Single(io, e => e.Address.Text == "UI[1]").Comment);
        Assert.Equal(4, io.Count);
    }

    [Theory]
    [InlineData("<tr><td>DO[ 3]</td><td>OFF</td><td>Gun close</td></tr>", "DO[3]", "Gun close")]
    [InlineData("GI[1] = 37", "GI[1]", null)]
    public void IoVariants(string text, string signal, string? comment)
    {
        SignalEntry entry = Assert.Single(SignalCatalog.Io(text));
        Assert.Equal(signal, entry.Address.Text);
        Assert.Equal(comment, entry.Comment);
    }

    [Fact]
    public void VariablesAreTheOnesHoldingOneNumber()
    {
        string[] lines =
        [
            "$VERSION  Storage: SHADOW  Access: RO  : STRING[37] = 'V9.40P/23'",
            "$SCR.$NUM_GROUP  Storage: CMOS  Access: RO  : INTEGER = 1",
            "[*SYSTEM*]$ANGTOL  Storage: CMOS  Access: RW  : ARRAY[2] OF REAL",
            "    [1] = 1.000000e+01",
            "    [2] = 2.500000e+00",
            "[*SYSTEM*]$MCR  Storage: SHADOW  Access: RW  : MCR_T",
            "  Field: $MCR.$GENOVERRIDE Access: RW: INTEGER = 100",
            "  Field: $MCR.$NAME Access: RW: STRING[8] = 'X'",
            "$DCSS_CPC[1].$ENABLE  Storage: CMOS  Access: RW  : BOOLEAN = TRUE",
            "[*NUMREG*]$NUMREG  Storage: CMOS  Access: RW  : ARRAY[2] OF Numeric Reg",
            "  [1] = 5  'Not a variable'",
        ];

        IReadOnlyList<SignalEntry> found = SignalCatalog.Variables(lines);
        string[] names = [.. found.Select(f => f.Address.Text)];

        Assert.Equal(["$SCR.$NUM_GROUP", "$ANGTOL[1]", "$ANGTOL[2]", "$MCR.$GENOVERRIDE", "$DCSS_CPC[1].$ENABLE"], names);
        Assert.Equal("INTEGER, RO", found[0].Comment);
        Assert.Equal("2.500000e+00", found[2].Value);
        Assert.All(found, f => Assert.Equal(SignalKind.SystemVariable, f.Address.Kind));
    }

    [Fact]
    public void ABackupListsRegistersIoAndVariablesInOrder()
    {
        using var temp = new TempFolder();
        RobotBackup backup = FakeBackup.Write(temp.Path, "R1", DateTimeOffset.UtcNow,
            ("NUMREG.VA", "[*NUMREG*]$NUMREG  Storage: CMOS  Access: RW  : ARRAY[2] OF Numeric Reg\n  [2] = 7  'Count'\n  [1] = 3  ''\n"),
            ("IOSTATE.DG", "DO[  2] OFF  Gun\nDI[  1] ON   Part\n"),
            ("SYSVARS.VA", "$SCR.$NUM_GROUP  Storage: CMOS  Access: RO  : INTEGER = 1\n"));

        SignalListing listing = SignalCatalog.FromBackup(backup.Set);

        Assert.Equal(["R[1]", "R[2]", "DI[1]", "DO[2]", "$SCR.$NUM_GROUP"], listing.Entries.Select(e => e.Address.Text));
        Assert.Empty(listing.Notes);
    }

    [Fact]
    public void ABackupWithoutTheFilesSaysSo()
    {
        using var temp = new TempFolder();
        RobotBackup backup = FakeBackup.Write(temp.Path, "R1", DateTimeOffset.UtcNow, ("MAIN.LS", "/PROG MAIN\n"));

        SignalListing listing = SignalCatalog.FromBackup(backup.Set);

        Assert.Empty(listing.Entries);
        Assert.Contains(listing.Notes, n => n.Contains("NUMREG.VA", StringComparison.Ordinal));
        Assert.Contains(listing.Notes, n => n.Contains("IOSTATE.DG", StringComparison.Ordinal));
    }

    [Fact]
    public void ALaterListingFillsInValuesAndKeepsComments()
    {
        SignalAddress r2 = SignalAddress.Parse("R[2]");
        var backup = new SignalListing([new SignalEntry(r2, "Weld count", "1200")], []);
        var live = new SignalListing([new SignalEntry(r2, null, "1287")], []);

        SignalEntry merged = Assert.Single(SignalCatalog.Merge([backup, live]));

        Assert.Equal("Weld count", merged.Comment);
        Assert.Equal("1287", merged.Value);
    }

    [Fact]
    public async Task AReadOfTheRobotIsTwoGetsAndNothingElse()
    {
        await using SimRig rig = SimRig.Start(Fixtures.Profile(Fixtures.Folder(Fixtures.Synthetic)));
        using var web = new ControllerWebClient(IPAddress.Loopback, rig.Sim.HttpPort);

        SignalListing listing = await SignalCatalog.ReadAsync(web);

        Assert.Contains(listing.Entries, e => e.Address.Text == "R[2]" && e.Comment == "Weld count");
        Assert.Contains(listing.Entries, e => e.Address.Text == "DI[1]" && e.Comment == "Part present");
        Assert.DoesNotContain(listing.Entries, e => e.Address.Kind == SignalKind.SystemVariable);
        Assert.Empty(listing.Notes);
        Assert.Empty(rig.Refused);
    }
}
