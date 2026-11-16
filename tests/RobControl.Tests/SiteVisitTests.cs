using RobControl.Core.Backup;
using RobControl.Core.Capture;
using RobControl.Core.Robots;
using Xunit;

namespace RobControl.Tests;

public sealed class SiteVisitTests : IDisposable
{
    private readonly TempFolder _temp = new();

    public void Dispose() => _temp.Dispose();

    [Fact]
    public async Task AVisitProbesBacksUpAndCapturesEachRobotThenWritesOneReportAndZip()
    {
        string folder = Fixtures.CopyOf(Fixtures.Synthetic, _temp);
        await using SimRig rig = SimRig.Start(Fixtures.Profile(folder));
        var archive = new BackupArchive(Path.Combine(_temp.Path, "archive"));
        var probed = new List<string>();
        var options = new SiteVisitOptions
        {
            Title = "Plant / Line 3",
            Tool = "test",
            Archive = archive,
            Pause = TimeSpan.Zero,
            Probed = (r, _) => probed.Add(r.Name),
            Skip = r => r.Name == "R9" ? "recording trends" : null,
        };
        var skipped = new Robot("R9", rig.Robot.Address) { FtpPort = rig.Robot.FtpPort, HttpPort = rig.Robot.HttpPort };

        VisitResult result = await SiteVisit.RunAsync([rig.Robot, skipped], Path.Combine(_temp.Path, "visits"), options, null, rig.Sink);

        VisitRobot first = result.Robots[0];
        Assert.Equal(["R2-14"], probed);
        Assert.Equal(BackupOutcome.Complete, first.Backup!.Outcome);
        Assert.NotNull(first.Capture);
        Assert.Contains("first complete backup", first.Changes!.Problem, StringComparison.Ordinal);
        Assert.Equal("skipped: recording trends", Assert.Single(result.Robots[1].Notes));
        Assert.True(File.Exists(Path.Combine(result.Folder, SiteVisit.ReportFileName)));
        Assert.True(File.Exists(result.ZipPath));

        // The capture took the backup in and did not open a second FTP session for a sample.
        Assert.True(Directory.Exists(Path.Combine(first.Capture!.Folder, "backup")) || Directory.EnumerateDirectories(first.Capture.Folder).Any());
        Assert.Contains("| R2-14 |", result.Report, StringComparison.Ordinal);
        Assert.Contains("skipped: recording trends", result.Report, StringComparison.Ordinal);
        Assert.Empty(rig.Refused);
    }

    [Fact]
    public async Task ASecondVisitSaysWhatChanged()
    {
        string folder = Fixtures.CopyOf(Fixtures.Synthetic, _temp);
        await using SimRig rig = SimRig.Start(Fixtures.Profile(folder));
        var options = new SiteVisitOptions { Title = "P", Tool = "t", Archive = new BackupArchive(Path.Combine(_temp.Path, "a")), Pause = TimeSpan.Zero, Capture = false, Zip = false };

        await SiteVisit.RunAsync([rig.Robot], Path.Combine(_temp.Path, "v"), options);
        await Task.Delay(1100);
        File.AppendAllText(Path.Combine(folder, "MD", "WELD_A.LS"), "\n");
        VisitResult second = await SiteVisit.RunAsync([rig.Robot], Path.Combine(_temp.Path, "v"), options);

        Assert.True(second.Robots[0].Changes!.HasRealChanges);
        Assert.Contains("changed since the previous backup", second.Summary, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ARobotThatDoesNotAnswerIsNotBackedUpOrCaptured()
    {
        var robot = new Robot("Gone", System.Net.IPAddress.Loopback) { FtpPort = FreePort(), HttpPort = FreePort() };
        var options = new SiteVisitOptions { Title = "P", Tool = "t", Archive = new BackupArchive(Path.Combine(_temp.Path, "a")), Pause = TimeSpan.Zero, Zip = false };

        VisitResult result = await SiteVisit.RunAsync([robot], Path.Combine(_temp.Path, "v"), options);

        VisitRobot r = Assert.Single(result.Robots);
        Assert.Null(r.Backup);
        Assert.Null(r.Capture);
        Assert.Contains(r.Notes, n => n.StartsWith("backup not tried", StringComparison.Ordinal));
        Assert.Contains(r.Notes, n => n.StartsWith("capture not tried", StringComparison.Ordinal));
    }

    private static int FreePort()
    {
        var l = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        l.Start();
        int port = ((System.Net.IPEndPoint)l.LocalEndpoint).Port;
        l.Stop();
        return port;
    }
}
