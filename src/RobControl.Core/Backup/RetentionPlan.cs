using RobControl.Core.Robots;

namespace RobControl.Core.Backup;

/// <summary>
/// Which old backups a "keep the newest N" rule would remove - worked out, shown, and only acted on
/// when the person says so. Nothing in RobControl deletes a backup on its own (CLAUDE.md, archive).
///
/// <para>The rule, per robot:</para>
/// <list type="bullet">
/// <item>The newest <c>keep</c> complete backups stay, and so does everything newer than the oldest
/// of them - a recent failed attempt is evidence of a problem, not clutter.</item>
/// <item>Older complete backups go, and so do incomplete ones older than the oldest kept complete
/// backup: a newer complete one supersedes them.</item>
/// <item>A robot with fewer than <c>keep</c> complete backups loses nothing.</item>
/// <item>Folders without a readable manifest are never touched: RobControl cannot tell what they are.</item>
/// </list>
/// </summary>
public sealed record RetentionPlan(int Keep, IReadOnlyList<RetentionCandidate> Remove)
{
    public long Bytes => Remove.Sum(r => r.Bytes);

    public static RetentionPlan Build(BackupArchive archive, IEnumerable<Robot> robots, int keep)
    {
        ArgumentNullException.ThrowIfNull(archive);
        ArgumentNullException.ThrowIfNull(robots);
        ArgumentOutOfRangeException.ThrowIfLessThan(keep, 1);

        var remove = new List<RetentionCandidate>();
        foreach (Robot robot in robots)
        {
            IReadOnlyList<BackupSet> sets = archive.List(robot); // newest first
            BackupSet[] complete = [.. sets.Where(s => s.Manifest.Outcome == BackupOutcome.Complete)];
            if (complete.Length <= keep)
            {
                continue;
            }

            DateTimeOffset oldestKept = complete[keep - 1].Manifest.StartedUtc;
            foreach (BackupSet set in sets.Where(s => s.Manifest.StartedUtc < oldestKept))
            {
                remove.Add(new RetentionCandidate(robot.Name, set.FolderPath, set.Manifest.StartedUtc, set.Manifest.Outcome, FolderBytes(set.FolderPath)));
            }
        }

        return new RetentionPlan(keep, [.. remove.OrderBy(r => r.Robot, StringComparer.OrdinalIgnoreCase).ThenBy(r => r.StartedUtc)]);
    }

    private static long FolderBytes(string folder)
    {
        try
        {
            return Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories).Sum(f => new FileInfo(f).Length);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return 0;
        }
    }
}
