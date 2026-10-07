using System.Security.Cryptography;
using RobControl.Core.Robots;

namespace RobControl.Core.Backup;

/// <summary>
/// Re-hashes backups on disk and compares them with what their manifests recorded at the time.
///
/// <para>Why: an archive lives for years on laptops, USB sticks and shares. A file that was edited,
/// truncated by a bad copy or eaten by a failing disk looks exactly like a good one in Explorer.
/// The SHA-256 in every manifest is what lets RobControl say "this backup is still the one that was
/// taken" before somebody restores from it.</para>
///
/// <para>Touches only the disk. Nothing is sent to a controller, and nothing is changed.</para>
/// </summary>
public static class BackupVerifier
{
    /// <summary>Files RobControl writes beside the controller's files. Not in the manifest's file list, and expected.</summary>
    private static readonly string[] OwnFiles = [BackupManifest.FileName, BackupRunner.TranscriptFileName];

    public static BackupVerification Verify(BackupSet set, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(set);
        string robot = set.Manifest.RobotName;
        var missing = new List<string>();
        var changed = new List<string>();
        var listed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        int checkedCount = 0;

        foreach (BackupFileRecord file in set.Manifest.Files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string path;
            try
            {
                path = set.PathOf(file);
            }
            catch (BackupArchiveException ex)
            {
                return new BackupVerification(robot, set.FolderPath, checkedCount, missing, changed, [], ex.Message);
            }

            listed.Add(Path.GetFullPath(path));
            if (!File.Exists(path))
            {
                missing.Add(file.RelativePath);
                continue;
            }

            checkedCount++;
            try
            {
                // Size first: cheap, and a truncated copy - the commonest damage - fails it.
                if (new FileInfo(path).Length != file.Bytes || !string.Equals(Hash(path), file.Sha256, StringComparison.OrdinalIgnoreCase))
                {
                    changed.Add(file.RelativePath);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Present but unreadable is as bad as changed for a restore: it cannot be used.
                changed.Add($"{file.RelativePath} (unreadable: {ex.Message})");
            }
        }

        var unlisted = new List<string>();
        try
        {
            string root = Path.GetFullPath(set.FolderPath);
            foreach (string path in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
            {
                string relative = Path.GetRelativePath(root, path).Replace(Path.DirectorySeparatorChar, '/');
                if (!listed.Contains(Path.GetFullPath(path)) && !OwnFiles.Contains(relative, StringComparer.OrdinalIgnoreCase))
                {
                    unlisted.Add(relative);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new BackupVerification(robot, set.FolderPath, checkedCount, missing, changed, unlisted,
                $"The folder could not be listed: {ex.Message}");
        }

        unlisted.Sort(StringComparer.OrdinalIgnoreCase);
        return new BackupVerification(robot, set.FolderPath, checkedCount, missing, changed, unlisted);
    }

    /// <summary>
    /// Every backup of every robot given, newest first per robot, plus a row for each folder that
    /// has no readable manifest (a crashed backup, a folder copied in by hand) - so nothing in the
    /// archive is silently left out of the check.
    /// </summary>
    public static IReadOnlyList<BackupVerification> VerifyAll(
        BackupArchive archive,
        IEnumerable<Robot> robots,
        bool newestOnly = false,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(archive);
        ArgumentNullException.ThrowIfNull(robots);
        var results = new List<BackupVerification>();

        foreach (Robot robot in robots)
        {
            var unreadable = new List<string>();
            IReadOnlyList<BackupSet> sets = archive.List(robot, unreadable);
            foreach (BackupSet set in newestOnly ? sets.Take(1) : sets)
            {
                cancellationToken.ThrowIfCancellationRequested();
                progress?.Report($"{robot.Name} {set.FolderName}");
                results.Add(Verify(set, cancellationToken));
            }

            if (!newestOnly)
            {
                foreach (string folder in unreadable.Order(StringComparer.OrdinalIgnoreCase))
                {
                    string why = folder.EndsWith(ArchiveNames.InProgressSuffix, StringComparison.OrdinalIgnoreCase)
                        ? "Never finished - a backup that was interrupted (RobControl closed or the PC slept). It has no manifest to check against."
                        : "No readable manifest.json - it cannot be checked, and RobControl does not show it in the history.";
                    results.Add(new BackupVerification(robot.Name, folder, 0, [], [], [], why));
                }
            }
        }

        return results;
    }

    private static string Hash(string path)
    {
        using FileStream stream = File.OpenRead(path);
        return Convert.ToHexStringLower(SHA256.HashData(stream));
    }
}
