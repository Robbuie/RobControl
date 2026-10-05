using RobControl.Core.Backup;

namespace RobControl.Core.History;

/// <summary>One file across two backups. Either side is null when the file is only in the other.</summary>
public sealed record FileComparison(string RelativePath, FileChange Change, BackupFileRecord? Old, BackupFileRecord? New)
{
    /// <summary>Whether a line diff makes sense: both sides exist and look like text.</summary>
    public bool CanDiff { get; init; }

    /// <summary>
    /// A <c>.DG</c> diagnostic file: a snapshot of live state (positions, clocks, counters) that
    /// differs between any two backups by nature. Shown, but filtered out of "what changed" by
    /// default so real changes are not buried under them.
    /// </summary>
    public bool IsLiveData => Path.GetExtension(RelativePath).Equals(".DG", StringComparison.OrdinalIgnoreCase);
}
