namespace RobControl.Core.Insight;

/// <summary>How one robot's backups stand - the column a person scans first on a visit.</summary>
public enum BackupHealth
{
    /// <summary>A complete backup within the site's limit, and the newest attempt worked.</summary>
    Ok,

    /// <summary>
    /// The last good backup is still within the limit, but the attempts since it did not complete -
    /// the robot is going stale and nobody has noticed yet.
    /// </summary>
    Failing,

    /// <summary>The last complete backup is older than the site's limit.</summary>
    Stale,

    /// <summary>No complete backup at all.</summary>
    Never,
}
