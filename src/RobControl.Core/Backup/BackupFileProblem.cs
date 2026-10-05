namespace RobControl.Core.Backup;

/// <summary>A file that was listed and did not arrive, or was not fetched, and why.</summary>
public sealed record BackupFileProblem(string Device, string Name, string Reason);
