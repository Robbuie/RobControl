using System.Globalization;
using System.Text;
using RobControl.Core.Backup;
using RobControl.Core.Robots;

namespace RobControl.Core.History;

/// <summary>
/// "What changed?" - per robot, its newest complete backup against the complete one before it, sorted
/// into programs, settings, register values and noise, with DCS files called out.
///
/// <para><b>From the archive only.</b> Equality is by the manifests' SHA-256, as in
/// <see cref="BackupComparer"/>; text files that differ are diffed to count lines. Nothing is sent to a
/// robot, so it runs after a scheduled backup or on a laptop with a copied archive.</para>
///
/// <para><b>Noise is a guess until real backups say otherwise.</b> <see cref="BuiltInNoise"/> covers what
/// is known to change on its own (diagnostic snapshots, alarm logs); the site's <c>ignoreChanges</c> adds
/// whatever the first real backups show moving by itself.</para>
/// </summary>
public static class ChangeReport
{
    /// <summary>Files that change between any two backups of a running robot.</summary>
    public static IReadOnlyList<string> BuiltInNoise { get; } = ["*.DG", "ERR*.LS", "LOGBOOK*.LS", "LOGBOOK*.DT", "ACCNTG*"];

    /// <summary>Register files: real data, but they move every cycle (counters, positions).</summary>
    public static IReadOnlyList<string> ValueFiles { get; } = ["NUMREG.*", "POSREG.*", "STRREG.*", "FLAG.*"];

    /// <summary>Text files larger than this are compared by hash only - no line count.</summary>
    public const long MaxDiffBytes = 4L * 1024 * 1024;

    /// <summary>Changed lines quoted per program file in the written report.</summary>
    public const int QuotedLines = 12;

    public static ChangeKind KindOf(string relativePath, IEnumerable<string>? ignore = null)
    {
        ArgumentNullException.ThrowIfNull(relativePath);
        string inDevice = InDevice(relativePath);
        if (FileNamePattern.FirstMatch(BuiltInNoise, inDevice) is not null
            || (ignore is not null && FileNamePattern.FirstMatch(ignore, inDevice) is not null))
        {
            return ChangeKind.Noise;
        }

        if (FileNamePattern.FirstMatch(ValueFiles, inDevice) is not null)
        {
            return ChangeKind.Values;
        }

        return Path.GetExtension(relativePath).ToUpperInvariant() switch
        {
            ".TP" or ".LS" or ".PC" or ".VR" or ".MN" => ChangeKind.Program,
            _ => ChangeKind.Setting,
        };
    }

    /// <summary>Dual check safety files: <c>DCS*</c> anywhere, by name.</summary>
    public static bool IsSafetyFile(string relativePath) =>
        Path.GetFileName(relativePath).StartsWith("DCS", StringComparison.OrdinalIgnoreCase)
        && !Path.GetExtension(relativePath).Equals(".DG", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// <paramref name="robot"/>'s newest complete backup against the complete one before it. With
    /// <paramref name="newer"/> given, that backup against the complete one before it instead.
    /// </summary>
    public static RobotChanges ForRobot(BackupArchive archive, Robot robot, IEnumerable<string>? ignore = null, BackupSet? newer = null)
    {
        ArgumentNullException.ThrowIfNull(archive);
        ArgumentNullException.ThrowIfNull(robot);
        List<BackupSet> complete = [.. archive.List(robot).Where(s => s.Manifest.Outcome == BackupOutcome.Complete)];
        newer ??= complete.FirstOrDefault();
        if (newer is null)
        {
            return new RobotChanges(robot.Name, null, null, [], "no complete backup");
        }

        BackupSet? older = complete.FirstOrDefault(s => s.Manifest.StartedUtc < newer.Manifest.StartedUtc);
        if (older is null)
        {
            return new RobotChanges(robot.Name, newer, null, [], "first complete backup - nothing to compare with");
        }

        return Between(robot.Name, older, newer, ignore);
    }

    public static RobotChanges Between(string robot, BackupSet older, BackupSet newer, IEnumerable<string>? ignore = null)
    {
        ArgumentNullException.ThrowIfNull(older);
        ArgumentNullException.ThrowIfNull(newer);
        List<string> patterns = [.. ignore ?? []];
        var files = new List<FileChangeSummary>();
        foreach (FileComparison file in BackupComparer.Compare(older, newer))
        {
            if (file.Change == FileChange.Same)
            {
                continue;
            }

            ChangeKind kind = KindOf(file.RelativePath, patterns);
            (int? added, int? removed) = kind == ChangeKind.Noise ? (null, null) : Count(older, newer, file);
            files.Add(new FileChangeSummary(file.RelativePath, file.Change, kind, added, removed, IsSafetyFile(file.RelativePath)));
        }

        return new RobotChanges(robot, newer, older, files, null);
    }

    public static IReadOnlyList<RobotChanges> ForRobots(BackupArchive archive, IEnumerable<Robot> robots, IEnumerable<string>? ignore = null, CancellationToken cancellation = default)
    {
        ArgumentNullException.ThrowIfNull(robots);
        List<string> patterns = [.. ignore ?? []];
        var result = new List<RobotChanges>();
        foreach (Robot robot in robots)
        {
            cancellation.ThrowIfCancellationRequested();
            try
            {
                result.Add(ForRobot(archive, robot, patterns));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or BackupArchiveException)
            {
                result.Add(new RobotChanges(robot.Name, null, null, [], $"could not read its backups: {ex.Message}"));
            }
        }

        return result;
    }

    /// <summary>"3 of 12 robots changed: R1-04 (2 programs), ..." - the notification and the event row.</summary>
    public static string Headline(IReadOnlyList<RobotChanges> robots)
    {
        ArgumentNullException.ThrowIfNull(robots);
        List<RobotChanges> changed = [.. robots.Where(r => r.HasRealChanges)];
        if (changed.Count == 0)
        {
            return string.Create(CultureInfo.InvariantCulture, $"No program or setting changes on {robots.Count} robot(s) since their previous backup.");
        }

        string safety = changed.Any(r => r.HasSafetyChanges) ? " DCS files changed - check them." : string.Empty;
        string names = string.Join(", ", changed.Take(6).Select(r => $"{r.Robot} ({r.Short})"))
            + (changed.Count > 6 ? string.Create(CultureInfo.InvariantCulture, $" and {changed.Count - 6} more") : string.Empty);
        return string.Create(CultureInfo.InvariantCulture, $"{changed.Count} of {robots.Count} robot(s) changed since their previous backup: {names}.{safety}");
    }

    /// <summary>The written report, in the Markdown subset the in-app reader shows.</summary>
    public static string ToMarkdown(string title, IReadOnlyList<RobotChanges> robots, DateTimeOffset madeUtc, string tool)
    {
        ArgumentNullException.ThrowIfNull(robots);
        var md = new StringBuilder();
        md.AppendLine(CultureInfo.InvariantCulture, $"# What changed - {title}");
        md.AppendLine();
        md.AppendLine(CultureInfo.InvariantCulture, $"*{madeUtc.ToLocalTime():yyyy-MM-dd HH:mm} - {tool}. Each robot's newest complete backup against the complete one before it. Read from the archive; nothing was sent to a robot.*");
        md.AppendLine();
        md.AppendLine(Headline(robots));
        md.AppendLine();
        md.AppendLine("| Robot | Compared | Programs | Settings | Registers | Safety (DCS) |");
        md.AppendLine("|---|---|---|---|---|---|");
        foreach (RobotChanges r in robots)
        {
            string compared = r.Problem ?? $"{r.Older!.FolderName} -> {r.Newer!.FolderName}";
            md.AppendLine(CultureInfo.InvariantCulture,
                $"| {r.Robot} | {compared} | {Count(r, ChangeKind.Program)} | {Count(r, ChangeKind.Setting)} | {Count(r, ChangeKind.Values)} | {(r.HasSafetyChanges ? "**CHANGED**" : "-")} |");
        }

        foreach (RobotChanges r in robots.Where(r => r.Files.Any(f => f.Kind != ChangeKind.Noise)))
        {
            md.AppendLine();
            md.AppendLine(CultureInfo.InvariantCulture, $"## {r.Robot}");
            md.AppendLine();
            md.AppendLine(CultureInfo.InvariantCulture, $"`{r.Older!.FolderName}` -> `{r.Newer!.FolderName}`");
            md.AppendLine();
            foreach (FileChangeSummary f in r.Files.Where(f => f.Kind != ChangeKind.Noise).OrderByDescending(f => f.IsSafety).ThenBy(f => f.Kind).ThenBy(f => f.Path, StringComparer.OrdinalIgnoreCase))
            {
                string safety = f.IsSafety ? " - **safety configuration**" : string.Empty;
                md.AppendLine(CultureInfo.InvariantCulture, $"- `{f.Path}` {f.Describe()} ({Label(f.Kind)}){safety}");
            }

            foreach (FileChangeSummary f in r.Files.Where(f => f.Kind == ChangeKind.Program && f.Change == FileChange.Changed && f.Path.EndsWith(".LS", StringComparison.OrdinalIgnoreCase)))
            {
                IReadOnlyList<string> quoted = Quote(r.Older!, r.Newer!, f.Path);
                if (quoted.Count == 0)
                {
                    continue;
                }

                md.AppendLine();
                md.AppendLine(CultureInfo.InvariantCulture, $"`{f.Path}`:");
                md.AppendLine();
                md.AppendLine("```");
                foreach (string line in quoted)
                {
                    md.AppendLine(line);
                }

                md.AppendLine("```");
            }

            int noise = r.Files.Count(f => f.Kind == ChangeKind.Noise);
            if (noise > 0)
            {
                md.AppendLine();
                md.AppendLine(CultureInfo.InvariantCulture, $"*Also {noise} file(s) that change on their own (diagnostic snapshots, alarm logs, the site's ignore list) - not listed.*");
            }
        }

        return md.ToString();
    }

    private static string Count(RobotChanges r, ChangeKind kind)
    {
        int n = r.Files.Count(f => f.Kind == kind);
        return n == 0 ? "-" : n.ToString(CultureInfo.InvariantCulture);
    }

    private static string Label(ChangeKind kind) => kind switch
    {
        ChangeKind.Program => "program",
        ChangeKind.Setting => "setting",
        ChangeKind.Values => "register values",
        _ => "changes on its own",
    };

    /// <summary>The changed body lines of a listing as "- 42: old" / "+ 42: new", header lines left out.</summary>
    private static IReadOnlyList<string> Quote(BackupSet older, BackupSet newer, string path)
    {
        try
        {
            FileComparison file = BackupComparer.Compare(older, newer).First(f => f.RelativePath.Equals(path, StringComparison.OrdinalIgnoreCase));
            if (!file.CanDiff || TooBig(file.Old) || TooBig(file.New))
            {
                return [];
            }

            var lines = new List<string>();
            foreach (DiffLine line in BackupComparer.DiffFile(older, newer, file))
            {
                if (line.Kind == DiffLineKind.Same || !IsBodyLine(line.Text))
                {
                    continue;
                }

                if (lines.Count == QuotedLines)
                {
                    lines.Add("...");
                    break;
                }

                lines.Add((line.Kind == DiffLineKind.Added ? "+ " : "- ") + line.Text.Trim());
            }

            return lines;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or BackupArchiveException or InvalidOperationException)
        {
            return [];
        }
    }

    /// <summary>A numbered TP line (<c>  42:L P[3] ...</c>) or a position - not the header (MODIFIED, sizes) that changes with every edit.</summary>
    private static bool IsBodyLine(string text)
    {
        string t = text.TrimStart();
        int colon = t.IndexOf(':', StringComparison.Ordinal);
        return (colon > 0 && t[..colon].All(char.IsAsciiDigit)) || t.StartsWith("P[", StringComparison.Ordinal)
            || t.StartsWith('X') || t.StartsWith('W');
    }

    private static (int? Added, int? Removed) Count(BackupSet older, BackupSet newer, FileComparison file)
    {
        if (!file.CanDiff || TooBig(file.Old) || TooBig(file.New))
        {
            return (null, null);
        }

        try
        {
            IReadOnlyList<DiffLine> diff = BackupComparer.DiffFile(older, newer, file);
            return (diff.Count(d => d.Kind == DiffLineKind.Added), diff.Count(d => d.Kind == DiffLineKind.Removed));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or BackupArchiveException)
        {
            return (null, null);
        }
    }

    private static bool TooBig(BackupFileRecord? record) => record is null || record.Bytes > MaxDiffBytes;

    private static string InDevice(string relativePath)
    {
        string path = relativePath.Replace('\\', '/');
        int slash = path.IndexOf('/', StringComparison.Ordinal);
        return slash < 0 ? path : path[(slash + 1)..];
    }
}
