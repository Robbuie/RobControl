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

    /// <summary>
    /// Also copy the folders inside a device, down to <see cref="MaxFolderDepth"/>. Off here so a bare
    /// <see cref="BackupOptions"/> behaves as it always has; a system's backup settings turn it on.
    /// A folder found with this off is noted in the manifest and does not make the backup partial.
    /// </summary>
    public bool IncludeSubfolders { get; init; }

    /// <summary>How deep below a device a backup goes. Controller file systems are shallow; this is a guard.</summary>
    public const int MaxFolderDepth = 4;

    /// <summary>
    /// Patterns for files not to copy, matched against the name inside the device (<c>SUB/FILE.TP</c>)
    /// and the bare file name: <c>*.DG</c>, <c>LOG*/*</c>. See <see cref="FileNamePattern"/>.
    /// </summary>
    public IReadOnlyList<string> Exclude { get; init; } = [];

    public Transports.Ftp.FtpClientOptions Ftp { get; init; } = Transports.Ftp.FtpClientOptions.Default;

    public static BackupOptions Default { get; } = new();
}
