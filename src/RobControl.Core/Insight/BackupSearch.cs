using System.Text.RegularExpressions;
using RobControl.Core.Backup;
using RobControl.Core.History;

namespace RobControl.Core.Insight;

/// <summary>
/// Text search across backups: "where is R[45] used", "who sets DO[120]", "which robots still have
/// the old weld schedule". Reads the archive only.
/// </summary>
public static class BackupSearch
{
    public const int DefaultMaxHits = 5000;

    public static SearchResult Search(IEnumerable<RobotBackup> backups, SearchQuery query, int maxHits = DefaultMaxHits, CancellationToken cancellation = default)
    {
        ArgumentNullException.ThrowIfNull(backups);
        ArgumentNullException.ThrowIfNull(query);
        Regex pattern = Pattern(query);
        var hits = new List<SearchHit>();
        int files = 0;
        var robots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (RobotBackup backup in backups)
        {
            robots.Add(backup.Robot);
            foreach (BackupFileRecord file in backup.Set.Manifest.Files)
            {
                cancellation.ThrowIfCancellationRequested();
                string path;
                try
                {
                    path = backup.Set.PathOf(file);
                }
                catch (BackupArchiveException)
                {
                    continue;
                }

                if (!InScope(file.Name, path, query.Scope) || !File.Exists(path))
                {
                    continue;
                }

                IReadOnlyList<string> lines;
                try
                {
                    lines = BackupComparer.ReadLines(path);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    continue;
                }

                files++;
                string? program = Path.GetExtension(file.Name).Equals(".LS", StringComparison.OrdinalIgnoreCase)
                    ? ProgramListingParser.Parse(lines)?.Name
                    : null;

                for (int i = 0; i < lines.Count; i++)
                {
                    bool match;
                    try
                    {
                        match = pattern.IsMatch(lines[i]);
                    }
                    catch (RegexMatchTimeoutException)
                    {
                        match = false;
                    }

                    if (!match)
                    {
                        continue;
                    }

                    hits.Add(new SearchHit(backup.Robot, backup.Stamp, file.RelativePath, program, i + 1, lines[i].Trim(), path));
                    if (hits.Count >= maxHits)
                    {
                        return new SearchResult(hits, files, robots.Count, Truncated: true);
                    }
                }
            }
        }

        return new SearchResult(hits, files, robots.Count, Truncated: false);
    }

    /// <summary>
    /// The pattern for a query. A user's regular expression gets a time limit, so a pathological one
    /// costs a quarter of a second per line rather than the afternoon.
    /// </summary>
    public static Regex Pattern(SearchQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);
        string text = query.Text ?? string.Empty;
        if (string.IsNullOrWhiteSpace(text))
        {
            throw new InsightException("Type something to search for - a program name, R[45], DO[120], or any text.");
        }

        if (!query.IsRegex && ProgramListingParser.ReferencePattern(text) is { } reference)
        {
            return reference;
        }

        RegexOptions options = RegexOptions.CultureInvariant | (query.CaseSensitive ? RegexOptions.None : RegexOptions.IgnoreCase);
        try
        {
            return new Regex(query.IsRegex ? text : Regex.Escape(text.Trim()), options, TimeSpan.FromMilliseconds(250));
        }
        catch (ArgumentException ex)
        {
            throw new InsightException($"'{text}' is not a valid regular expression: {ex.Message}", ex)
            {
                Remediation = "Untick Regex to search for the text as typed.",
            };
        }
    }

    private static bool InScope(string name, string path, SearchScope scope)
    {
        string extension = Path.GetExtension(name);
        return scope switch
        {
            SearchScope.Programs => extension.Equals(".LS", StringComparison.OrdinalIgnoreCase),
            SearchScope.ProgramsAndVariables => extension.Equals(".LS", StringComparison.OrdinalIgnoreCase)
                || extension.Equals(".VA", StringComparison.OrdinalIgnoreCase),
            _ => extension.Equals(".LS", StringComparison.OrdinalIgnoreCase)
                || extension.Equals(".VA", StringComparison.OrdinalIgnoreCase)
                || extension.Equals(".DG", StringComparison.OrdinalIgnoreCase)
                || extension.Equals(".TXT", StringComparison.OrdinalIgnoreCase)
                || TextSniffer.LooksLikeText(path),
        };
    }
}
