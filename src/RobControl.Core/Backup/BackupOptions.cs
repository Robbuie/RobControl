namespace RobControl.Core.Backup;

/// <summary>What a backup takes, and how hard it leans on the controller.</summary>
public sealed record BackupOptions
{
    /// <summary>
    /// Controller devices to copy, in order. <c>md:</c> is the one that matters: reading from it
    /// makes the controller generate fresh ASCII listings beside the binaries. <c>fr:</c> (FROM)
    /// is optional and can be large.
    /// </summary>
    public IReadOnlyList<string> Devices { get; init; } = ["md:"];

    /// <summary>
    /// Attempts per file. A controller can fail to generate one listing while it is busy and
    /// produce it fine a second later, so one retry is worth it; more just slows a failing backup.
    /// </summary>
    public int AttemptsPerFile { get; init; } = 2;

    /// <summary>Pause between files. Zero by default; raise it for a controller that struggles.</summary>
    public TimeSpan PauseBetweenFiles { get; init; } = TimeSpan.Zero;

    public Transports.Ftp.FtpClientOptions Ftp { get; init; } = Transports.Ftp.FtpClientOptions.Default;

    public static BackupOptions Default { get; } = new();
}
