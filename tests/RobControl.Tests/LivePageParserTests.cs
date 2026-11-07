using RobControl.Core.Live;
using RobControl.Core.Trending;
using RobControl.Core.Transports.Http;
using Xunit;

namespace RobControl.Tests;

/// <summary>
/// The Live tab's parsers against pages as a real controller's web server sends them - HTML around a
/// <c>&lt;pre&gt;</c> (tests/Fixtures/excerpt-r30ib-v830-repairtool).
/// </summary>
public class LivePageParserTests
{
    private const string Real = "excerpt-r30ib-v830-repairtool";

    [Fact]
    public void TheFileComesOutOfTheHtmlWrapper()
    {
        string text = DiagnosticPage.Text(Fixtures.ReadText(Real, "MD/SFTYSIG.DG"));
        Assert.StartsWith("F Number:", text, StringComparison.Ordinal);
        Assert.DoesNotContain("<", text, StringComparison.Ordinal);
        Assert.DoesNotContain("openSetupWin", text, StringComparison.Ordinal);
    }

    [Fact]
    public void PlainTextIsLeftAlone() =>
        Assert.Equal("DI[  1] ON   Part present\nx", DiagnosticPage.Text("DI[  1] ON   Part present\r\nx"));

    [Fact]
    public void RealIoPageListsEveryTypeUnderThePendantsNames()
    {
        IReadOnlyList<IoPoint> points = IoStateParser.Points(Fixtures.ReadText(Real, "MD/IOSTATE.DG"));

        IoPoint imstp = points.First(p => p.Key == "DI[1]");
        Assert.Equal("*IMSTP", imstp.Comment);
        Assert.Equal("DIN", imstp.Listed);
        Assert.False(imstp.IsOn);
        Assert.True(points.First(p => p.Key == "DI[2]").IsOn);
        Assert.Equal("Auto Mode Req", points.First(p => p.Key == "DI[131]").Comment);
        Assert.Equal("Rep%Cmp B0", points.First(p => p.Key == "DI[865]").Comment);
        Assert.Equal(string.Empty, points.First(p => p.Key == "DI[33]").Comment);
        Assert.Equal("System Ready", points.First(p => p.Key == "DO[2]").Comment);

        IoPoint changer = points.First(p => p.Key == "GI[5]");
        Assert.Equal(1, changer.Value);
        Assert.Equal("1", changer.State);
        Assert.Equal("Group", changer.Category);
        Assert.Equal("Input", changer.Direction);

        Assert.Equal("ON", points.First(p => p.Key == "SO[3]").State);
        Assert.Equal("SOP", points.First(p => p.Key == "SI[2]").Category);
        Assert.Contains(points, p => p.Key == "UI[1]");
        Assert.Contains(points, p => p.Key == "RO[1]");

        // Flags are printed two to a line; each keeps its own comment.
        Assert.Equal("Continue", points.First(p => p.Key == "F[1]").Comment);
        Assert.Equal(string.Empty, points.First(p => p.Key == "F[513]").Comment);
        Assert.True(points.First(p => p.Key == "F[514]").IsOn);
        Assert.Equal("Internal", points.First(p => p.Key == "F[514]").Direction);

        Assert.DoesNotContain(points, p => p.Type is "DIN" or "DOUT" or "FLG");
    }

    [Fact]
    public void TheRecorderGetsTheRealPageUnderTheSameKeysItStores()
    {
        IReadOnlyDictionary<string, double> io = IoStateParser.Parse(Fixtures.ReadText(Real, "MD/IOSTATE.DG"));
        Assert.Equal(1, io["DI[2]"]);
        Assert.Equal(0, io["DO[1]"]);
        Assert.Equal(1, io["F[514]"]);
        Assert.Equal(1, io["GI[5]"]);
    }

    [Theory]
    [InlineData("CSI[  1]  ON  Light curtain OK", "CSI[1]", "Safe I/O (DCS / CIP Safety)", "Input")]
    [InlineData("CSO[ 12] OFF  Zone clear", "CSO[12]", "Safe I/O (DCS / CIP Safety)", "Output")]
    [InlineData("SPI[3] ON", "SPI[3]", "Safe I/O (DCS / CIP Safety)", "Input")]
    [InlineData("SIR[ 4] OFF", "SIR[4]", "Safe I/O (DCS / CIP Safety)", "Internal")]
    [InlineData("WSI[ 2] OFF  Arc detect", "WSI[2]", "Weld", "Input")]
    [InlineData("AIN[ 1]  4.25  Pressure", "AI[1]", "Analog", "Input")]
    public void SafeAndOtherIoIsKeptRatherThanDropped(string line, string key, string category, string direction)
    {
        IoPoint point = Assert.Single(IoStateParser.Points(line));
        Assert.Equal(key, point.Key);
        Assert.Equal(category, point.Category);
        Assert.Equal(direction, point.Direction);
    }

    [Fact]
    public void SafeIoCanBeTrendedLikeAnyOtherPoint()
    {
        Assert.True(SignalAddress.TryParse("CSI[1]", out SignalAddress? address, out _));
        Assert.Equal(SignalKind.Io, address!.Kind);
        Assert.Equal(1, IoStateParser.Parse("CSI[  1]  ON  Light curtain OK")["CSI[1]"]);
    }

    [Fact]
    public void RegistersOnAnIoPageAreNotIo() =>
        Assert.Empty(IoStateParser.Points("R[  1] 12\nPR[2] 0\nSR[3] 1"));

    [Fact]
    public void TheSignalBrowserListsTheRealPage()
    {
        IReadOnlyList<SignalEntry> entries = SignalCatalog.Io(Fixtures.ReadText(Real, "MD/IOSTATE.DG"));
        SignalEntry imstp = entries.First(e => e.Address.Text == "DI[1]");
        Assert.Equal("*IMSTP", imstp.Comment);
        Assert.Equal("OFF", imstp.Value);
        Assert.Null(entries.First(e => e.Address.Text == "DI[33]").Comment);
    }

    [Fact]
    public void StopSignals()
    {
        IReadOnlyList<SafetySignal> signals = SafetySignalParser.Parse(Fixtures.ReadText(Real, "MD/SFTYSIG.DG"));
        Assert.Equal(12, signals.Count);
        Assert.Equal(new SafetySignal("External ESTOP", false), signals[0]);
        Assert.True(signals.Single(s => s.Name == "Hand Broken").IsOn);
    }

    [Fact]
    public void TasksWithWhereTheyAreAndTheirCallStack()
    {
        ProgramStateReport report = ProgramStateParser.Parse(Fixtures.ReadText(Real, "MD/PRGSTATE.DG"));

        Assert.Equal(4, report.Tasks.Count);
        TaskState shell = report.Tasks[0];
        Assert.Equal((1, "DGSHELL", "RUNNING", 776), (shell.Number, shell.Name, shell.Status, shell.Line!.Value));
        Assert.True(shell.IsRunning);
        Assert.Equal("DGSHELL, line 776", shell.Where);

        TaskState aborted = report.Tasks[1];
        Assert.Equal("ABORTED", aborted.Status);
        Assert.Null(aborted.Line);
        Assert.Equal("DGMONFRC, line 87", aborted.Where);

        TaskState deep = report.Tasks.Single(t => t.Name == "SWAXTSK1");
        Assert.Equal("PROCESSAMR of SWAXTCMN, line 874", deep.Where);
        Assert.Equal(2, deep.Stack.Count);
        Assert.Equal("SWAXTSK1:172 > PROCESSAMR:874", deep.CallStack);

        Assert.Equal("TP", report.Tasks.Single(t => t.Name == "STYLE110").Stack[0].Type);
    }

    [Fact]
    public void ProgramsWithTheirDetails()
    {
        ProgramStateReport report = ProgramStateParser.Parse(Fixtures.ReadText(Real, "MD/PRGSTATE.DG"));

        Assert.Equal(new[] { "-BCKED2-", "BICSETUP", "CHANGE", "CLR_TRAN", "DG3MERROR" }, report.Programs.Select(p => p.Name));
        ProgramInfo change = report.Programs[2];
        Assert.Equal("TP", change.Type);
        Assert.Equal("Paper Attch", change.Comment);
        Assert.Equal(57, change.Lines);
        Assert.Equal(1805, change.SizeBytes);
        Assert.Equal(new DateTime(2026, 11, 6, 14, 51, 0), change.ModifiedTime);
        Assert.False(change.WriteProtected);
        Assert.False(change.IsTask);

        Assert.Equal("MACRO", report.Programs[3].Type);
        Assert.True(report.Programs[3].WriteProtected);
        Assert.Null(report.Programs[1].ModifiedTime);
        Assert.Equal(string.Empty, report.Programs[1].Comment);

        ProgramInfo karel = report.Programs[4];
        Assert.True(karel.IsTask);
        Assert.Equal(@"TD:\DG3MERROR.PC", karel.SourceFile);
        Assert.Equal("8454", karel.Field("Checksum"));
    }

    [Fact]
    public void JointUserFrameAndWorldPositions()
    {
        IReadOnlyList<PositionFrame> frames = PositionParser.Parse(Fixtures.ReadText(Real, "MD/CURPOS.DG"));

        Assert.Equal(new[] { PositionParser.Joint, PositionParser.UserFrame, PositionParser.World }, frames.Select(f => f.Kind));
        Assert.All(frames, f => Assert.Equal(1, f.Group));

        PositionFrame joints = frames[0];
        Assert.Equal(new[] { "J1", "J2", "J3", "J4", "J5", "J6", "E1" }, joints.Axes.Select(a => a.Axis));
        Assert.Equal(-159.64, joints["J6"]);
        Assert.Equal(-3500.02, joints["E1"]);

        PositionFrame user = frames[1];
        Assert.Equal((0, 1), (user.UserFrame!.Value, user.Tool!.Value));
        Assert.Equal("N U T, 0, 0, 0", user.Config);
        Assert.Equal(-2637.74, user["Y"]);
        Assert.Equal(0, user["P"]);

        PositionFrame world = frames[2];
        Assert.Null(world.UserFrame);
        Assert.Equal(1, world.Tool);
        Assert.Equal(90, world["R"]);
    }

    [Fact]
    public void NothingRecognisedIsEmptyNotAnError()
    {
        Assert.True(ProgramStateParser.Parse("<html><pre>nothing here</pre></html>").IsEmpty);
        Assert.Empty(PositionParser.Parse("garbage"));
        Assert.Empty(SafetySignalParser.Parse(null));
        Assert.Empty(IoStateParser.Points(string.Empty));
    }
}
