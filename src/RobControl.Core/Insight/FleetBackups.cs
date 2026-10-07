using RobControl.Core.Backup;
using RobControl.Core.Robots;

namespace RobControl.Core.Insight;

/// <summary>
/// Which backups a fleet-wide view reads. Everything here works from the archive on disk - nothing
/// is sent to a controller - so search, alarm history and the report work on a laptop in a hotel.
/// </summary>
public static class FleetBackups
{
    /// <summary>
    /// The newest complete backup of each robot - "the fleet as it is now". A robot with no complete
    /// backup is left out rather than represented by a partial one, which could be missing exactly
    /// the program being searched for.
    /// </summary>
    public static IReadOnlyList<RobotBackup> Latest(BackupArchive archive, IEnumerable<Robot> robots)
    {
        ArgumentNullException.ThrowIfNull(archive);
        ArgumentNullException.ThrowIfNull(robots);
        var result = new List<RobotBackup>();
        foreach (Robot robot in robots)
        {
            if (archive.LatestComplete(robot) is { } set)
            {
                result.Add(new RobotBackup(robot.Name, set));
            }
        }

        return result;
    }

    /// <summary>Every readable backup of each robot, newest first per robot - for history: alarms, setting changes.</summary>
    public static IReadOnlyList<RobotBackup> All(BackupArchive archive, IEnumerable<Robot> robots)
    {
        ArgumentNullException.ThrowIfNull(archive);
        ArgumentNullException.ThrowIfNull(robots);
        return [.. robots.SelectMany(robot => archive.List(robot).Select(set => new RobotBackup(robot.Name, set)))];
    }
}
