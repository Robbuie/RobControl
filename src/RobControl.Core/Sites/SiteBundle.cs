using System.IO.Compression;
using System.Text;
using System.Text.Json;
using RobControl.Core.Backup;
using RobControl.Core.Persistence;
using RobControl.Core.Robots;

namespace RobControl.Core.Sites;

/// <summary>
/// A whole site in one zip, to move it to another laptop or out of a plant that has no network to
/// the outside: the site file (settings and robot list), a snapshot of the site's database (event
/// log, last probes, trends), and - when asked - every backup in its archive.
///
/// <para>Like a site file, a bundle imports as a <b>new</b> site and is never merged into one that
/// exists. The zip's own layout under <c>backups/</c> is the archive layout, so it can also be opened
/// in Explorer and a backup copied out by hand.</para>
///
/// <para>Everything read from a bundle is untrusted: every entry name is checked the way a file name
/// from a controller is (<see cref="ArchiveNames.IsSafeFileName"/>) and must land inside the target
/// folder, or it is skipped and named.</para>
/// </summary>
public static class SiteBundle
{
    public const string Extension = ".robcontrol-bundle.zip";
    public const string InfoEntry = "bundle.json";
    public const string SiteEntry = "site.robcontrol-site.json";
    public const string DatabaseEntry = "robcontrol.db";
    public const string BackupsPrefix = "backups/";

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    /// <summary>
    /// Bundles the open site: its settings and <paramref name="robots"/>, a snapshot of
    /// <paramref name="store"/>, and with <paramref name="includeBackups"/> every backup in
    /// <paramref name="archive"/>. The snapshot is taken to a temporary file and removed afterwards.
    /// </summary>
    public static SiteBundleInfo Export(
        Site site,
        IReadOnlyList<Robot> robots,
        FleetStore store,
        BackupArchive archive,
        bool includeBackups,
        string zipPath,
        string? tool = null,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(site);
        ArgumentNullException.ThrowIfNull(store);
        string snapshot = Path.Combine(Path.GetTempPath(), $"robcontrol-bundle-{Guid.NewGuid():N}.db");
        try
        {
            progress?.Report("Copying the site database...");
            store.SnapshotTo(snapshot);
            return Write(zipPath, SiteCatalog.ToFile(site, robots, tool), snapshot, includeBackups ? archive : null, robots, tool, progress, cancellationToken);
        }
        finally
        {
            TryDelete(snapshot);
        }
    }

    /// <summary>
    /// Writes the bundle. <paramref name="databaseSnapshot"/> is a copy of the site database made for
    /// the purpose (<c>FleetStore.SnapshotTo</c>), never the live file. With <paramref name="archive"/>
    /// null, no backups are included.
    /// </summary>
    public static SiteBundleInfo Write(
        string zipPath,
        SiteFile site,
        string? databaseSnapshot,
        BackupArchive? archive,
        IEnumerable<Robot> robots,
        string? tool = null,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(zipPath);
        ArgumentNullException.ThrowIfNull(site);
        ArgumentNullException.ThrowIfNull(robots);

        // Each robot's readable backup folders. Interrupted (.partial) and manifest-less folders
        // stay behind: an import could not show them in a history anyway.
        var folders = new List<(string Robot, BackupSet Set)>();
        if (archive is not null)
        {
            foreach (Robot robot in robots)
            {
                folders.AddRange(archive.List(robot).Select(set => (ArchiveNames.RobotFolder(robot.Name), set)));
            }
        }

        string temp = zipPath + ".tmp";
        long backupBytes = 0;
        SiteBundleInfo info;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(zipPath))!);
            using (FileStream stream = File.Create(temp))
            using (var zip = new ZipArchive(stream, ZipArchiveMode.Create))
            {
                info = new SiteBundleInfo
                {
                    Format = SiteBundleInfo.FormatName,
                    ExportedUtc = DateTimeOffset.UtcNow.ToString("O", System.Globalization.CultureInfo.InvariantCulture),
                    ExportedBy = tool,
                    SiteName = site.Settings.Name,
                    Robots = site.Robots.Count,
                    Database = databaseSnapshot is not null,
                    BackupFolders = folders.Count,
                };

                WriteText(zip, SiteEntry, SiteCatalog.ToJson(site));
                if (databaseSnapshot is not null)
                {
                    progress?.Report("the site database");
                    zip.CreateEntryFromFile(databaseSnapshot, DatabaseEntry, CompressionLevel.Optimal);
                }

                foreach ((string robotFolder, BackupSet set) in folders)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    progress?.Report($"{robotFolder} {set.FolderName}");
                    string root = Path.GetFullPath(set.FolderPath);
                    foreach (string file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
                    {
                        string relative = Path.GetRelativePath(root, file).Replace(Path.DirectorySeparatorChar, '/');
                        zip.CreateEntryFromFile(file, $"{BackupsPrefix}{robotFolder}/{set.FolderName}/{relative}", CompressionLevel.Optimal);
                        backupBytes += new FileInfo(file).Length;
                    }
                }

                // Written last, once the byte count is known; readers find it by name.
                info = info with { BackupBytes = backupBytes };
                WriteText(zip, InfoEntry, JsonSerializer.Serialize(info, Json));
            }

            // Only a finished zip takes the real name: a cancelled or failed export leaves nothing
            // that looks like a bundle.
            File.Move(temp, zipPath, overwrite: true);
            return info;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            TryDelete(temp);
            throw new SiteException($"The bundle could not be written to {zipPath}: {ex.Message}", ex)
            {
                Remediation = "Check there is room on that drive and the folder is writable.",
            };
        }
        catch (OperationCanceledException)
        {
            TryDelete(temp);
            throw;
        }
    }

    /// <summary>What a bundle holds, or a <see cref="SiteException"/> saying in one sentence why it is not one.</summary>
    public static SiteBundleContents Read(string zipPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(zipPath);
        try
        {
            using ZipArchive zip = ZipFile.OpenRead(zipPath);
            SiteBundleInfo? info = zip.GetEntry(InfoEntry) is { } infoEntry
                ? JsonSerializer.Deserialize<SiteBundleInfo>(ReadText(infoEntry))
                : null;
            if (info is null || !string.Equals(info.Format, SiteBundleInfo.FormatName, StringComparison.Ordinal))
            {
                throw NotABundle(zipPath);
            }

            if (info.Version > SiteBundleInfo.CurrentVersion)
            {
                throw new SiteException($"{zipPath} was written by a newer RobControl (bundle version {info.Version}).")
                {
                    Remediation = "Update RobControl on this PC, then import it again.",
                };
            }

            ZipArchiveEntry site = zip.GetEntry(SiteEntry) ?? throw NotABundle(zipPath);
            return new SiteBundleContents(info, SiteCatalog.ParseExport(ReadText(site), zipPath));
        }
        catch (Exception ex) when (ex is InvalidDataException or JsonException)
        {
            throw new SiteException($"{zipPath} is not a RobControl site bundle: {ex.Message}", ex)
            {
                Remediation = "Pick a file written by Site > Export site bundle.",
            };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new SiteException($"{zipPath} could not be read: {ex.Message}", ex);
        }
    }

    /// <summary>Unpacks the database snapshot to <paramref name="targetPath"/>. False when the bundle has none.</summary>
    public static bool ExtractDatabase(string zipPath, string targetPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(zipPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetPath);
        using ZipArchive zip = ZipFile.OpenRead(zipPath);
        if (zip.GetEntry(DatabaseEntry) is not { } entry)
        {
            return false;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(targetPath))!);
        entry.ExtractToFile(targetPath, overwrite: true);
        return true;
    }

    /// <summary>
    /// Unpacks the backups into <paramref name="archiveRoot"/>, in the archive layout. A backup folder
    /// that already exists there is left exactly as it is - never overwritten, never merged - and named
    /// in <c>Skipped</c>, as is any entry whose name is not safe to write.
    /// </summary>
    public static (int Folders, IReadOnlyList<string> Skipped) ExtractBackups(
        string zipPath,
        string archiveRoot,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(zipPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(archiveRoot);
        string root = Path.GetFullPath(archiveRoot);
        string rootWithSeparator = root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var skipped = new List<string>();
        var existing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var written = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        using ZipArchive zip = ZipFile.OpenRead(zipPath);
        foreach (ZipArchiveEntry entry in zip.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!entry.FullName.StartsWith(BackupsPrefix, StringComparison.Ordinal) || entry.FullName.EndsWith('/'))
            {
                continue;
            }

            string[] parts = entry.FullName[BackupsPrefix.Length..].Split('/');
            string? problem = null;
            if (parts.Length < 3)
            {
                problem = "not inside a robot's backup folder";
            }
            else if (parts[1].EndsWith(ArchiveNames.InProgressSuffix, StringComparison.OrdinalIgnoreCase))
            {
                problem = "an interrupted backup";
            }
            else
            {
                foreach (string part in parts)
                {
                    if (!ArchiveNames.IsSafeFileName(part, out problem))
                    {
                        break;
                    }
                }
            }

            string folderKey = parts.Length >= 2 ? parts[0] + "/" + parts[1] : entry.FullName;
            string folder = Path.GetFullPath(Path.Combine(root, parts[0], parts.Length > 1 ? parts[1] : string.Empty));
            string target = Path.GetFullPath(Path.Combine([root, .. parts]));
            if (problem is null && !target.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase))
            {
                problem = "would land outside the archive folder";
            }

            if (problem is not null)
            {
                skipped.Add($"{entry.FullName}: {problem}");
                continue;
            }

            // Decided once per backup folder, before the first of its files is written.
            if (!written.Contains(folderKey) && !existing.Contains(folderKey) && Directory.Exists(folder))
            {
                existing.Add(folderKey);
                skipped.Add($"{folderKey}: already in the archive here, left as it is");
            }

            if (existing.Contains(folderKey))
            {
                continue;
            }

            if (written.Add(folderKey))
            {
                progress?.Report(folderKey);
            }

            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                entry.ExtractToFile(target, overwrite: false);
            }
            catch (IOException ex)
            {
                // A name twice in one zip, most likely. The first copy stays.
                skipped.Add($"{entry.FullName}: {ex.Message}");
            }
        }

        return (written.Count, skipped);
    }

    private static SiteException NotABundle(string path) =>
        new($"{path} is not a RobControl site bundle.") { Remediation = "Pick a file written by Site > Export site bundle." };

    private static void WriteText(ZipArchive zip, string name, string text)
    {
        ZipArchiveEntry entry = zip.CreateEntry(name, CompressionLevel.Optimal);
        using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        writer.Write(text);
    }

    private static string ReadText(ZipArchiveEntry entry)
    {
        // A site file is small. A "site file" entry the size of a disk is not one.
        if (entry.Length > 16 * 1024 * 1024)
        {
            throw new InvalidDataException($"{entry.FullName} is {entry.Length} bytes - far too large to be what it says.");
        }

        using var reader = new StreamReader(entry.Open(), Encoding.UTF8);
        return reader.ReadToEnd();
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }
}
