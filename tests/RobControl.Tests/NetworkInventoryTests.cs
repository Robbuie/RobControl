using System.Net;
using RobControl.Core.Backup;
using RobControl.Core.Insight;
using RobControl.Core.Robots;
using Xunit;

namespace RobControl.Tests;

public sealed class NetworkInventoryTests : IDisposable
{
    private static readonly DateTimeOffset Day1 = new(2026, 9, 1, 8, 0, 0, TimeSpan.Zero);
    private readonly TempFolder _temp = new();

    public void Dispose() => _temp.Dispose();

    // Shaped like the variable listings: header lines with the value inline, and array/field lines under a header.
    private const string HostListing = """
        $HOSTNAME  Storage: CMOS  Access: RW  : STRING[33] = 'R1-01'
        $HOSTENT  Storage: CMOS  Access: RW  : ARRAY[16] OF HOST_ENT_T
          Field: $HOSTENT[1].$H_NAME  Access: RW: STRING[33] = 'PLC1'
          Field: $HOSTENT[1].$H_ADDR  Access: RW: STRING[16] = '10.20.1.1'
        $TMI_ROUTER  Storage: CMOS  Access: RW  : STRING[16] = '10.20.1.254'
        $TMI_SNMASK  Storage: CMOS  Access: RW  : STRING[16] = '255.255.255.0'
        $ETHERNET_ADDR  Storage: CMOS  Access: RO  : STRING[18] = '00-E0-E4-12-34-AB'
        $HOSTCFG[1].$IP_ADDR  Storage: CMOS  Access: RW  : STRING[16] = '10.20.1.54'
        $HOSTCFG[2].$IP_ADDR  Storage: CMOS  Access: RW  : STRING[16] = '0.0.0.0'
        $SOME_ADDR_COUNT  Storage: CMOS  Access: RW  : INTEGER = 4
        """;

    [Fact]
    public void ReadsHostnameAddressesSubnetRouterAndMacFromTheListings()
    {
        FakeBackup.Write(_temp.Path, "R1-01", Day1, ("SYSHOST.VA", HostListing), ("MAIN.LS", "/PROG MAIN"));

        NetworkRow row = Assert.Single(NetworkInventory.Build(new BackupArchive(_temp.Path), [new Robot("R1-01", IPAddress.Parse("10.20.1.54"))]));

        Assert.Equal("R1-01", row.Hostname);
        Assert.Equal("255.255.255.0", row.SubnetMask);
        Assert.Equal("10.20.1.254", row.Router);
        Assert.Equal("00:E0:E4:12:34:AB", row.Mac);
        Assert.Contains("10.20.1.54", row.Addresses);
        Assert.Contains("10.20.1.1", row.Addresses);
        Assert.DoesNotContain("0.0.0.0", row.Addresses);
        Assert.False(row.AddressMismatch);
        Assert.All(row.Settings, s => Assert.Equal("MD/SYSHOST.VA", s.File));
        Assert.Contains(row.Settings, s => s.Variable == "$HOSTENT[1].$H_ADDR" && s.Value == "10.20.1.1");
    }

    [Fact]
    public void AnAddressTheBackupNeverMentionsIsFlagged()
    {
        FakeBackup.Write(_temp.Path, "R1-01", Day1, ("SYSHOST.VA", HostListing));

        NetworkRow row = Assert.Single(NetworkInventory.Build(new BackupArchive(_temp.Path), [new Robot("R1-01", IPAddress.Parse("10.20.1.99"))]));

        Assert.True(row.AddressMismatch);
        Assert.Contains("10.20.1.99", row.Note, StringComparison.Ordinal);
    }

    [Fact]
    public void ARobotWithoutABackupOrWithoutRecognisableVariablesSaysSo()
    {
        FakeBackup.Write(_temp.Path, "R2-01", Day1, ("SYSVARS.VA", "$VERSION  Storage: SHADOW  Access: RO  : STRING[37] = 'V9.40P/23'"));
        NetworkRow[] rows = [.. NetworkInventory.Build(new BackupArchive(_temp.Path),
            [new Robot("R1-01", IPAddress.Parse("10.0.0.1")), new Robot("R2-01", IPAddress.Parse("10.0.0.2"))])];

        Assert.Null(rows[0].Backup);
        Assert.Contains("No complete backup", rows[0].Note, StringComparison.Ordinal);
        Assert.Empty(rows[1].Settings);
        Assert.False(rows[1].AddressMismatch);
        Assert.Contains("Phase 0", rows[1].Note, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("$TMI_ROUTER", NetworkInventory.RouterKind)]
    [InlineData("$HOSTENT[3].$H_NAME", NetworkInventory.HostnameKind)]
    [InlineData("$HOSTENT[3].$H_ADDR", NetworkInventory.AddressKind)]
    [InlineData("$ETHERNET_ADDR", NetworkInventory.MacKind)]
    [InlineData("$VERSION", null)]
    [InlineData("$MNUTOOL[1,1]", null)]
    public void VariableNamesAreRecognisedByTheirLastField(string variable, string? kind) => Assert.Equal(kind, NetworkInventory.Kind(variable));

    [Theory]
    [InlineData(NetworkInventory.AddressKind, "10.0.0.5", "10.0.0.5")]
    [InlineData(NetworkInventory.AddressKind, "300.1.1.1", null)]
    [InlineData(NetworkInventory.AddressKind, "*uninit*", null)]
    [InlineData(NetworkInventory.MacKind, "00e0e41234ab", "00:E0:E4:12:34:AB")]
    [InlineData(NetworkInventory.MacKind, "00:E0:E4:12:34", null)]
    [InlineData(NetworkInventory.HostnameKind, "has space", null)]
    public void ValuesMustLookLikeWhatTheNamePromises(string kind, string value, string? expected) =>
        Assert.Equal(expected, NetworkInventory.Normalise(kind, value));

    [Fact]
    public void TheCsvHasASummaryAndEveryValueWithItsSource()
    {
        FakeBackup.Write(_temp.Path, "R1-01", Day1, ("SYSHOST.VA", HostListing));
        string csv = NetworkInventory.ToCsv(NetworkInventory.Build(new BackupArchive(_temp.Path), [new Robot("R1-01", IPAddress.Parse("10.20.1.54"))]));

        Assert.StartsWith("Robot,Address in robot list,Hostname", csv, StringComparison.Ordinal);
        Assert.Contains("R1-01,MAC address,$ETHERNET_ADDR,00:E0:E4:12:34:AB,MD/SYSHOST.VA", csv, StringComparison.Ordinal);
    }
}
