namespace RobControl.Core.Backup;

/// <summary>What a backup run produced: the manifest, and the folder it ended up in (if any).</summary>
public sealed record BackupResult(BackupManifest Manifest, string? FolderPath, string Transcript)
{
    public BackupOutcome Outcome => Manifest.Outcome;
}
