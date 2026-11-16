using RobControl.Core.Backup;
using RobControl.Core.History;
using RobControl.Core.Robots;
using Xunit;

namespace RobControl.Tests;

public sealed class ChangeReportTests : IDisposable
{
    private const string Listing = "/PROG  WELD_A\n/ATTR\nCOMMENT = \"Weld A\";\nMODIFIED = DATE 26-09-30  TIME 14:02:11;\n/MN\n   1:  CALL GUN_OPEN ;\n   2:L P[1] 1500mm/sec CNT100 ;\n/END\n";

    private readonly TempFolder _temp = new();

    public void Dispose() => _temp.Dispose();

    [Fact]
    public void ProgramsAndSettingsCountNoiseAndRegistersDoNot()
    {
        DateTimeOffset t = new(2026, 11, 10, 3, 0, 0, TimeSpan.Zero);
        FakeBackup.Write(_temp.Path, "R1-04", t,
            ("WELD_A.LS", Listing), ("SYSTEM.VA", "a=1\n"), ("NUMREG.VA", "[1]=5\n"), ("IOSTATE.DG", "x\n"), ("ERRALL.LS", "old\n"), ("DCSVRFY.VA", "crc 1\n"));
        FakeBackup.Write(_temp.Path, "R1-04", t.AddDays(1),
            ("WELD_A.LS", Listing.Replace("1500mm/sec", "1200mm/sec", StringComparison.Ordinal).Replace("26-09-30", "26-11-10", StringComparison.Ordinal)),
            ("SYSTEM.VA", "a=1\n"), ("NUMREG.VA", "[1]=9\n"), ("IOSTATE.DG", "y\n"), ("ERRALL.LS", "new\n"), ("DCSVRFY.VA", "crc 2\n"), ("NEW.TP", "bin"));

        RobotChanges changes = ChangeReport.ForRobot(new BackupArchive(_temp.Path), Robot("R1-04"));

        Assert.Null(changes.Problem);
        Assert.True(changes.HasRealChanges);
        Assert.True(changes.HasSafetyChanges);
        FileChangeSummary weld = Assert.Single(changes.Files, f => f.Path == "MD/WELD_A.LS");
        Assert.Equal(ChangeKind.Program, weld.Kind);
        Assert.Equal(2, weld.LinesAdded);
        Assert.Equal(ChangeKind.Values, Assert.Single(changes.Files, f => f.Path == "MD/NUMREG.VA").Kind);
        Assert.Equal(ChangeKind.Noise, Assert.Single(changes.Files, f => f.Path == "MD/IOSTATE.DG").Kind);
        Assert.Equal(ChangeKind.Noise, Assert.Single(changes.Files, f => f.Path == "MD/ERRALL.LS").Kind);
        Assert.Equal(FileChange.Added, Assert.Single(changes.Files, f => f.Path == "MD/NEW.TP").Change);
        Assert.DoesNotContain(changes.Files, f => f.Path == "MD/SYSTEM.VA");
        Assert.Equal("2 programs, 1 setting file, SAFETY (DCS)", changes.Short);

        string md = ChangeReport.ToMarkdown("Plant / Line 3", [changes], t.AddDays(1), "test");
        Assert.Contains("- 2:L P[1] 1500mm/sec CNT100", md, StringComparison.Ordinal);
        Assert.Contains("+ 2:L P[1] 1200mm/sec CNT100", md, StringComparison.Ordinal);
        Assert.DoesNotContain("MODIFIED", md, StringComparison.Ordinal);
        Assert.Contains("**CHANGED**", md, StringComparison.Ordinal);
    }

    [Fact]
    public void TheSiteIgnoreListTurnsAFileIntoNoise()
    {
        DateTimeOffset t = new(2026, 11, 10, 3, 0, 0, TimeSpan.Zero);
        FakeBackup.Write(_temp.Path, "R1", t, ("SYSTEM.VA", "clock=1\n"));
        FakeBackup.Write(_temp.Path, "R1", t.AddHours(8), ("SYSTEM.VA", "clock=2\n"));

        Assert.True(ChangeReport.ForRobot(new BackupArchive(_temp.Path), Robot("R1")).HasRealChanges);
        RobotChanges ignored = ChangeReport.ForRobot(new BackupArchive(_temp.Path), Robot("R1"), ["system.va"]);
        Assert.False(ignored.HasRealChanges);
        Assert.Equal(ChangeKind.Noise, Assert.Single(ignored.Files).Kind);
    }

    [Fact]
    public void OneBackupIsNotAChangeAndIncompleteBackupsAreSkipped()
    {
        DateTimeOffset t = new(2026, 11, 10, 3, 0, 0, TimeSpan.Zero);
        FakeBackup.Write(_temp.Path, "R1", t, ("A.TP", "1"));
        FakeBackup.Write(_temp.Path, "R1", t.AddHours(1), BackupOutcome.Partial, ("A.TP", "2"));
        var archive = new BackupArchive(_temp.Path);

        RobotChanges first = ChangeReport.ForRobot(archive, Robot("R1"));
        Assert.Contains("first complete backup", first.Problem, StringComparison.Ordinal);

        FakeBackup.Write(_temp.Path, "R2", t, ("A.TP", "1"));
        FakeBackup.Write(_temp.Path, "R2", t.AddHours(1), ("A.TP", "1"));
        IReadOnlyList<RobotChanges> all = ChangeReport.ForRobots(archive, [Robot("R1"), Robot("R2"), Robot("R3")]);
        Assert.Equal("no complete backup", all[2].Problem);
        Assert.StartsWith("No program or setting changes on 3 robot(s)", ChangeReport.Headline(all), StringComparison.Ordinal);
    }

    [Fact]
    public void TheHeadlineNamesTheRobotsThatChanged()
    {
        DateTimeOffset t = new(2026, 11, 10, 3, 0, 0, TimeSpan.Zero);
        FakeBackup.Write(_temp.Path, "R1", t, ("A.TP", "1"));
        FakeBackup.Write(_temp.Path, "R1", t.AddHours(1), ("A.TP", "2"));
        FakeBackup.Write(_temp.Path, "R2", t, ("A.TP", "1"));
        FakeBackup.Write(_temp.Path, "R2", t.AddHours(1), ("A.TP", "1"));

        string headline = ChangeReport.Headline(ChangeReport.ForRobots(new BackupArchive(_temp.Path), [Robot("R1"), Robot("R2")]));

        Assert.Equal("1 of 2 robot(s) changed since their previous backup: R1 (1 program).", headline);
    }

    private static Robot Robot(string name) => new(name, System.Net.IPAddress.Parse("10.0.0.1"));
}
