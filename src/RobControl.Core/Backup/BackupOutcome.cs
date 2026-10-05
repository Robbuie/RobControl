namespace RobControl.Core.Backup;

/// <summary>How a backup ended. Anything but <see cref="Complete"/> leaves a folder marked INCOMPLETE.</summary>
public enum BackupOutcome
{
    /// <summary>Every listed file arrived and was hashed.</summary>
    Complete,

    /// <summary>The session worked but some files did not arrive. The manifest lists which and why.</summary>
    Partial,

    /// <summary>Could not connect, log in, or list. Nothing usable.</summary>
    Failed,

    /// <summary>Stopped by the person.</summary>
    Cancelled,
}
