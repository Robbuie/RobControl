namespace RobControl.Core.Backup;

/// <summary>One backup folder a retention plan would remove.</summary>
public sealed record RetentionCandidate(string Robot, string FolderPath, DateTimeOffset StartedUtc, BackupOutcome Outcome, long Bytes)
{
    public string FolderName => Path.GetFileName(FolderPath);
}
