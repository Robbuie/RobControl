using RobControl.Core.Robots;

namespace RobControl.Core.Backup;

/// <summary>
/// The archive: <c>&lt;root&gt;/&lt;robot&gt;/&lt;yyyy-MM-dd_HHmmss&gt;[_INCOMPLETE]/&lt;DEVICE&gt;/&lt;file&gt;</c>
/// plus a <c>manifest.json</c> in each backup folder.
///
/// <para>Plain folders on purpose (CLAUDE.md, "Backups - the archive"): an engineer with Explorer
/// and no RobControl must still be able to find and use a backup.</para>
/// </summary>
public sealed class BackupArchive
{
    public BackupArchive(string root)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        Root = Path.GetFullPath(root);
    }

    public string Root { get; }

    public string RobotFolder(Robot robot)
    {
        ArgumentNullException.ThrowIfNull(robot);
        return Path.Combine(Root, ArchiveNames.RobotFolder(robot.Name));
    }

    /// <summary>
    /// Every readable backup of <paramref name="robot"/>, newest first. A folder without a readable
    /// manifest is reported in <paramref name="unreadable"/> rather than silently dropped - an
    /// interrupted copy should be visible, not invisible.
    /// </summary>
    public IReadOnlyList<BackupSet> List(Robot robot, ICollection<string>? unreadable = null)
    {
        string folder = RobotFolder(robot);
        if (!Directory.Exists(folder))
        {
            return [];
        }

        var sets = new List<BackupSet>();
        foreach (string dir in Directory.EnumerateDirectories(folder))
        {
            if (dir.EndsWith(ArchiveNames.InProgressSuffix, StringComparison.OrdinalIgnoreCase))
            {
                // Still being written, or left by a crash. Not a backup either way.
                unreadable?.Add(dir);
                continue;
            }

            BackupSet? set = TryRead(dir);
            if (set is null)
            {
                unreadable?.Add(dir);
            }
            else
            {
                sets.Add(set);
            }
        }

        return [.. sets.OrderByDescending(s => s.Manifest.StartedUtc)];
    }

    /// <summary>The newest backup whose outcome is <see cref="BackupOutcome.Complete"/>, or null.</summary>
    public BackupSet? LatestComplete(Robot robot) =>
        List(robot).FirstOrDefault(s => s.Manifest.Outcome == BackupOutcome.Complete);

    public static BackupSet? TryRead(string folder)
    {
        string path = Path.Combine(folder, BackupManifest.FileName);
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            return new BackupSet(folder, BackupManifest.FromJson(File.ReadAllText(path)));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
            or System.Text.Json.JsonException or BackupArchiveException)
        {
            return null;
        }
    }
}
