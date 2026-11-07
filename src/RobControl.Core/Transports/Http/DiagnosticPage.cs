using System.Net;
using System.Text.RegularExpressions;

namespace RobControl.Core.Transports.Http;

/// <summary>
/// The file inside a diagnostic page. A controller's web server does not send <c>/MD/IOSTATE.DG</c>
/// as plain text: it wraps it in an HTML page (title, logo, a script block, a header table) and puts
/// the file itself in one <c>&lt;pre&gt;</c>. Seen on an R-30iB, V8.30 RepairTool+, November 2026.
///
/// <para>The same file fetched over FTP is the plain text, so text with no <c>&lt;pre&gt;</c> is
/// returned as it is - a backup copy, the simulator, a controller that does not wrap.</para>
/// </summary>
public static partial class DiagnosticPage
{
    public static string Text(string? body)
    {
        if (string.IsNullOrEmpty(body))
        {
            return string.Empty;
        }

        MatchCollection blocks = Pre().Matches(body);
        if (blocks.Count == 0)
        {
            return body.Replace("\r\n", "\n", StringComparison.Ordinal);
        }

        // Everything inside the <pre> blocks, tags dropped (a page could bold a word) and entities
        // decoded - an I/O comment can hold "&" or "<".
        string inner = string.Join("\n", blocks.Select(b => b.Groups["t"].Value));
        return WebUtility.HtmlDecode(Tag().Replace(inner, string.Empty)).Replace("\r\n", "\n", StringComparison.Ordinal);
    }

    [GeneratedRegex(@"<pre[^>]*>(?<t>.*?)</pre\s*>", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant)]
    private static partial Regex Pre();

    [GeneratedRegex(@"<[^>]{0,200}>", RegexOptions.CultureInvariant)]
    private static partial Regex Tag();
}
