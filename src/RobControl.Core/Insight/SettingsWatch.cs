using System.Text.RegularExpressions;
using RobControl.Core.Backup;
using RobControl.Core.History;

namespace RobControl.Core.Insight;

/// <summary>
/// Changes to the settings that quietly change what a robot does: tool and user frames (a TCP
/// touched up and never written down), payload (reducer wear), mastering (a remaster), reference
/// positions, joint limits, DCS, and the software version itself.
///
/// <para>It works from the variable listings (<c>.VA</c>) in consecutive complete backups: a line
/// diff of each listing that changed, with every changed line attributed to the variable whose
/// header it sits under. Variable names are FANUC's; which listing each lives in, and how it is laid
/// out, is for Phase 0 to confirm - matching is by name wherever it appears.</para>
/// </summary>
public static partial class SettingsWatch
{
    public static IReadOnlyList<SettingRule> Rules { get; } =
    [
        new("Tool frame", Name(@"^\$(MNUTOOL|UTOOL)\b")),
        new("User frame", Name(@"^\$(MNUFRAME|UFRAME)\b")),
        new("Mastering", Name(@"^\$DMR_GRP\b")),
        new("Payload", Name(@"^\$(PLST_GRP|PAYLOAD)")),
        new("Reference position", Name(@"^\$REFPOS")),
        new("Joint limits", Name(@"\.\$(UPPERLIMS|LOWERLIMS)\b")),
        new("DCS / safety", Name(@"^\$DCS")),
        new("Software version", Name(@"^\$VERSION\b")),
    ];

    /// <summary>Watched changes between two backups of one robot, oldest to newest.</summary>
    public static IReadOnlyList<SettingChange> Compare(string robot, BackupSet older, BackupSet newer)
    {
        ArgumentNullException.ThrowIfNull(older);
        ArgumentNullException.ThrowIfNull(newer);
        var changes = new List<SettingChange>();

        foreach (FileComparison file in BackupComparer.Compare(older, newer))
        {
            if (file.Change != FileChange.Changed || file.Old is null || file.New is null
                || !Path.GetExtension(file.RelativePath).Equals(".VA", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            IReadOnlyList<string> a;
            IReadOnlyList<string> b;
            try
            {
                a = BackupComparer.ReadLines(older.PathOf(file.Old));
                b = BackupComparer.ReadLines(newer.PathOf(file.New));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or BackupArchiveException)
            {
                continue;
            }

            string?[] varOfOld = Attribute(a);
            string?[] varOfNew = Attribute(b);
            var found = new Dictionary<string, (string Category, string? Old, string? New)>(StringComparer.OrdinalIgnoreCase);

            foreach (DiffLine line in LineDiff.Compute(a, b))
            {
                string? variable = line.Kind switch
                {
                    DiffLineKind.Removed when line.OldNumber is int o => varOfOld[o - 1],
                    DiffLineKind.Added when line.NewNumber is int n => varOfNew[n - 1],
                    _ => null,
                };

                if (variable is null || Categorise(variable) is not { } category)
                {
                    continue;
                }

                found.TryGetValue(variable, out (string Category, string? Old, string? New) seen);
                found[variable] = (category,
                    seen.Old ?? (line.Kind == DiffLineKind.Removed ? line.Text.Trim() : null),
                    seen.New ?? (line.Kind == DiffLineKind.Added ? line.Text.Trim() : null));
            }

            changes.AddRange(found.Select(f => new SettingChange(robot, f.Value.Category, f.Key, file.RelativePath,
                older.FolderName, newer.FolderName, newer.Manifest.StartedUtc, f.Value.Old, f.Value.New)));
        }

        return [.. changes.OrderBy(c => c.Category, StringComparer.Ordinal).ThenBy(c => c.Variable, StringComparer.OrdinalIgnoreCase)];
    }

    /// <summary>
    /// Watched changes across a robot's history: each complete backup against the complete one
    /// before it, for the pairs whose newer backup started on or after <paramref name="since"/>.
    /// </summary>
    public static IReadOnlyList<SettingChange> History(string robot, IEnumerable<BackupSet> backups, DateTimeOffset? since = null, CancellationToken cancellation = default)
    {
        ArgumentNullException.ThrowIfNull(backups);
        List<BackupSet> complete = [.. backups.Where(b => b.Manifest.Outcome == BackupOutcome.Complete).OrderBy(b => b.Manifest.StartedUtc)];
        var changes = new List<SettingChange>();
        for (int i = 1; i < complete.Count; i++)
        {
            cancellation.ThrowIfCancellationRequested();
            if (since is null || complete[i].Manifest.StartedUtc >= since)
            {
                changes.AddRange(Compare(robot, complete[i - 1], complete[i]));
            }
        }

        return [.. changes.OrderByDescending(c => c.NewerUtc)];
    }

    /// <summary>The watch category of a variable name, or null when it is not watched.</summary>
    public static string? Categorise(string variable) =>
        Rules.FirstOrDefault(r => r.Variable.IsMatch(variable))?.Category;

    /// <summary>
    /// For each line of a listing, the variable it belongs to: the most recent header line above it
    /// (<c>$NAME  Storage: ...</c>, or <c>[*SYSTEM*]$NAME ...</c>), or the variable named on the line itself.
    /// </summary>
    internal static string?[] Attribute(IReadOnlyList<string> lines)
    {
        var owner = new string?[lines.Count];
        string? current = null;
        for (int i = 0; i < lines.Count; i++)
        {
            Match header = VariableHeader().Match(lines[i]);
            if (header.Success)
            {
                current = header.Groups[1].Value.ToUpperInvariant();
            }

            owner[i] = current;
        }

        return owner;
    }

    private static Regex Name(string pattern) =>
        new(pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(250));

    [GeneratedRegex(@"^(?:\[\*?[A-Za-z0-9_]+\*?\])?(\$[A-Za-z0-9_]+(?:\[[^\]]*\])?(?:\.\$[A-Za-z0-9_]+(?:\[[^\]]*\])?)*)", RegexOptions.CultureInvariant)]
    private static partial Regex VariableHeader();
}
