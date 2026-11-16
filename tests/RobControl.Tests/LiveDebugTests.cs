using RobControl.Core.Insight;
using RobControl.Core.Live;
using RobControl.Core.Trending;
using Xunit;

namespace RobControl.Tests;

public sealed class LiveDebugTests
{
    private static readonly DateTimeOffset T = new(2026, 11, 16, 14, 0, 0, TimeSpan.Zero);

    [Fact]
    public void RunCheckNamesAlarmsStopSignalsAndTheUopThatHoldsIt()
    {
        LiveSnapshot s = Snapshot(
            active: [new AlarmEntry("R1", null, "SRVO-007", "External emergency stops", "")],
            io: [Io("UI", 1, 1, "*IMSTP"), Io("UI", 2, 0, "*HOLD"), Io("UI", 3, 1, "*SFSPD"), Io("UI", 8, 0, "Cell enable"),
                 Io("UO", 1, 0, "CMDENBL"), Io("UO", 2, 1, "SYSRDY"), Io("UO", 6, 1, "FAULT"), Io("UO", 8, 0, "TPENBL")],
            stops: [new SafetySignal("External ESTOP", true), new SafetySignal("Fence Open", false)],
            tasks: [new TaskState(1, "MAIN", "PAUSED", 12, "MAIN", "MAIN", [])]);

        IReadOnlyList<RunBlocker> blockers = RunCheck.Evaluate(s);

        Assert.Equal(BlockerLevel.Stop, blockers[0].Level);
        Assert.Contains(blockers, b => b.What == "Active alarm SRVO-007");
        Assert.Contains(blockers, b => b.What == "External ESTOP is TRUE");
        Assert.Contains(blockers, b => b.What == "UI[2] *HOLD is OFF");
        Assert.Contains(blockers, b => b.What == "UI[8] ENBL (listed as 'Cell enable') is OFF");
        Assert.Contains(blockers, b => b.What == "UO[6] FAULT is ON" && b.Level == BlockerLevel.Stop);
        Assert.Contains(blockers, b => b.What == "UO[1] CMDENBL is OFF");
        Assert.Contains(blockers, b => b.What == "Task MAIN is paused");
        Assert.DoesNotContain(blockers, b => b.What.StartsWith("UI[1]", StringComparison.Ordinal));
        Assert.DoesNotContain(blockers, b => b.What.StartsWith("UO[8]", StringComparison.Ordinal));
        Assert.DoesNotContain(blockers, b => b.What.Contains("Fence", StringComparison.Ordinal));
    }

    [Fact]
    public void EveryUiOffIsANoteNotFourBlockers()
    {
        LiveSnapshot s = Snapshot(io: [Io("UI", 1, 0, ""), Io("UI", 2, 0, ""), Io("UI", 3, 0, ""), Io("UI", 8, 0, "")]);

        RunBlocker only = Assert.Single(RunCheck.Evaluate(s));
        Assert.Equal(BlockerLevel.Note, only.Level);
        Assert.Contains("UOP", only.Why, StringComparison.Ordinal);
    }

    [Fact]
    public void WaitCheckFindsTheLineAndTheSignalItIsWaitingOn()
    {
        ProgramListing listing = ProgramListingParser.Parse(
            "/PROG PICK\n/MN\n   1:  WAIT DI[34:Part present]=ON AND DI[35]=OFF ;\n   2:  DO[7:Gun close]=ON ;\n/END".Split('\n'))!;
        LiveSnapshot s = Snapshot(
            io: [Io("DI", 34, 0, "Part present"), Io("DI", 35, 0, "Clamp")],
            tasks: [new TaskState(1, "PICK", "RUNNING", 1, "PICK", "PICK", []), new TaskState(2, "DGMON", "ABORTED", null, null, null, [])],
            programs: [new ProgramInfo("PICK", "TP", [new("Last modified", "01-NOV-26 08:00")])]);

        WaitLine line = Assert.Single(WaitCheck.Evaluate(s, p => p == "PICK" ? (listing, T) : null));

        Assert.Equal("WAIT DI[34:Part present]=ON AND DI[35]=OFF", line.Text);
        Assert.Null(line.Note);
        Assert.True(line.IsWaitingOnASignal);
        WaitSignal di34 = line.Signals.Single(x => x.Point == "DI[34]");
        Assert.Equal("ON", di34.Expected);
        Assert.Equal("OFF", di34.Now);
        Assert.False(di34.Satisfied);
        Assert.True(line.Signals.Single(x => x.Point == "DI[35]").Satisfied);
    }

    [Fact]
    public void WaitCheckWarnsWhenTheProgramChangedAfterTheBackupAndSaysWhenThereIsNoListing()
    {
        ProgramListing listing = ProgramListingParser.Parse("/PROG PICK\n/MN\n   1:  WAIT DI[34]=ON ;\n/END".Split('\n'))!;
        LiveSnapshot s = Snapshot(
            tasks: [new TaskState(1, "PICK", "RUNNING", 1, "PICK", "PICK", []), new TaskState(2, "OTHER", "RUNNING", 5, "OTHER", "OTHER", []), new TaskState(3, "KTASK", "RUNNING", 9, "KTASK", "KTASK", [])],
            programs: [new ProgramInfo("PICK", "TP", [new("Last modified", "16-NOV-26 13:00")]), new ProgramInfo("KTASK", "PC", [])]);

        IReadOnlyList<WaitLine> lines = WaitCheck.Evaluate(s, p => p == "PICK" ? (listing, T.AddDays(-2)) : null);

        Assert.Contains("modified on the robot", lines[0].Note, StringComparison.Ordinal);
        Assert.Contains("No OTHER.LS", lines[1].Note, StringComparison.Ordinal);
        Assert.Contains("only TP programs", lines[2].Note, StringComparison.Ordinal);
    }

    [Fact]
    public void NetworkHealthFlagsErrorCountersDownLinksAndFaultLines()
    {
        const string ethernet = "ETHERNET STATUS\nPort 1:\n  Link:          UP 100Mbps Full\n  CRC errors:    12\n  Collisions     0\n  Rx packets:    123456\nPort 2:\n  Link:          DOWN\n";
        const string eip = "06-NOV-26 08:01 Scanner connection 1 opened\n06-NOV-26 08:05 Connection 1 timed out (PLC 10.0.0.5)\n";

        NetworkHealthReport report = NetworkHealth.Parse(ethernet, eip);

        Assert.True(report.Counters.Single(c => c.Name == "CRC errors").Concern);
        Assert.False(report.Counters.Single(c => c.Name == "Collisions").Concern);
        Assert.False(report.Counters.Single(c => c.Name == "Rx packets").Concern);
        Assert.Contains(report.Counters, c => c.Section == "Port 2" && c.Name == "Link" && c.Concern);
        Assert.False(report.Log[0].Concern);
        Assert.True(report.Log[1].Concern);
        Assert.Equal(3, report.Concerns);
        Assert.Contains("CRC errors 12", report.Summary, StringComparison.Ordinal);
    }

    [Fact]
    public void TheTimelineCorrectsClocksAndNamesWhoWentFirst()
    {
        AlarmEntry[] alarms =
        [
            new("R1", new DateTime(2026, 11, 16, 8, 0, 30), "SRVO-007", "External emergency stops", "a"),
            new("R2", new DateTime(2026, 11, 16, 8, 1, 0), "SRVO-007", "External emergency stops", "b"),

            // R3's clock is 2 minutes fast: by this PC it went first.
            new("R3", new DateTime(2026, 11, 16, 8, 2, 10), "INTP-127", "Power fail detected", "c"),
            new("R1", new DateTime(2026, 11, 16, 11, 0, 0), "SRVO-062", "BZAL alarm", "d"),
            new("R1", new DateTime(2026, 11, 16, 11, 0, 0), "SRVO-062", "BZAL alarm", "d"),
            new("R1", null, "SYST-001", "undated", "e"),
        ];

        AlarmTimelineResult result = AlarmTimeline.Build(alarms, new Dictionary<string, TimeSpan> { ["R3"] = TimeSpan.FromMinutes(2) });

        Assert.Equal(1, result.Undated);
        Assert.Equal(4, result.Entries.Count);
        Assert.Equal(2, result.Incidents.Count);
        AlarmIncident cellStop = result.Incidents[1];
        Assert.Equal(1, cellStop.Number);
        Assert.Equal("R3", cellStop.FirstText);
        Assert.True(cellStop.SpansRobots);
        Assert.Equal(3, cellStop.Count);
        Assert.Equal("-2 min", result.Entries.Single(e => e.Robot == "R3").CorrectionText);
        Assert.Equal("clock not known", result.Entries.Single(e => e.Robot == "R2").CorrectionText);
    }

    [Fact]
    public void RobotsSharingTheFirstMinuteAreAllNamed()
    {
        AlarmEntry[] alarms = [new("R1", new DateTime(2026, 11, 16, 8, 0, 0), "A-001", "x", "a"), new("R2", new DateTime(2026, 11, 16, 8, 0, 0), "B-001", "y", "b")];
        Assert.Equal("R1 or R2 (same time in their logs)", Assert.Single(AlarmTimeline.Build(alarms).Incidents).FirstText);
    }

    [Fact]
    public void TransitionsAndHowLongASignalHasHeld()
    {
        TrendPoint[] points =
        [
            new(T, 0), new(T.AddSeconds(10), 1), new(T.AddSeconds(20), 0), new(T.AddSeconds(30), 1), new(T.AddSeconds(35), 0),
            new(T.AddSeconds(95), 0),
        ];

        IReadOnlyList<SignalTransition> changes = SignalTransitions.Changes("DI[34]", "Part present", true, points);
        Assert.Equal(4, changes.Count);
        Assert.Null(changes[0].HeldBefore);
        Assert.Equal(TimeSpan.FromSeconds(10), changes[1].HeldBefore);
        Assert.Equal("ON -> OFF", changes[1].Change);

        // OFF has held 10 s before; now it has held 4 min 25 s: stuck.
        SignalHold hold = SignalTransitions.Now("DI[34]", "Part present", true, points, T.AddMinutes(5))!;
        Assert.Equal("OFF", hold.State);
        Assert.True(hold.Stuck);
        Assert.False(SignalTransitions.Now("DI[34]", "", true, points, T.AddSeconds(50))!.Stuck);
        Assert.False(SignalTransitions.Now("DI[9]", "", true, [new(T, 1)], T.AddHours(5))!.Stuck);
    }

    internal static LiveSnapshot Snapshot(
        IReadOnlyList<AlarmEntry>? active = null, IReadOnlyList<IoPoint>? io = null, IReadOnlyList<SafetySignal>? stops = null,
        IReadOnlyList<TaskState>? tasks = null, IReadOnlyList<ProgramInfo>? programs = null) =>
        new(T, [], active ?? [], [], new Dictionary<string, double>())
        {
            IoPoints = io ?? [],
            SafetySignals = stops ?? [],
            Programs = new ProgramStateReport(tasks ?? [], programs ?? []),
        };

    private static IoPoint Io(string type, int index, double value, string comment) => new(type, index, value, comment, type);
}
