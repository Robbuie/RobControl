using RobControl.Core.Events;
using RobControl.Core.Persistence;
using RobControl.Core.Live;
using RobControl.Core.Transports.Http;
using Xunit;

namespace RobControl.Tests;

public sealed class LiveReaderTests : IDisposable
{
    private readonly TempFolder _temp = new();

    public void Dispose() => _temp.Dispose();

    [Fact]
    public async Task ALiveReadGetsAlarmsAndIoAndSaysWhichPagesTheControllerDoesNotHave()
    {
        string folder = Fixtures.CopyOf(Fixtures.Synthetic, _temp);
        File.WriteAllText(Path.Combine(folder, "MD", "ERRACT.LS"), "SYNTHETIC\n05-NOV-26 08:01  SRVO-002 Teach pendant E-stop\n");
        await using SimRig rig = SimRig.Start(Fixtures.Profile(folder));
        using var web = new ControllerWebClient(rig.Robot.Address, rig.Robot.HttpPort);

        LiveSnapshot snapshot = await LiveReader.ReadAsync(rig.Robot, web, rig.Sink);

        AlarmEntryAssert(Assert.Single(snapshot.ActiveAlarms), "SRVO-002");
        Assert.Equal(3, snapshot.RecentAlarms.Count);
        Assert.Equal(1.0, snapshot.Io["DI[1]"]);
        Assert.Equal(LiveFileStatus.NotAvailable, snapshot.File("CURPOS.DG")!.Status);
        Assert.Equal(LiveFileStatus.Ok, snapshot.File("ERRALL.LS")!.Status);
        Assert.Contains("3 of 6 pages read", snapshot.Summary, StringComparison.Ordinal);

        // One event row for the whole read, and only GETs on the wire.
        RobotEvent row = Assert.Single(rig.Events, e => e.Category == EventCategory.Http);
        Assert.Contains("Live read", row.Message, StringComparison.Ordinal);
        Assert.Empty(rig.Refused);
    }

    [Fact]
    public async Task LockedDiagnosticFilesAreReportedNotThrown()
    {
        string folder = Fixtures.CopyOf(Fixtures.Synthetic, _temp);
        await using SimRig rig = SimRig.Start(Fixtures.Profile(folder, p => p with { DiagnosticFilesLocked = true }));
        using var web = new ControllerWebClient(rig.Robot.Address, rig.Robot.HttpPort);

        LiveSnapshot snapshot = await LiveReader.ReadAsync(rig.Robot, web, rig.Sink);

        Assert.All(snapshot.Files, f => Assert.Equal(LiveFileStatus.Locked, f.Status));
        Assert.Contains("Host Comm", snapshot.Files[0].Message!, StringComparison.Ordinal);
        Assert.Equal(EventSeverity.Warn, Assert.Single(rig.Events).Severity);
    }

    private static void AlarmEntryAssert(Core.Insight.AlarmEntry entry, string code)
    {
        Assert.Equal(code, entry.Code);
        Assert.Equal(new DateTime(2026, 11, 5, 8, 1, 0), entry.When);
    }
}
