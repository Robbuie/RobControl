using System.Globalization;
using System.Text;
using System.Text.Json;
using RobControl.Core.Backup;
using RobControl.Core.Insight;
using RobControl.Core.Robots;
using RobControl.Core.Trending;

namespace RobControl.Core.Live;

/// <summary>
/// Writes and lists alarm snapshots: <c>&lt;folder&gt;/&lt;robot&gt;/&lt;stamp&gt;_&lt;CODE&gt;/</c> with
/// <c>snapshot.md</c> (the state in words and tables), <c>snapshot.json</c> (for the list) and every page
/// read under <c>MD/</c> exactly as the controller wrote it. Plain files, like backups and captures, so they
/// open without RobControl.
/// </summary>
public static class AlarmSnapshots
{
    public const string JsonFile = "snapshot.json";
    public const string MarkdownFile = "snapshot.md";

    /// <summary>Digital outputs listed as ON in a snapshot at most - a rack can have thousands.</summary>
    public const int MaxOutputsListed = 200;

    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true };

    public static AlarmSnapshot Write(string root, Robot robot, IReadOnlyList<AlarmEntry> newAlarms, LiveSnapshot state, AlarmWatchOptions? options = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        ArgumentNullException.ThrowIfNull(robot);
        ArgumentNullException.ThrowIfNull(newAlarms);
        ArgumentNullException.ThrowIfNull(state);

        string code = newAlarms.Count == 0 ? "ALARM" : newAlarms[0].Code;
        string robotFolder = Path.Combine(root, ArchiveNames.RobotFolder(robot.Name));
        string folder = Path.Combine(robotFolder, $"{ArchiveNames.Stamp(state.Utc)}_{ArchiveNames.RobotFolder(code)}");
        for (int i = 2; Directory.Exists(folder); i++)
        {
            folder = Path.Combine(robotFolder, string.Create(CultureInfo.InvariantCulture, $"{ArchiveNames.Stamp(state.Utc)}_{ArchiveNames.RobotFolder(code)}_{i}"));
        }

        string md = Path.Combine(folder, "MD");
        Directory.CreateDirectory(md);
        foreach (LiveFile file in state.Files.Where(f => f.IsOk))
        {
            if (ArchiveNames.IsSafeFileName(file.FileName, out _))
            {
                File.WriteAllText(Path.Combine(md, file.FileName), file.Text, Encoding.Latin1);
            }
        }

        IReadOnlyList<RunBlocker> blockers = RunCheck.Evaluate(state);
        string summary = blockers.FirstOrDefault(b => b.Level != BlockerLevel.Note) is { } top
            ? $"{top.What}{(blockers.Count > 1 ? string.Create(CultureInfo.InvariantCulture, $" (+{blockers.Count - 1} more)") : string.Empty)}"
            : "no blocker found on the pages read";
        File.WriteAllText(Path.Combine(folder, MarkdownFile), Markdown(robot, newAlarms, state, blockers, options));
        var json = new
        {
            robot = robot.Name,
            address = robot.Address.ToString(),
            utc = state.Utc,
            newAlarms = newAlarms.Select(a => new { code = a.Code, message = a.Message, when = a.When }),
            summary,
            pages = state.Files.Select(f => new { file = f.FileName, status = f.Status.ToString() }),
        };
        File.WriteAllText(Path.Combine(folder, JsonFile), JsonSerializer.Serialize(json, Json));
        return new AlarmSnapshot(robot.Name, robot.Address.ToString(), state.Utc, newAlarms, folder, summary);
    }

    /// <summary>Every snapshot under <paramref name="root"/>, newest first. Folders without a readable snapshot.json are left out.</summary>
    public static IReadOnlyList<AlarmSnapshot> List(string root)
    {
        if (!Directory.Exists(root))
        {
            return [];
        }

        var list = new List<AlarmSnapshot>();
        foreach (string robotFolder in Directory.EnumerateDirectories(root))
        {
            foreach (string folder in Directory.EnumerateDirectories(robotFolder))
            {
                if (TryRead(folder) is { } snapshot)
                {
                    list.Add(snapshot);
                }
            }
        }

        return [.. list.OrderByDescending(s => s.Utc)];
    }

    private static AlarmSnapshot? TryRead(string folder)
    {
        string path = Path.Combine(folder, JsonFile);
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(path));
            JsonElement r = doc.RootElement;
            var alarms = new List<AlarmEntry>();
            if (r.TryGetProperty("newAlarms", out JsonElement list) && list.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement a in list.EnumerateArray())
                {
                    DateTime? when = a.TryGetProperty("when", out JsonElement w) && w.ValueKind == JsonValueKind.String && w.TryGetDateTime(out DateTime d) ? d : null;
                    string c = a.TryGetProperty("code", out JsonElement ce) ? ce.GetString() ?? string.Empty : string.Empty;
                    string m = a.TryGetProperty("message", out JsonElement me) ? me.GetString() ?? string.Empty : string.Empty;
                    alarms.Add(new AlarmEntry(r.GetProperty("robot").GetString() ?? string.Empty, when, c, m, $"{c} {m}"));
                }
            }

            return new AlarmSnapshot(
                r.GetProperty("robot").GetString() ?? Path.GetFileName(Path.GetDirectoryName(folder)) ?? string.Empty,
                r.TryGetProperty("address", out JsonElement addr) ? addr.GetString() ?? string.Empty : string.Empty,
                r.GetProperty("utc").GetDateTimeOffset(),
                alarms,
                folder,
                r.TryGetProperty("summary", out JsonElement s) ? s.GetString() ?? string.Empty : string.Empty);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or KeyNotFoundException or InvalidOperationException or FormatException)
        {
            return null;
        }
    }

    internal static string Markdown(Robot robot, IReadOnlyList<AlarmEntry> newAlarms, LiveSnapshot state, IReadOnlyList<RunBlocker> blockers, AlarmWatchOptions? options)
    {
        var md = new StringBuilder();
        md.AppendLine(CultureInfo.InvariantCulture, $"# Alarm snapshot - {robot.Name}");
        md.AppendLine();
        md.AppendLine(CultureInfo.InvariantCulture, $"*{robot.Describe()}, read {state.Utc.ToLocalTime():yyyy-MM-dd HH:mm:ss} (this PC's clock), just after the alarm appeared. Read-only: HTTP GET of the controller's own pages.*");
        if (options?.ClockNote?.Invoke() is { } clock)
        {
            md.AppendLine();
            md.AppendLine(clock);
        }

        md.AppendLine();
        md.AppendLine("## New alarm");
        md.AppendLine();
        foreach (AlarmEntry a in newAlarms)
        {
            md.AppendLine(CultureInfo.InvariantCulture, $"- **{a.Code}** {a.Message}{(a.When is { } w ? string.Create(CultureInfo.InvariantCulture, $" ({w:yyyy-MM-dd HH:mm:ss} on the controller)") : string.Empty)}");
        }

        if (state.ActiveAlarms.Count > newAlarms.Count)
        {
            md.AppendLine();
            md.AppendLine("Also active:");
            md.AppendLine();
            foreach (AlarmEntry a in state.ActiveAlarms.Where(a => !newAlarms.Any(n => n.Code == a.Code && n.Message == a.Message)))
            {
                md.AppendLine(CultureInfo.InvariantCulture, $"- {a.Code} {a.Message}");
            }
        }

        md.AppendLine();
        md.AppendLine("## Why it would not run");
        md.AppendLine();
        if (blockers.Count == 0)
        {
            md.AppendLine("Nothing on the pages read says so.");
        }
        else
        {
            md.AppendLine("| | What | Why |");
            md.AppendLine("|---|---|---|");
            foreach (RunBlocker b in blockers)
            {
                md.AppendLine(CultureInfo.InvariantCulture, $"| {b.LevelText} | {Cell(b.What)} | {Cell(b.Why)} |");
            }
        }

        if (state.Programs.Tasks.Count > 0)
        {
            md.AppendLine();
            md.AppendLine("## Tasks");
            md.AppendLine();
            md.AppendLine("| Task | Status | At | Call stack |");
            md.AppendLine("|---|---|---|---|");
            foreach (TaskState t in state.Programs.Tasks)
            {
                md.AppendLine(CultureInfo.InvariantCulture, $"| {t.Name} | {t.Status} | {Cell(t.Where)} | {Cell(t.CallStack)} |");
            }

            if (options?.Listing is { } listing)
            {
                List<WaitLine> lines = [.. WaitCheck.Evaluate(state, listing).Where(l => l.Text is not null)];
                if (lines.Count > 0)
                {
                    md.AppendLine();
                    md.AppendLine("The lines they were at (from the newest backup's listings):");
                    md.AppendLine();
                    md.AppendLine("```");
                    foreach (WaitLine l in lines)
                    {
                        md.AppendLine(CultureInfo.InvariantCulture, $"{l.Program} {l.Line}: {l.Text}");
                        if (l.Signals.Count > 0)
                        {
                            md.AppendLine("    " + l.SignalsText);
                        }
                    }

                    md.AppendLine("```");
                }
            }
        }

        List<SafetySignal> stops = [.. state.SafetySignals];
        List<IoPoint> uop = [.. state.IoPoints.Where(p => p.Type is "UI" or "UO" && p.Index <= 24)];
        if (stops.Count > 0 || uop.Count > 0)
        {
            md.AppendLine();
            md.AppendLine("## Stop signals and UOP");
            md.AppendLine();
            md.AppendLine("| Signal | State | Comment |");
            md.AppendLine("|---|---|---|");
            foreach (SafetySignal s in stops)
            {
                md.AppendLine(CultureInfo.InvariantCulture, $"| {Cell(s.Name)} | {(s.IsOn ? "**TRUE**" : "FALSE")} | |");
            }

            foreach (IoPoint p in uop)
            {
                md.AppendLine(CultureInfo.InvariantCulture, $"| {p.Key} | {p.State} | {Cell(p.Comment)} |");
            }
        }

        List<IoPoint> outputs = [.. state.IoPoints.Where(p => p.Direction == "Output" && p.Type is not "UO" && p.Value != 0)];
        if (outputs.Count > 0)
        {
            md.AppendLine();
            md.AppendLine(CultureInfo.InvariantCulture, $"## Outputs ON ({outputs.Count})");
            md.AppendLine();
            md.AppendLine("| Point | State | Comment |");
            md.AppendLine("|---|---|---|");
            foreach (IoPoint p in outputs.Take(MaxOutputsListed))
            {
                md.AppendLine(CultureInfo.InvariantCulture, $"| {p.Key} | {p.State} | {Cell(p.Comment)} |");
            }
        }

        if (state.Position.FirstOrDefault(f => f.IsJoint) is { } joints)
        {
            md.AppendLine();
            md.AppendLine("## Position");
            md.AppendLine();
            md.AppendLine("| Axis | Value |");
            md.AppendLine("|---|---|");
            foreach (AxisValue axis in joints.Axes)
            {
                md.AppendLine(CultureInfo.InvariantCulture, $"| {axis.Axis} | {axis.Value:0.00} |");
            }
        }

        md.AppendLine();
        md.AppendLine("Every page read is in the MD folder beside this file, exactly as the controller wrote it.");
        return md.ToString();
    }

    private static string Cell(string text) => text.Replace('|', '/').Replace('\n', ' ').Trim();
}
