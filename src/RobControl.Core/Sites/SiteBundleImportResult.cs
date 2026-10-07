namespace RobControl.Core.Sites;

/// <summary>What importing a bundle made.</summary>
/// <param name="Site">The new site.</param>
/// <param name="Robots">Robots in its list.</param>
/// <param name="History">The event log, last probes and trends came with it.</param>
/// <param name="BackupFolders">Backup folders unpacked into its archive.</param>
/// <param name="Skipped">A sentence for each robot, file or part that did not come across.</param>
public sealed record SiteBundleImportResult(Site Site, int Robots, bool History, int BackupFolders, IReadOnlyList<string> Skipped);
