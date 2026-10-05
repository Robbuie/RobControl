namespace RobControl.Core.Backup;

/// <summary>Where a running backup has got to, for a progress bar and a status line.</summary>
/// <param name="FileIndex">1-based index of the file being fetched, 0 while listing.</param>
public sealed record BackupProgress(string Robot, string Stage, int FileIndex, int FileCount, string? CurrentFile, long BytesSoFar);
