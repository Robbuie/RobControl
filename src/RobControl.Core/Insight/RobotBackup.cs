using RobControl.Core.Backup;

namespace RobControl.Core.Insight;

/// <summary>One backup and the robot it belongs to - the unit every fleet-wide view reads.</summary>
public sealed record RobotBackup(string Robot, BackupSet Set)
{
    /// <summary>The backup's folder name, e.g. <c>2026-10-04_221000</c> - how the person finds it in Explorer.</summary>
    public string Stamp => Set.FolderName;

    public DateTimeOffset StartedUtc => Set.Manifest.StartedUtc;
}
