using System.Text.RegularExpressions;
using RobControl.Core.Backup;

namespace RobControl.Core.Capture;

/// <summary>
/// Which diagnostic files a controller's own pages link to. The home page links the pages it has, and
/// <c>/MD/INDEX_ER.HTM</c> lists every diagnostic file on MD: (R-30iB V8.30, 6 Nov 2026).
///
/// <para><b>Only plain file names under /MD/ are kept</b>, and only with an extension that names a
/// listing or a diagnostic file. A link to <c>/KAREL/</c>, <c>/KCL/</c>, a form, or anything with a
/// query string is ignored - a capture follows the controller's index, it does not crawl it, and it
/// can never be steered into running something by what a page links to.</para>
/// </summary>
public static partial class CaptureIndex
{
    /// <summary>Extensions worth fetching: diagnostics, listings, variable dumps, plain text.</summary>
    public static IReadOnlySet<string> Extensions { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        ".DG", ".LS", ".VA", ".TXT", ".DT",
    };

    /// <summary>Index pages on /MD/ that may be followed one level - e.g. INDEX_ER.HTM.</summary>
    public static bool IsIndexPage(string name) =>
        name.StartsWith("INDEX", StringComparison.OrdinalIgnoreCase)
        && (name.EndsWith(".HTM", StringComparison.OrdinalIgnoreCase) || name.EndsWith(".HTML", StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// The MD: file names <paramref name="html"/> links to, in page order, without duplicates.
    /// <paramref name="indexes"/> gets the index pages it links to.
    /// </summary>
    public static IReadOnlyList<string> Files(string? html, out IReadOnlyList<string> indexes)
    {
        var files = new List<string>();
        var pages = new List<string>();
        if (string.IsNullOrEmpty(html))
        {
            indexes = pages;
            return files;
        }

        foreach (Match match in Href().Matches(html))
        {
            string link = match.Groups["v"].Value.Trim();
            if (link.Contains('?') || link.Contains('#') || link.Contains(':'))
            {
                continue;
            }

            // "/MD/ERRALL.LS", "/md/errall.ls", "ERRALL.LS" (relative to /MD/) and "../MD/X.DG" are
            // all the same file; anything under another folder is not ours to follow.
            string path = link.Replace('\\', '/');
            int md = path.LastIndexOf("/MD/", StringComparison.OrdinalIgnoreCase);
            string name;
            if (md >= 0)
            {
                name = path[(md + 4)..];
            }
            else if (!path.Contains('/'))
            {
                name = path;
            }
            else
            {
                continue;
            }

            if (name.Length == 0 || name.Contains('/') || !ArchiveNames.IsSafeFileName(name, out _))
            {
                continue;
            }

            string upper = name.ToUpperInvariant();
            if (IsIndexPage(upper))
            {
                if (!pages.Contains(upper))
                {
                    pages.Add(upper);
                }
            }
            else if (Extensions.Contains(Path.GetExtension(upper)) && !files.Contains(upper))
            {
                files.Add(upper);
            }
        }

        indexes = pages;
        return files;
    }

    [GeneratedRegex("""href\s*=\s*["']?(?<v>[^"'\s>]+)""", RegexOptions.IgnoreCase)]
    private static partial Regex Href();
}
