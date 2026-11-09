using System.IO.Compression;
using System.Net;
using RobControl.Core.Capture;
using RobControl.Core.Live;
using RobControl.Core.Transports.Http;
using RobControl.RobotSim;
using Xunit;

namespace RobControl.Tests;

public sealed class ControllerCaptureTests : IDisposable
{
    private readonly TempFolder _temp = new();

    public void Dispose() => _temp.Dispose();

    private static CaptureOptions Quick => new() { Tool = "RobControl test", Pause = TimeSpan.Zero };

    [Fact]
    public async Task CapturesFollowingTheControllersOwnIndexAndSendsOnlyReads()
    {
        await using SimRig rig = SimRig.Start(Fixtures.Profile(Fixtures.Folder(Fixtures.Synthetic)));
        CaptureResult result = await ControllerCapture.RunAsync(rig.Robot, _temp.Path, Quick, events: rig.Sink);

        Assert.Empty(rig.Refused);
        Assert.True(Directory.Exists(result.Folder));
        Assert.StartsWith("R2-14_", Path.GetFileName(result.Folder), StringComparison.Ordinal);

        // The home page and the index were read; what they link under /MD/ was fetched...
        Assert.Contains(result.Pages, p => p.Path == "/" && p.IsOk);
        Assert.Contains(result.Pages, p => p.Path == "/MD/INDEX_ER.HTM" && p.IsOk);
        Assert.Contains(result.Pages, p => p.Path == "/MD/MAIN.LS" && p.IsOk);
        Assert.Contains(result.Pages, p => p.Path == "/MD/IOSTATE.DG" && p.IsOk);

        // ...and nothing else: no KAREL, no KCL beyond the two SHOWs, no other device, no binary.
        Assert.DoesNotContain(result.Pages, p => p.Path.Contains("KAREL", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(result.Pages, p => p.Path.Contains("/FR/", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(result.Pages, p => p.Path.EndsWith(".TP", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(result.Pages, p => p.Path.Contains("boot", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(result.Pages.Count, result.Pages.Select(p => p.Path.ToUpperInvariant()).Distinct().Count());

        // FTP: logged in, a handful copied.
        Assert.Contains("logged in", result.FtpSummary, StringComparison.Ordinal);
        Assert.True(File.Exists(Path.Combine(result.Folder, "MD", "MAIN.TP")));
        Assert.True(File.Exists(Path.Combine(result.Folder, "_raw", "ftp-transcript.txt")));

        // The report says what each parser made of it.
        Assert.Contains("IOSTATE.DG:", result.Report, StringComparison.Ordinal);
        Assert.Contains("registers", result.Report, StringComparison.Ordinal);
        Assert.Contains("first try", result.Report, StringComparison.Ordinal);
        Assert.True(File.Exists(Path.Combine(result.Folder, "report.txt")));
        Assert.True(File.Exists(Path.Combine(result.Folder, "capture.json")));

        // One event row.
        RobControl.Core.Events.RobotEvent entry = Assert.Single(rig.Events, e => e.Message.StartsWith("Capture:", StringComparison.Ordinal));
        Assert.Contains("pages read", entry.Message, StringComparison.Ordinal);

        // Zipped, with the folder inside.
        Assert.NotNull(result.ZipPath);
        using ZipArchive zip = ZipFile.OpenRead(result.ZipPath!);
        string top = Path.GetFileName(result.Folder);
        Assert.Contains(zip.Entries, e => e.FullName.Replace('\\', '/') == $"{top}/profile.json");
        Assert.Contains(zip.Entries, e => e.FullName.Replace('\\', '/') == $"{top}/MD/IOSTATE.DG");
    }

    [Fact]
    public async Task ACaptureReplaysInTheSimulator()
    {
        string captured;
        await using (SimRig rig = SimRig.Start(Fixtures.Profile(Fixtures.Folder(Fixtures.Synthetic))))
        {
            captured = (await ControllerCapture.RunAsync(rig.Robot, _temp.Path, Quick with { Zip = false })).Folder;
        }

        SimProfile profile = SimProfile.Load(captured);
        Assert.Contains("R-30iB Plus", profile.Banner, StringComparison.Ordinal);
        Assert.True(profile.Kcl.ContainsKey("SHOW VAR $VERSION"));

        await using SimRig replay = SimRig.Start(profile);
        using var web = new ControllerWebClient(IPAddress.Loopback, replay.Sim.HttpPort);
        LiveSnapshot live = await LiveReader.ReadAsync(replay.Robot, web);
        Assert.True(live.Io.Count > 0);
        HttpExchange home = await web.GetAsync("/", HttpResourceKind.DiagnosticFiles);
        Assert.Equal(200, home.StatusCode);
        Assert.Empty(replay.Refused);
    }

    [Fact]
    public async Task ASupportBundleCarriesTheBackupAndTheEventLog()
    {
        string backup = Path.Combine(_temp.Path, "src", "2026-11-08_101500");
        Directory.CreateDirectory(Path.Combine(backup, "MD"));
        File.WriteAllText(Path.Combine(backup, "manifest.json"), "{}");
        File.WriteAllText(Path.Combine(backup, "MD", "MAIN.LS"), "/PROG MAIN");

        await using SimRig rig = SimRig.Start(Fixtures.Profile(Fixtures.Folder(Fixtures.Synthetic)));
        CaptureResult result = await ControllerCapture.RunAsync(rig.Robot, Path.Combine(_temp.Path, "out"),
            Quick with { BackupFolder = backup, EventLogCsv = "Time,Robot\n" });

        Assert.True(File.Exists(Path.Combine(result.Folder, "backup", "2026-11-08_101500", "MD", "MAIN.LS")));
        Assert.Equal("Time,Robot\n", File.ReadAllText(Path.Combine(result.Folder, "events.csv")).TrimStart('﻿'));
    }

    [Fact]
    public async Task LockedDiagnosticFilesStopThePagesAndSaySo()
    {
        await using SimRig rig = SimRig.Start(Fixtures.Profile(Fixtures.Folder(Fixtures.Synthetic), p => p with { DiagnosticFilesLocked = true }));
        CaptureResult result = await ControllerCapture.RunAsync(rig.Robot, _temp.Path, Quick);

        Assert.Contains(result.Pages, p => p.Error is { } e && e.StartsWith("locked", StringComparison.Ordinal));
        Assert.True(result.Pages.Count <= 3);
        Assert.Contains("Host Comm > HTTP", result.Report, StringComparison.Ordinal);
        Assert.Contains("\"diagnosticFilesLocked\": true", File.ReadAllText(Path.Combine(result.Folder, "profile.json")), StringComparison.Ordinal);
    }

    [Fact]
    public async Task NoWebServerStopsAfterTheFirstPage()
    {
        var probe = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        int closed = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();

        var robot = new RobControl.Core.Robots.Robot("R9", IPAddress.Loopback) { FtpPort = closed, HttpPort = closed };
        CaptureResult result = await ControllerCapture.RunAsync(robot, _temp.Path, Quick);
        Assert.Single(result.Pages);
        Assert.Contains("refused", result.FtpSummary, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("<a href=\"/MD/ERRALL.LS\">x</a>", "ERRALL.LS")]
    [InlineData("<a href='errall.ls'>x</a>", "ERRALL.LS")]
    [InlineData("<A HREF=/md/IOSTATE.DG>x</A>", "IOSTATE.DG")]
    [InlineData("<a href=\"../MD/CURPOS.DG\">x</a>", "CURPOS.DG")]
    [InlineData("<a href=\"/KAREL/PROG?a=1\">x</a>", "")]
    [InlineData("<a href=\"/KCL/SHOW%20VAR\">x</a>", "")]
    [InlineData("<a href=\"/FR/X.LS\">x</a>", "")]
    [InlineData("<a href=\"/MD/MAIN.TP\">x</a>", "")]
    [InlineData("<a href=\"http://h/MD/X.DG\">x</a>", "")]
    [InlineData("<a href=\"/MD/X.DG?refresh\">x</a>", "")]
    public void TheIndexYieldsOnlyPlainMdListings(string html, string expected)
    {
        IReadOnlyList<string> files = CaptureIndex.Files(html, out _);
        Assert.Equal(expected, string.Join(",", files));
    }

    [Fact]
    public void IndexPagesAreFollowedOneLevel()
    {
        CaptureIndex.Files("<a href=\"/MD/INDEX_ER.HTM\">e</a><a href=\"/MD/INDEX_DG.HTM\">d</a><a href=\"/MD/OTHER.HTM\">o</a>", out IReadOnlyList<string> indexes);
        Assert.Equal(["INDEX_ER.HTM", "INDEX_DG.HTM"], indexes);
    }
}
