using System.Security.Cryptography;
using RobControl.Core.Backup;
using RobControl.Core.Persistence;
using RobControl.RobotSim;
using Xunit;

namespace RobControl.Tests;

public class BackupRunnerTests
{
    private const string Tool = "RobControl test";

    [Fact]
    public async Task ACompleteBackupHasEveryFileByteForByte()
    {
        using var temp = new TempFolder();
        await using SimRig rig = SimRig.Start(Fixtures.Profile(Fixtures.Folder(Fixtures.Synthetic)));
        var runner = new BackupRunner(new BackupArchive(temp.Path), Tool, rig.Sink);

        BackupResult result = await runner.RunAsync(rig.Robot);

        Assert.Equal(BackupOutcome.Complete, result.Outcome);
        Assert.NotNull(result.FolderPath);
        Assert.DoesNotContain(ArchiveNames.IncompleteSuffix, result.FolderPath, StringComparison.Ordinal);

        string source = Path.Combine(Fixtures.Folder(Fixtures.Synthetic), "MD");
        string[] expected = [.. Directory.GetFiles(source).Select(Path.GetFileName).Order()!];
        Assert.Equal(expected, result.Manifest.Files.Select(f => f.Name).Order().ToArray());

        foreach (BackupFileRecord file in result.Manifest.Files)
        {
            byte[] original = File.ReadAllBytes(Path.Combine(source, file.Name));
            byte[] copied = File.ReadAllBytes(Path.Combine(result.FolderPath!, "MD", file.Name));
            Assert.Equal(original, copied);
            Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(original)), file.Sha256);
        }

        Assert.True(File.Exists(Path.Combine(result.FolderPath!, BackupManifest.FileName)));
        Assert.True(File.Exists(Path.Combine(result.FolderPath!, BackupRunner.TranscriptFileName)));
        Assert.Equal("R-30iB Plus, SpotTool+ V9.40P/23", result.Manifest.Identity.Describe());
        Assert.Empty(rig.Refused);
    }

    [Fact]
    public async Task OnlyReadCommandsReachTheController()
    {
        using var temp = new TempFolder();
        await using SimRig rig = SimRig.Start(Fixtures.Profile(Fixtures.Folder(Fixtures.Synthetic)));
        await new BackupRunner(new BackupArchive(temp.Path), Tool).RunAsync(rig.Robot, new BackupOptions { Devices = ["md:", "fr:"] });

        string[] verbs = [.. rig.Sim.FtpCommands.Select(c => c.Split(' ')[0]).Distinct()];
        Assert.All(verbs, v => Assert.Contains(v, FtpVerbs));
        Assert.Empty(rig.Refused);
    }

    private static readonly string[] FtpVerbs = ["USER", "PASS", "TYPE", "CWD", "PASV", "NLST", "RETR", "QUIT"];

    [Fact]
    public async Task ARefusedFileMakesItPartialAndMarkedIncomplete()
    {
        using var temp = new TempFolder();
        await using SimRig rig = SimRig.Start(Fixtures.Profile(Fixtures.Folder(Fixtures.Synthetic)));
        rig.Sim.Faults.RefuseFiles["SYSMAST.SV"] = true;

        BackupResult result = await new BackupRunner(new BackupArchive(temp.Path), Tool, rig.Sink).RunAsync(rig.Robot);

        Assert.Equal(BackupOutcome.Partial, result.Outcome);
        Assert.EndsWith(ArchiveNames.IncompleteSuffix, result.FolderPath, StringComparison.Ordinal);
        BackupFileProblem failure = Assert.Single(result.Manifest.Failures);
        Assert.Equal("SYSMAST.SV", failure.Name);
        Assert.Contains("550", failure.Reason, StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(result.FolderPath!, "MD", "SYSMAST.SV")));
        Assert.Contains(rig.Events, e => e.Severity == EventSeverity.Warn && e.Message.Contains("partial", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ADroppedConnectionIsRetriedOnAFreshSession()
    {
        using var temp = new TempFolder();
        await using SimRig rig = SimRig.Start(Fixtures.Profile(Fixtures.Folder(Fixtures.Synthetic)));
        rig.Sim.Faults.DropOnceOnFile["NUMREG.VA"] = true;

        BackupResult result = await new BackupRunner(new BackupArchive(temp.Path), Tool).RunAsync(rig.Robot);

        Assert.Equal(BackupOutcome.Complete, result.Outcome);
        byte[] original = File.ReadAllBytes(Path.Combine(Fixtures.Folder(Fixtures.Synthetic), "MD", "NUMREG.VA"));
        Assert.Equal(original, File.ReadAllBytes(Path.Combine(result.FolderPath!, "MD", "NUMREG.VA")));
        Assert.True(rig.Sim.FtpCommands.Count(c => c.StartsWith("USER", StringComparison.Ordinal)) >= 2);
    }

    [Fact]
    public async Task HostileListedNamesAreSkippedAndNothingLandsOutsideTheBackup()
    {
        using var temp = new TempFolder();
        string archiveRoot = Path.Combine(temp.Path, "archive");
        await using SimRig rig = SimRig.Start(Fixtures.Profile(Fixtures.Folder(Fixtures.Synthetic)));
        rig.Sim.Faults.ExtraListedNames.Add("../../escaped.TP");
        rig.Sim.Faults.ExtraListedNames.Add("..\\..\\escaped2.TP");

        BackupResult result = await new BackupRunner(new BackupArchive(archiveRoot), Tool).RunAsync(rig.Robot);

        Assert.Equal(BackupOutcome.Partial, result.Outcome);
        Assert.Equal(2, result.Manifest.Skipped.Count);
        Assert.DoesNotContain(rig.Sim.FtpCommands, c => c.Contains("escaped", StringComparison.Ordinal));
        Assert.Empty(Directory.GetFiles(temp.Path, "escaped*", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task DevicePrefixedListingsStillBackUp()
    {
        using var temp = new TempFolder();
        await using SimRig rig = SimRig.Start(Fixtures.Profile(Fixtures.Folder(Fixtures.Synthetic), p => p with { NlstWithDevicePrefix = true }));

        BackupResult result = await new BackupRunner(new BackupArchive(temp.Path), Tool).RunAsync(rig.Robot);

        Assert.Equal(BackupOutcome.Complete, result.Outcome);
        Assert.Contains(result.Manifest.Files, f => f.Name == "SUMMARY.DG");
    }

    [Fact]
    public async Task AnUnreachableRobotIsAFailedBackupThatSaysWhy()
    {
        using var temp = new TempFolder();
        var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        int port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();

        var robot = new Core.Robots.Robot("Gone", System.Net.IPAddress.Loopback) { FtpPort = port };
        BackupResult result = await new BackupRunner(new BackupArchive(temp.Path), Tool).RunAsync(robot);

        Assert.Equal(BackupOutcome.Failed, result.Outcome);
        Assert.Contains("refused", result.Manifest.Summary!, StringComparison.Ordinal);
        Assert.EndsWith(ArchiveNames.IncompleteSuffix, result.FolderPath, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheArchiveListsBackupsNewestFirstAndFindsTheLatestComplete()
    {
        using var temp = new TempFolder();
        await using SimRig rig = SimRig.Start(Fixtures.Profile(Fixtures.Folder(Fixtures.Synthetic)));
        var archive = new BackupArchive(temp.Path);
        var runner = new BackupRunner(archive, Tool);

        BackupResult first = await runner.RunAsync(rig.Robot);
        rig.Sim.Faults.RefuseFiles["MAIN.TP"] = true;
        BackupResult second = await runner.RunAsync(rig.Robot);

        IReadOnlyList<BackupSet> sets = archive.List(rig.Robot);
        Assert.Equal(2, sets.Count);
        Assert.Equal(second.FolderPath, sets[0].FolderPath);
        Assert.Equal(first.FolderPath, archive.LatestComplete(rig.Robot)!.FolderPath);
    }

    [Fact]
    public async Task ACancelledBackupIsMarkedIncomplete()
    {
        using var temp = new TempFolder();
        await using SimRig rig = SimRig.Start(Fixtures.Profile(Fixtures.Folder(Fixtures.Synthetic)));
        using var cts = new CancellationTokenSource();
        var progress = new SyncProgress<BackupProgress>(p =>
        {
            if (p.FileIndex == 3)
            {
                cts.Cancel();
            }
        });

        BackupResult result = await new BackupRunner(new BackupArchive(temp.Path), Tool)
            .RunAsync(rig.Robot, progress: progress, cancellationToken: cts.Token);

        Assert.Equal(BackupOutcome.Cancelled, result.Outcome);
        Assert.EndsWith(ArchiveNames.IncompleteSuffix, result.FolderPath, StringComparison.Ordinal);
    }

    /// <summary>Progress&lt;T&gt; posts to a sync context; this calls straight through, so the test is deterministic.</summary>
    private sealed class SyncProgress<T>(Action<T> handler) : IProgress<T>
    {
        public void Report(T value) => handler(value);
    }
}
