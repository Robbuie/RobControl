using System.Net;
using RobControl.Core.Backup;
using RobControl.Core.Insight;
using RobControl.Core.Robots;
using RobControl.Core.Sites;
using Xunit;

namespace RobControl.Tests;

/// <summary>Verifying backups against their manifests, backup health, retention, and retrying a failed scheduled backup.</summary>
public sealed class BackupHealthTests : IDisposable
{
    private static readonly DateTimeOffset Day1 = new(2026, 9, 1, 8, 0, 0, TimeSpan.Zero);
    private readonly TempFolder _temp = new();

    public void Dispose() => _temp.Dispose();

    private static Robot R(string name) => new(name, IPAddress.Parse("10.0.0.1"));

    [Fact]
    public void AnUntouchedBackupVerifiesIntact()
    {
        RobotBackup b = FakeBackup.Write(_temp.Path, "R1-01", Day1, ("MAIN.LS", "/PROG MAIN\n"), ("NUMREG.VA", "[1] = 5\n"));
        File.WriteAllText(Path.Combine(b.Set.FolderPath, BackupRunner.TranscriptFileName), "220 ready");

        BackupVerification v = BackupVerifier.Verify(b.Set);

        Assert.True(v.IsIntact);
        Assert.Equal(2, v.FilesChecked);
        Assert.Empty(v.Unlisted);
        Assert.StartsWith("Intact", v.Summary, StringComparison.Ordinal);
    }

    [Fact]
    public void AnEditedFileAndADeletedFileAreBothCaught()
    {
        RobotBackup b = FakeBackup.Write(_temp.Path, "R1-01", Day1, ("MAIN.LS", "/PROG MAIN\n"), ("WELD_A.LS", "/PROG WELD_A\n"), ("NUMREG.VA", "[1] = 5\n"));
        // Same length, different content: only the hash can tell.
        File.WriteAllText(Path.Combine(b.Set.FolderPath, "MD", "MAIN.LS"), "/PROG MAIX\r\n");
        File.Delete(Path.Combine(b.Set.FolderPath, "MD", "NUMREG.VA"));
        File.WriteAllText(Path.Combine(b.Set.FolderPath, "MD", "NOTES.TXT"), "added by hand");

        BackupVerification v = BackupVerifier.Verify(b.Set);

        Assert.False(v.IsIntact);
        Assert.Equal(["MD/MAIN.LS"], v.Changed);
        Assert.Equal(["MD/NUMREG.VA"], v.Missing);
        Assert.Equal(["MD/NOTES.TXT"], v.Unlisted);
        Assert.StartsWith("DAMAGED", v.Summary, StringComparison.Ordinal);
        Assert.Contains("Do not restore", v.Summary, StringComparison.Ordinal);
    }

    [Fact]
    public void ATruncatedFileIsCaughtBySize()
    {
        RobotBackup b = FakeBackup.Write(_temp.Path, "R1-01", Day1, ("MAIN.LS", "/PROG MAIN\nline 2\n"));
        File.WriteAllText(Path.Combine(b.Set.FolderPath, "MD", "MAIN.LS"), "/PROG");

        Assert.Equal(["MD/MAIN.LS"], BackupVerifier.Verify(b.Set).Changed);
    }

    [Fact]
    public void VerifyAllReportsFoldersItCannotCheckInsteadOfSkippingThem()
    {
        FakeBackup.Write(_temp.Path, "R1-01", Day1, ("MAIN.LS", "x"));
        FakeBackup.Write(_temp.Path, "R1-01", Day1.AddDays(1), ("MAIN.LS", "y"));
        string robotFolder = Path.Combine(_temp.Path, ArchiveNames.RobotFolder("R1-01"));
        Directory.CreateDirectory(Path.Combine(robotFolder, "2026-09-05_120000" + ArchiveNames.InProgressSuffix));
        Directory.CreateDirectory(Path.Combine(robotFolder, "copied in"));

        IReadOnlyList<BackupVerification> all = BackupVerifier.VerifyAll(new BackupArchive(_temp.Path), [R("R1-01")]);

        Assert.Equal(4, all.Count);
        Assert.Equal(2, all.Count(v => v.IsIntact));
        Assert.Contains(all, v => v.Problem is { } p && p.Contains("interrupted", StringComparison.Ordinal));
        Assert.Contains(all, v => v.Problem is { } p && p.Contains("manifest", StringComparison.Ordinal));

        BackupVerification newest = Assert.Single(BackupVerifier.VerifyAll(new BackupArchive(_temp.Path), [R("R1-01")], newestOnly: true));
        Assert.Equal(ArchiveNames.Stamp(Day1.AddDays(1)), newest.FolderName);
    }

    [Fact]
    public void AManifestPointingOutsideItsFolderIsAProblemNotARead()
    {
        RobotBackup b = FakeBackup.Write(_temp.Path, "R1-01", Day1, ("MAIN.LS", "x"));
        BackupManifest bad = b.Set.Manifest with { Files = [new BackupFileRecord("md:", "x", "../../escape.txt", 1, "00")] };

        BackupVerification v = BackupVerifier.Verify(new BackupSet(b.Set.FolderPath, bad));

        Assert.False(v.IsIntact);
        Assert.NotNull(v.Problem);
    }

    [Theory]
    [InlineData(new[] { "c" }, 2.0, BackupHealth.Ok)]
    [InlineData(new[] { "f", "c" }, 2.0, BackupHealth.Failing)]
    [InlineData(new[] { "f", "p", "c" }, 2.0, BackupHealth.Failing)]
    [InlineData(new[] { "c" }, 10.0, BackupHealth.Stale)]
    [InlineData(new[] { "f", "c" }, 10.0, BackupHealth.Stale)]
    [InlineData(new[] { "f", "f" }, 2.0, BackupHealth.Never)]
    [InlineData(new string[0], 2.0, BackupHealth.Never)]
    public void HealthReadsTheHistoryNewestFirst(string[] outcomesNewestFirst, double newestCompleteAgeDays, BackupHealth expected)
    {
        DateTimeOffset now = Day1.AddDays(30);
        DateTimeOffset completeAt = now.AddDays(-newestCompleteAgeDays);
        for (int i = 0; i < outcomesNewestFirst.Length; i++)
        {
            BackupOutcome outcome = outcomesNewestFirst[i] switch { "c" => BackupOutcome.Complete, "p" => BackupOutcome.Partial, _ => BackupOutcome.Failed };
            // Each newer attempt an hour after the one before it; the first complete one at completeAt.
            int completeIndex = Array.IndexOf(outcomesNewestFirst, "c");
            DateTimeOffset at = completeIndex < 0 ? now.AddHours(-1 - i) : completeAt.AddHours(completeIndex - i);
            FakeBackup.Write(_temp.Path, "R1-01", at, outcome, ("MAIN.LS", "x" + i));
        }

        InventoryRow row = Assert.Single(FleetInventory.Build([(R("R1-01"), null)], new BackupArchive(_temp.Path)));

        Assert.Equal(expected, row.Health(now, staleAfterDays: 7));
        Assert.Equal(outcomesNewestFirst.TakeWhile(o => o != "c").Count(), row.FailedSinceLastComplete);
    }

    [Fact]
    public void RetentionKeepsTheNewestCompleteBackupsAndAnythingAfterThem()
    {
        // Oldest first: c1 f2 c3 c4 f5 c6 f7  - keep 2 complete (c6, c4) and everything newer (f5, f7).
        string[] history = ["c", "f", "c", "c", "f", "c", "f"];
        for (int i = 0; i < history.Length; i++)
        {
            FakeBackup.Write(_temp.Path, "R1-01", Day1.AddDays(i), history[i] == "c" ? BackupOutcome.Complete : BackupOutcome.Failed, ("MAIN.LS", "x" + i));
        }

        FakeBackup.Write(_temp.Path, "R2-01", Day1, ("MAIN.LS", "only one"));

        RetentionPlan plan = RetentionPlan.Build(new BackupArchive(_temp.Path), [R("R1-01"), R("R2-01")], keep: 2);

        Assert.Equal([Day1, Day1.AddDays(1), Day1.AddDays(2)], plan.Remove.Select(r => r.StartedUtc).ToArray());
        Assert.All(plan.Remove, r => Assert.Equal("R1-01", r.Robot));
        Assert.True(plan.Bytes > 0);

        // Building a plan deletes nothing.
        Assert.Equal(7, new BackupArchive(_temp.Path).List(R("R1-01")).Count);
    }

    [Fact]
    public void RetentionLeavesARobotWithFewBackupsAlone()
    {
        FakeBackup.Write(_temp.Path, "R1-01", Day1, BackupOutcome.Failed, ("MAIN.LS", "a"));
        FakeBackup.Write(_temp.Path, "R1-01", Day1.AddDays(1), ("MAIN.LS", "b"));

        Assert.Empty(RetentionPlan.Build(new BackupArchive(_temp.Path), [R("R1-01")], keep: 1).Remove);
    }

    [Fact]
    public async Task AFailedScheduledBackupIsRetriedAndTheRetryIsKept()
    {
        await using SimRig rig = SimRig.Start(Fixtures.Profile(Fixtures.Folder(Fixtures.Synthetic)));
        rig.Sim.Faults.RefuseFiles["SYSMAST.SV"] = true;
        var runner = new BackupRunner(new BackupArchive(_temp.Path), "test", rig.Sink);
        var attempts = new List<BackupOutcome>();
        IReadOnlyList<Robot>? retried = null;

        IReadOnlyList<BackupResult> results = await new FleetBackup(runner).RunAsync(
            [rig.Robot],
            finished: (_, r) => { lock (attempts) { attempts.Add(r.Outcome); } },
            retries: 2,
            retryDelay: TimeSpan.FromMilliseconds(1100),
            retrying: (robots, _) =>
            {
                retried = robots;
                rig.Sim.Faults.RefuseFiles.Clear(); // the controller recovers before the retry
            });

        Assert.Equal(BackupOutcome.Complete, Assert.Single(results).Outcome);
        Assert.Equal([BackupOutcome.Partial, BackupOutcome.Complete], attempts);
        Assert.Equal(rig.Robot, Assert.Single(retried!));

        // Both attempts are in the archive: the retry does not hide the failure.
        Assert.Equal(2, new BackupArchive(_temp.Path).List(rig.Robot).Count);
        Assert.Empty(rig.Refused);
    }

    [Fact]
    public async Task NoRetriesMeansOneAttempt()
    {
        await using SimRig rig = SimRig.Start(Fixtures.Profile(Fixtures.Folder(Fixtures.Synthetic)));
        rig.Sim.Faults.RefuseFiles["SYSMAST.SV"] = true;
        var runner = new BackupRunner(new BackupArchive(_temp.Path), "test");
        bool retried = false;

        IReadOnlyList<BackupResult> results = await new FleetBackup(runner).RunAsync([rig.Robot], retrying: (_, _) => retried = true);

        Assert.Equal(BackupOutcome.Partial, Assert.Single(results).Outcome);
        Assert.False(retried);
    }

    [Fact]
    public void NewSiteSettingsHaveSafeDefaultsAndAreClamped()
    {
        SiteSettings fresh = new SiteSettings { Name = "P" }.Normalised("x");
        Assert.Equal(7, fresh.StaleAfterDays);
        Assert.Equal(1, fresh.ScheduleRetries);
        Assert.Equal(0, fresh.KeepBackups);

        SiteSettings wild = new SiteSettings { Name = "P", StaleAfterDays = 0, ScheduleRetries = 50, RetryDelayMinutes = -3, KeepBackups = -1 }.Normalised("x");
        Assert.Equal(1, wild.StaleAfterDays);
        Assert.Equal(3, wild.ScheduleRetries);
        Assert.Equal(1, wild.RetryDelayMinutes);
        Assert.Equal(0, wild.KeepBackups);
    }
}
