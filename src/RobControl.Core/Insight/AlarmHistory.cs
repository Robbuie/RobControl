using RobControl.Core.Backup;
using RobControl.Core.History;

namespace RobControl.Core.Insight;

/// <summary>
/// Alarm history from the alarm logs already in the backups. A controller's log is a rolling window,
/// so consecutive backups overlap; reading all of them and removing the duplicates gives a history
/// longer than the controller itself keeps.
/// </summary>
public static class AlarmHistory
{
    public static IReadOnlyList<AlarmEntry> Collect(IEnumerable<RobotBackup> backups, CancellationToken cancellation = default)
    {
        ArgumentNullException.ThrowIfNull(backups);
        var seen = new HashSet<(string, DateTime?, string, string)>();
        var entries = new List<AlarmEntry>();

        foreach (RobotBackup backup in backups.OrderBy(b => b.StartedUtc))
        {
            foreach (BackupFileRecord file in backup.Set.Manifest.Files.Where(f => AlarmLogParser.IsAlarmLog(f.Name)))
            {
                cancellation.ThrowIfCancellationRequested();
                IReadOnlyList<string> lines;
                try
                {
                    lines = BackupComparer.ReadLines(backup.Set.PathOf(file));
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or BackupArchiveException)
                {
                    continue;
                }

                foreach (AlarmEntry entry in AlarmLogParser.Parse(backup.Robot, lines))
                {
                    // Undated lines cannot be told apart across backups; the raw line stands in for the time.
                    var key = (entry.Robot.ToUpperInvariant(), entry.When, entry.Code, entry.When is null ? entry.Line : entry.Message);
                    if (seen.Add(key))
                    {
                        entries.Add(entry);
                    }
                }
            }
        }

        return [.. entries.OrderByDescending(e => e.When ?? DateTime.MinValue)];
    }

    /// <summary>Entries on or after <paramref name="since"/>; undated entries are kept, since their age is unknown.</summary>
    public static IEnumerable<AlarmEntry> Since(IEnumerable<AlarmEntry> entries, DateTime? since) =>
        since is null ? entries : entries.Where(e => e.When is null || e.When >= since);

    /// <summary>The most frequent codes first - the Pareto that says where the downtime is.</summary>
    public static IReadOnlyList<AlarmCodeSummary> ByCode(IEnumerable<AlarmEntry> entries) =>
        [.. entries.GroupBy(e => e.Code, StringComparer.OrdinalIgnoreCase)
            .Select(g =>
            {
                List<string> robots = [.. g.Select(e => e.Robot).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase)];
                string message = g.GroupBy(e => e.Message).OrderByDescending(m => m.Count()).First().Key;
                return new AlarmCodeSummary(g.Key, message, g.Count(), robots.Count, string.Join(", ", robots),
                    g.Min(e => e.When), g.Max(e => e.When), AlarmConcerns.Classify(g.Key));
            })
            .OrderByDescending(s => s.Count).ThenBy(s => s.Code, StringComparer.OrdinalIgnoreCase)];

    public static IReadOnlyList<RobotAlarmSummary> ByRobot(IEnumerable<AlarmEntry> entries) =>
        [.. entries.GroupBy(e => e.Robot, StringComparer.OrdinalIgnoreCase)
            .Select(g =>
            {
                List<DateTime> times = [.. g.Where(e => e.When is not null).Select(e => e.When!.Value).Order()];
                TimeSpan? mtb = times.Count >= 2 ? (times[^1] - times[0]) / (times.Count - 1) : null;
                string top = g.GroupBy(e => e.Code).OrderByDescending(c => c.Count()).ThenBy(c => c.Key, StringComparer.Ordinal).First().Key;
                return new RobotAlarmSummary(g.Key, g.Count(), g.Select(e => e.Code).Distinct().Count(), top,
                    times.Count > 0 ? times[0] : null, times.Count > 0 ? times[^1] : null, mtb, g.Count(e => e.Concern != AlarmConcern.None));
            })
            .OrderByDescending(s => s.Count).ThenBy(s => s.Robot, StringComparer.OrdinalIgnoreCase)];
}
