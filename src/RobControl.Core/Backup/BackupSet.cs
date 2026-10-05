namespace RobControl.Core.Backup;

/// <summary>One backup folder on disk and what its manifest says.</summary>
public sealed record BackupSet(string FolderPath, BackupManifest Manifest)
{
    public string FolderName => Path.GetFileName(FolderPath);

    /// <summary>The full path of a file inside this backup, from its manifest-relative path.</summary>
    public string PathOf(BackupFileRecord file)
    {
        ArgumentNullException.ThrowIfNull(file);
        string full = Path.GetFullPath(Path.Combine(FolderPath, file.RelativePath.Replace('/', Path.DirectorySeparatorChar)));

        // A manifest is a file somebody could edit. It does not get to point outside its folder.
        string root = Path.GetFullPath(FolderPath).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return full.StartsWith(root, StringComparison.OrdinalIgnoreCase)
            ? full
            : throw new BackupArchiveException($"The manifest in {FolderName} names a file outside the backup: {file.RelativePath}");
    }
}
