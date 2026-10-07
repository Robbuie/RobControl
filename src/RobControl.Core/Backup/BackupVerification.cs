namespace RobControl.Core.Backup;

/// <summary>
/// What re-reading one backup folder against its manifest found. Read-only: nothing is repaired,
/// renamed or deleted - a damaged backup is reported, and the person decides what to do with it.
/// </summary>
/// <param name="Robot">The robot the manifest names.</param>
/// <param name="FolderPath">The backup folder that was checked.</param>
/// <param name="FilesChecked">Files the manifest lists that were found and hashed.</param>
/// <param name="Missing">Manifest paths with no file on disk.</param>
/// <param name="Changed">Manifest paths whose file no longer has the size or SHA-256 recorded when it was taken.</param>
/// <param name="Unlisted">Files in the folder the manifest does not mention - added by hand, or left by another tool.</param>
/// <param name="Problem">Set when the folder could not be checked at all: no readable manifest, or a manifest pointing outside the folder.</param>
public sealed record BackupVerification(
    string Robot,
    string FolderPath,
    int FilesChecked,
    IReadOnlyList<string> Missing,
    IReadOnlyList<string> Changed,
    IReadOnlyList<string> Unlisted,
    string? Problem = null)
{
    public string FolderName => Path.GetFileName(FolderPath);

    /// <summary>
    /// Every listed file is present and unchanged. An unlisted extra file does not make a backup
    /// bad - everything it promised is still there - so it is reported but not counted against it.
    /// </summary>
    public bool IsIntact => Problem is null && Missing.Count == 0 && Changed.Count == 0;

    /// <summary>One line for the grid and the event log.</summary>
    public string Summary
    {
        get
        {
            if (Problem is not null)
            {
                return Problem;
            }

            if (IsIntact)
            {
                return Unlisted.Count == 0
                    ? $"Intact - {FilesChecked} files match the manifest."
                    : $"Intact - {FilesChecked} files match the manifest; {Unlisted.Count} extra file(s) not in it.";
            }

            var parts = new List<string>();
            if (Missing.Count > 0)
            {
                parts.Add($"{Missing.Count} missing ({Names(Missing)})");
            }

            if (Changed.Count > 0)
            {
                parts.Add($"{Changed.Count} changed since it was taken ({Names(Changed)})");
            }

            return "DAMAGED - " + string.Join("; ", parts) + ". Do not restore from this backup.";
        }
    }

    private static string Names(IReadOnlyList<string> paths) =>
        paths.Count <= 3 ? string.Join(", ", paths) : string.Join(", ", paths.Take(3)) + $" and {paths.Count - 3} more";
}
