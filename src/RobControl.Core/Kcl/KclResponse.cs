using System.Net;
using System.Text.RegularExpressions;

namespace RobControl.Core.Kcl;

/// <summary>
/// Pulls the command output out of the page a controller wraps it in.
///
/// <para>Field reports are that the output sits inside an <c>&lt;XMP&gt;</c> element (older pages)
/// or a <c>&lt;PRE&gt;</c>. Both are tried, then a plain tag strip as the last resort, so a page
/// layout we have not seen still yields its text rather than nothing. <b>Unconfirmed against our
/// own controllers</b> - see CLAUDE.md, Phase 0.</para>
/// </summary>
public static partial class KclResponse
{
    public static string ExtractText(string html)
    {
        ArgumentNullException.ThrowIfNull(html);

        Match xmp = Xmp().Match(html);
        if (xmp.Success)
        {
            // XMP content is literal text: nothing inside it is markup.
            return Tidy(xmp.Groups[1].Value);
        }

        Match pre = Pre().Match(html);
        if (pre.Success)
        {
            return Tidy(WebUtility.HtmlDecode(Tags().Replace(pre.Groups[1].Value, string.Empty)));
        }

        if (!html.Contains('<', StringComparison.Ordinal))
        {
            return Tidy(html);
        }

        string body = Body().Match(html) is { Success: true } b ? b.Groups[1].Value : html;
        body = Scripts().Replace(body, string.Empty);
        body = Breaks().Replace(body, "\n");
        return Tidy(WebUtility.HtmlDecode(Tags().Replace(body, string.Empty)));
    }

    /// <summary>The first line carrying a FANUC alarm code, e.g. <c>VARS-014</c>, or null.</summary>
    public static string? FindError(string output)
    {
        ArgumentNullException.ThrowIfNull(output);
        Match match = AlarmCode().Match(output);
        return match.Success ? match.Value.Trim() : null;
    }

    private static string Tidy(string text)
    {
        string[] lines = text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n');
        return string.Join('\n', lines.Select(l => l.TrimEnd())).Trim('\n');
    }

    [GeneratedRegex(@"<xmp[^>]*>(.*?)</xmp>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex Xmp();

    [GeneratedRegex(@"<pre[^>]*>(.*?)</pre>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex Pre();

    [GeneratedRegex(@"<body[^>]*>(.*?)</body>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex Body();

    [GeneratedRegex(@"<(script|style)[^>]*>.*?</\1>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex Scripts();

    [GeneratedRegex(@"<br\s*/?>|</p>|</tr>|</div>", RegexOptions.IgnoreCase)]
    private static partial Regex Breaks();

    [GeneratedRegex(@"<[^>]+>")]
    private static partial Regex Tags();

    [GeneratedRegex(@"(?m)^.*\b[A-Z]{2,5}-\d{3}\b.*$")]
    private static partial Regex AlarmCode();
}
