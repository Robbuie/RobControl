using RobControl.Core.Events;
using RobControl.Core.Insight;
using RobControl.Core.Persistence;
using Xunit;

namespace RobControl.Tests;

public class CsvTests
{
    [Theory]
    [InlineData("R1-01", "R1-01")]
    [InlineData("a,b", "\"a,b\"")]
    [InlineData("say \"hi\"", "\"say \"\"hi\"\"\"")]
    [InlineData("=HYPERLINK(\"x\")", "\"'=HYPERLINK(\"\"x\"\")\"")]
    [InlineData("+1", "\"'+1\"")]
    [InlineData("-cmd", "\"'-cmd\"")]
    [InlineData("@SUM", "\"'@SUM\"")]
    [InlineData("line1\nline2", "\"line1\nline2\"")]
    [InlineData(null, "")]
    public void FieldsAreQuotedAndCanNeverBeFormulas(string? value, string expected) => Assert.Equal(expected, Csv.Field(value));

    [Fact]
    public void AlarmsExportOldestFirstWithTheJobColumn()
    {
        AlarmEntry[] entries =
        [
            new("R2", new DateTime(2026, 9, 2, 10, 0, 0), "SRVO-065", "BLAL alarm (Group:1 Axis:1)", "x"),
            new("R1", new DateTime(2026, 9, 1, 9, 30, 5), "INTP-105", "Run request failed", "y"),
        ];

        string[] lines = AlarmHistory.ToCsv(entries).TrimEnd().Split(Environment.NewLine);

        Assert.Equal("When,Robot,Alarm,Facility,Message,Needs a job", lines[0]);
        Assert.StartsWith("2026-09-01 09:30:05,R1,INTP-105,INTP,", lines[1], StringComparison.Ordinal);
        Assert.StartsWith("2026-09-02 10:00:00,R2,SRVO-065,SRVO,", lines[2], StringComparison.Ordinal);
        Assert.NotEqual(string.Empty, lines[2].Split(',')[^1]);
        Assert.Equal(string.Empty, lines[1].Split(',')[^1]);
    }

    [Fact]
    public void SearchHitsAndProgramsExport()
    {
        string hits = BackupSearch.ToCsv([new SearchHit("R1", "2026-09-01_080000", "MD/MAIN.LS", "MAIN", 7, "  7:  R[45]=R[45]+1 ;", "/x")]);
        Assert.Contains("R1,2026-09-01_080000,MD/MAIN.LS,MAIN,7,", hits, StringComparison.Ordinal);

        string uses = BackupSearch.ToCsv([new ProgramUse("R1", "WELD_A", "Weld, side A", [3, 9])]);
        Assert.Contains("R1,WELD_A,\"Weld, side A\",\"3, 9\"", uses, StringComparison.Ordinal);
    }

    [Fact]
    public void TheEventLogExportsOldestFirstWithUtc()
    {
        var newer = new RobotEvent(new DateTimeOffset(2026, 9, 2, 0, 0, 0, TimeSpan.Zero), EventSeverity.Warn, EventCategory.Backup, "R1 (10.0.0.1)", "Backup partial", "SYSMAST.SV: 550");
        var older = new RobotEvent(new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero), EventSeverity.Info, EventCategory.Probe, null, "Probe", null);

        string[] lines = EventLogCsv.ToCsv([newer, older]).TrimEnd().Split(Environment.NewLine);

        Assert.Equal(3, lines.Length);
        Assert.Contains("2026-09-01T00:00:00.0000000Z", lines[1], StringComparison.Ordinal);
        Assert.Contains(",Warn,Backup,R1 (10.0.0.1),Backup partial,SYSMAST.SV: 550", lines[2], StringComparison.Ordinal);
    }
}
