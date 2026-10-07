using System.Net;
using RobControl.Core.Backup;
using RobControl.Core.Controllers;
using RobControl.Core.Insight;
using RobControl.Core.Robots;
using Xunit;

namespace RobControl.Tests;

public sealed class InsightTests : IDisposable
{
    private const string Main = """
        /PROG  MAIN
        /ATTR
        COMMENT		= "Main line";
        /MN
           1:  UFRAME_NUM=1 ;
           2:  CALL WELD_A    ;
           3:  R[45:Weld count]=R[45:Weld count]+1    ;
           4:  WAIT DI[ 12:Part present]=ON    ;
           5:  !CALL NOT_A_CALL ;
           6:  PR[3,2]=PR[3,2]+10 ;
           7:  RUN WATCH ;
        /POS
        /END
        """;

    private const string WeldA = """
        /PROG  WELD_A
        /ATTR
        COMMENT		= "Weld A";
        /MN
           1:L P[1] 1500mm/sec CNT0    ;
           2:  DO[120:Gun close]=ON    ;
           3:  IF R[45]>1000,JMP LBL[9] ;
        /END
        """;

    private const string Spare = "/PROG  SPARE\n/MN\n   1:  R[1]=0 ;\n/END\n";

    private static readonly DateTimeOffset Day1 = new(2026, 9, 1, 8, 0, 0, TimeSpan.Zero);

    private readonly TempFolder _temp = new();

    public void Dispose() => _temp.Dispose();

    [Fact]
    public void A_listing_gives_its_name_comment_lines_and_references()
    {
        ProgramListing listing = ProgramListingParser.Parse(Main.Split('\n'))!;

        Assert.Equal("MAIN", listing.Name);
        Assert.Equal("Main line", listing.Comment);
        Assert.Equal(7, listing.Lines.Count);
        Assert.Equal(["WELD_A", "WATCH"], listing.Calls.ToArray());
        Assert.Contains(listing.References, r => r is { Kind: ReferenceKind.Register, Target: "R[45]", Line: 3 });
        Assert.Contains(listing.References, r => r is { Kind: ReferenceKind.Io, Target: "DI[12]", Line: 4 });
        Assert.Contains(listing.References, r => r is { Kind: ReferenceKind.PositionRegister, Target: "PR[3]", Line: 6 });
        Assert.DoesNotContain(listing.References, r => r.Target == "NOT_A_CALL");
        Assert.DoesNotContain(listing.References, r => r.Target == "F[1]"); // UFRAME_NUM is not a flag
    }

    [Fact]
    public void Something_that_is_not_a_TP_listing_is_not_parsed_as_one()
    {
        Assert.Null(ProgramListingParser.Parse(["$VERSION  Storage: SHADOW  : STRING[37] = 'V9.40P/23'"]));
    }

    [Theory]
    [InlineData("R[45]", "R[45:Weld count]=R[45:Weld count]+1", true)]
    [InlineData("R[45]", "IF R[ 45]>1000", true)]
    [InlineData("R[45]", "PR[45]=LPOS", false)]
    [InlineData("R[45]", "R[450]=0", false)]
    [InlineData("PR[3]", "PR[3,2]=PR[3,2]+10", true)]
    [InlineData("DO[120]", "SDO[120]=ON", false)]
    [InlineData("F[1]", "UFRAME[1]", false)]
    public void A_register_or_port_is_found_however_the_line_writes_it(string target, string line, bool expected)
    {
        Assert.Equal(expected, ProgramListingParser.ReferencePattern(target)!.IsMatch(line));
    }

    [Fact]
    public void Search_finds_a_register_across_robots_and_names_the_program()
    {
        List<RobotBackup> backups =
        [
            FakeBackup.Write(_temp.Path, "R1-01", Day1, ("MAIN.LS", Main), ("WELD_A.LS", WeldA)),
            FakeBackup.Write(_temp.Path, "R1-02", Day1, ("MAIN.LS", Main)),
        ];

        SearchResult result = BackupSearch.Search(backups, new SearchQuery("r[45]"));

        Assert.Equal(3, result.Hits.Count);
        Assert.Equal(2, result.RobotsSearched);
        Assert.Contains(result.Hits, h => h is { Robot: "R1-01", Program: "WELD_A", Line: 7 });
        Assert.False(result.Truncated);
    }

    [Fact]
    public void Search_scope_regex_and_limits()
    {
        RobotBackup backup = FakeBackup.Write(_temp.Path, "R1-01", Day1,
            ("MAIN.LS", Main), ("SYSVARS.VA", "$WELD_COUNT  Storage: CMOS  : INTEGER = 45\n"));

        Assert.Empty(BackupSearch.Search([backup], new SearchQuery("WELD_COUNT")).Hits);
        Assert.Single(BackupSearch.Search([backup], new SearchQuery("WELD_COUNT", SearchScope.ProgramsAndVariables)).Hits);
        Assert.Equal(2, BackupSearch.Search([backup], new SearchQuery(@"^\s+[2-3]:", IsRegex: true)).Hits.Count);
        Assert.True(BackupSearch.Search([backup], new SearchQuery("R"), maxHits: 2).Truncated);
        Assert.Throws<InsightException>(() => BackupSearch.Search([backup], new SearchQuery("([", IsRegex: true)));
        Assert.Throws<InsightException>(() => BackupSearch.Search([backup], new SearchQuery("  ")));
    }

    [Fact]
    public void Cross_reference_callers_where_used_and_programs_nothing_calls()
    {
        CrossReference xref = CrossReference.Build(
        [
            FakeBackup.Write(_temp.Path, "R1-01", Day1, ("MAIN.LS", Main), ("WELD_A.LS", WeldA), ("SPARE.LS", Spare)),
            FakeBackup.Write(_temp.Path, "R1-02", Day1, ("WELD_A.LS", WeldA)),
        ]);

        ProgramUse caller = Assert.Single(xref.Callers("weld_a"));
        Assert.Equal(("R1-01", "MAIN"), (caller.Robot, caller.Program));
        Assert.Equal(["R1-01", "R1-02"], xref.RobotsWith("WELD_A").ToArray());

        List<ProgramUse> r45 = [.. xref.WhereUsed("R[45]")];
        Assert.Equal(3, r45.Count); // MAIN and WELD_A on R1-01, WELD_A on R1-02
        Assert.Equal("3", r45.Single(u => u.Program == "MAIN").LineList);
        Assert.Equal(2, xref.WhereUsed("DO[120]").Count);

        string[] notCalled = [.. xref.NotCalled().Select(u => $"{u.Robot}/{u.Program}")];
        Assert.Equal(["R1-01/MAIN", "R1-01/SPARE", "R1-02/WELD_A"], notCalled);
    }

    [Fact]
    public void Alarm_logs_are_read_tolerantly_and_overlapping_backups_do_not_double_count()
    {
        const string Log1 = """
            ERRALL.LS     Robot Name ROBOT
              1 04-OCT-26 21:55:10  SRVO-062 BZAL alarm (Group:1 Axis:4)          ACTIVE
              2 04-OCT-26 09:12  SPOT-088 Weld schedule out of range
              3 03-OCT-26 17:40  INTP-105 Run request failed
            """;
        const string Log2 = """
              1 05-OCT-26 06:00:00  SRVO-050 Collision Detect alarm (G:1 A:3)
              2 04-OCT-26 21:55:10  SRVO-062 BZAL alarm (Group:1 Axis:4)          ACTIVE
              3 04-OCT-26 09:12  SPOT-088 Weld schedule out of range
            """;

        List<RobotBackup> backups =
        [
            FakeBackup.Write(_temp.Path, "R1-01", Day1, ("ERRALL.LS", Log1)),
            FakeBackup.Write(_temp.Path, "R1-01", Day1.AddDays(1), ("ERRALL.LS", Log2)),
            FakeBackup.Write(_temp.Path, "R1-02", Day1, ("ERRALL.LS", "  1 04-OCT-26 10:00  SPOT-088 Weld schedule out of range\n")),
        ];

        IReadOnlyList<AlarmEntry> entries = AlarmHistory.Collect(backups);

        Assert.Equal(5, entries.Count);
        AlarmEntry bzal = entries.Single(e => e.Code == "SRVO-062");
        Assert.Equal(new DateTime(2026, 10, 4, 21, 55, 10), bzal.When);
        Assert.Equal("BZAL alarm (Group:1 Axis:4)", bzal.Message);
        Assert.Equal(AlarmConcern.Battery, bzal.Concern);
        Assert.Equal(AlarmConcern.Collision, entries.Single(e => e.Code == "SRVO-050").Concern);

        AlarmCodeSummary top = AlarmHistory.ByCode(entries)[0];
        Assert.Equal(("SPOT-088", 2, 2), (top.Code, top.Count, top.RobotCount));

        RobotAlarmSummary r1 = AlarmHistory.ByRobot(entries).Single(r => r.Robot == "R1-01");
        Assert.Equal(4, r1.Count);
        Assert.Equal(2, r1.Concerns);
        Assert.NotNull(r1.MeanTimeBetween);

        Assert.Equal(2, AlarmHistory.Since(entries, new DateTime(2026, 10, 4, 12, 0, 0)).Count());
    }

    [Fact]
    public void A_changed_tool_frame_and_payload_are_reported_but_a_counter_is_not()
    {
        const string Before = """
            $MNUTOOL  Storage: CMOS  Access: RW  : ARRAY[1,10] OF POSITION
              [1,1] = 
              Group: 1   Config: N D B, 0, 0, 0
              X:   120.000   Y:     0.000   Z:   250.000
            $PLST_GRP1  Storage: CMOS  Access: RW  : PLST_GRP_T
              Field: $PLST_GRP1.$PAYLOAD Access: RW: REAL = 85.000000
            $WELD_COUNT  Storage: CMOS  Access: RW  : INTEGER = 100
            """;
        string after = Before.Replace("X:   120.000", "X:   121.500", StringComparison.Ordinal)
            .Replace("85.000000", "110.000000", StringComparison.Ordinal)
            .Replace("INTEGER = 100", "INTEGER = 250", StringComparison.Ordinal);

        RobotBackup older = FakeBackup.Write(_temp.Path, "R1-01", Day1, ("SYSFRAME.VA", Before));
        RobotBackup newer = FakeBackup.Write(_temp.Path, "R1-01", Day1.AddDays(1), ("SYSFRAME.VA", after));

        IReadOnlyList<SettingChange> changes = SettingsWatch.Compare("R1-01", older.Set, newer.Set);

        Assert.Equal(["Payload", "Tool frame"], changes.Select(c => c.Category).ToArray());
        SettingChange tool = changes.Single(c => c.Category == "Tool frame");
        Assert.Equal("$MNUTOOL", tool.Variable);
        Assert.Contains("120.000", tool.OldText, StringComparison.Ordinal);
        Assert.Contains("121.500", tool.NewText, StringComparison.Ordinal);
    }

    [Fact]
    public void Setting_history_compares_consecutive_complete_backups_only()
    {
        RobotBackup a = FakeBackup.Write(_temp.Path, "R1-01", Day1, ("SYSVARS.VA", "$VERSION  : STRING[37] = 'V9.30P/40'\n"));
        RobotBackup broken = FakeBackup.Write(_temp.Path, "R1-01", Day1.AddDays(1), BackupOutcome.Partial, ("SYSVARS.VA", "$VERSION  : STRING[37] = 'X'\n"));
        RobotBackup b = FakeBackup.Write(_temp.Path, "R1-01", Day1.AddDays(2), ("SYSVARS.VA", "$VERSION  : STRING[37] = 'V9.40P/23'\n"));

        SettingChange change = Assert.Single(SettingsWatch.History("R1-01", [b.Set, broken.Set, a.Set]));
        Assert.Equal("Software version", change.Category);
        Assert.Equal(a.Stamp, change.OlderStamp);
        Assert.Empty(SettingsWatch.History("R1-01", [a.Set, b.Set], since: Day1.AddDays(3)));
    }

    [Fact]
    public void Inventory_merges_probe_and_backup_and_exports_safe_csv()
    {
        string root = Path.Combine(_temp.Path, "archive");
        var robot = new Robot("=HYPERLINK(\"x\")", IPAddress.Parse("10.0.0.5")) { Line = "Line 1" };
        var robot2 = new Robot("R1-02", IPAddress.Parse("10.0.0.6"));
        FakeBackup.Write(root, robot.Name, Day1, ("MAIN.LS", Main));
        FakeBackup.Write(root, robot.Name, Day1.AddDays(1), BackupOutcome.Failed);
        var probed = new ControllerIdentity { SoftwareVersion = "V9.40P/23", RobotModel = "R-2000iC/210F" };

        IReadOnlyList<InventoryRow> rows = FleetInventory.Build([(robot, probed), (robot2, null)], new BackupArchive(root));

        InventoryRow first = rows[0];
        Assert.Equal("V9.40P/23", first.Software);
        Assert.Equal(Day1, first.LastCompleteUtc);
        Assert.Equal("Failed", first.LastOutcome);
        Assert.Equal(2, first.BackupCount);
        Assert.Equal("Never backed up", rows[1].LastOutcome);
        Assert.Null(rows[1].BackupAgeDays(Day1));

        string csv = FleetInventory.ToCsv(rows);
        Assert.Contains("\"'=HYPERLINK(\"\"x\"\")\"", csv, StringComparison.Ordinal);
    }

    [Fact]
    public void The_site_report_encodes_everything_from_controllers()
    {
        var data = new SiteReportData
        {
            SiteName = "Plant <3>",
            GeneratedUtc = Day1,
            Since = Day1.AddDays(-30),
            Tool = "RobControl test",
            ArchiveRoot = @"D:\Backups",
            SiteNotes = "Call <b>Dave</b>",
            Inventory = [new InventoryRow("R1-01<script>", "10.0.0.1", null, "R-30iB Plus", null, null, null, null, Day1.AddDays(-10), Day1, "Complete", 3)],
            ConcernAlarms = [new AlarmEntry("R1-01", new DateTime(2026, 8, 30), "SRVO-065", "BLAL alarm <&>", "raw")],
        };

        string html = SiteReport.Render(data);

        Assert.DoesNotContain("<script>", html, StringComparison.Ordinal);
        Assert.Contains("R1-01&lt;script&gt;", html, StringComparison.Ordinal);
        Assert.Contains("Plant &lt;3&gt;", html, StringComparison.Ordinal);
        Assert.Contains("BLAL alarm &lt;&amp;&gt;", html, StringComparison.Ordinal);
        Assert.Contains("class=\"bad\">10 d", html, StringComparison.Ordinal);
    }
}
