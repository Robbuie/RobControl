using RobControl.Core.Kcl;
using Xunit;

namespace RobControl.Tests;

public class KclResponseTests
{
    [Fact]
    public void XmpContentIsTakenLiterally()
    {
        string html = "<HTML><BODY><H1>KCL</H1><XMP>$VERSION = 'V9.40P/23' <not a tag></XMP></BODY></HTML>";
        Assert.Equal("$VERSION = 'V9.40P/23' <not a tag>", KclResponse.ExtractText(html));
    }

    [Fact]
    public void PreContentIsDecoded()
    {
        string html = "<html><body><pre>A &amp; B<br>C</pre></body></html>";
        Assert.Equal("A & BC", KclResponse.ExtractText(html));
    }

    [Fact]
    public void UnknownLayoutStillYieldsItsText()
    {
        string html = "<html><head><script>x()</script></head><body><p>Line one</p><div>Line two</div></body></html>";
        string text = KclResponse.ExtractText(html);
        Assert.Contains("Line one", text, StringComparison.Ordinal);
        Assert.Contains("Line two", text, StringComparison.Ordinal);
        Assert.DoesNotContain("x()", text, StringComparison.Ordinal);
    }

    [Fact]
    public void ControllerErrorsAreFoundInsideASuccessfulPage()
    {
        Assert.Equal("VARS-014 Create type failed", KclResponse.FindError("something\nVARS-014 Create type failed\n"));
        Assert.Null(KclResponse.FindError("$VERSION = 'V9.40P/23'"));
    }
}
