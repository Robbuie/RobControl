using RobControl.Core.Controllers;
using RobControl.Core.Insight;
using RobControl.Core.Transports.Http;
using RobControl.Core.Trending;
using Xunit;

namespace RobControl.Tests;

/// <summary>
/// Every /MD/ file the web server sends comes inside an HTML page (R-30iB V8.30, 6 Nov 2026). The
/// I/O, program, position and stop-signal parsers were fixed for that in 0.7.1; these are the rest of
/// the readers that get a page over HTTP - registers for trends and the signal browser, the summary
/// for the probe, the alarm log - fed the synthetic files in the real controller's wrapper.
/// </summary>
public class WrappedPageTests
{
    private const string Synthetic = "synthetic-r30ibplus-v940-spottool";

    /// <summary>The wrapper from the real IOSTATE.DG excerpt, around another file.</summary>
    private static string Wrap(string file)
    {
        string real = Fixtures.ReadText("excerpt-r30ib-v830-repairtool", "MD/IOSTATE.DG");
        int open = real.IndexOf("<pre>", StringComparison.OrdinalIgnoreCase);
        int close = real.IndexOf("</pre>", StringComparison.OrdinalIgnoreCase);
        Assert.True(open > 0 && close > open, "the excerpt should still have its <pre>");

        // As a web server would: < > & and ' escaped inside the <pre>.
        string escaped = System.Net.WebUtility.HtmlEncode(file);
        return real[..(open + 5)] + escaped + real[close..];
    }

    [Fact]
    public void RegistersAreReadFromTheWrappedListing()
    {
        string plain = Fixtures.ReadText(Synthetic, "MD/NUMREG.VA");
        IReadOnlyDictionary<int, double> expected = RegisterFileParser.Parse(plain);
        Assert.Equal(3, expected.Count);

        IReadOnlyDictionary<int, double> wrapped = RegisterFileParser.Parse(Wrap(plain));
        Assert.Equal(expected, wrapped);
    }

    [Fact]
    public void TheSignalBrowserGetsRegisterCommentsWithoutEntities()
    {
        IReadOnlyList<SignalEntry> entries = SignalCatalog.Registers(Wrap(Fixtures.ReadText(Synthetic, "MD/NUMREG.VA")));
        Assert.Contains(entries, e => e.Address.Text == "R[2]" && e.Comment == "Weld count");
        Assert.DoesNotContain(entries, e => e.Comment is { } c && c.Contains('&'));
    }

    [Fact]
    public void TheProbeReadsIdentityFromAWrappedSummary()
    {
        string plain = Fixtures.ReadText(Synthetic, "MD/SUMMARY.DG");
        ControllerIdentity identity = ControllerIdentityParser.Parse(DiagnosticPage.Text(Wrap(plain)));
        Assert.Equal(ControllerIdentityParser.Parse(plain).SoftwareVersion, identity.SoftwareVersion);
        Assert.Equal("F123456", identity.FNumber);
    }

    [Fact]
    public void TheAlarmLogReadsTheSameWrappedOrNot()
    {
        string plain = Fixtures.ReadText(Synthetic, "MD/ERRALL.LS");
        IReadOnlyList<AlarmEntry> a = AlarmLogParser.Parse("R1", plain.Split('\n'));
        IReadOnlyList<AlarmEntry> b = AlarmLogParser.Parse("R1", DiagnosticPage.Text(Wrap(plain)).Split('\n'));
        Assert.NotEmpty(a);
        Assert.Equal(a.Select(e => e.Code), b.Select(e => e.Code));
    }
}
