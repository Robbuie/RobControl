namespace RobControl.Core.Backup;

/// <summary>One file that arrived.</summary>
/// <param name="Device">As asked for, upper-cased: <c>MD:</c>.</param>
/// <param name="Name">The name the controller listed.</param>
/// <param name="RelativePath">Inside the backup folder, forward slashes: <c>MD/SUMMARY.DG</c>.</param>
public sealed record BackupFileRecord(string Device, string Name, string RelativePath, long Bytes, string Sha256);
