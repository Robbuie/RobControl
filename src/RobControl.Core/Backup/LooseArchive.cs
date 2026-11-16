using System.Net;
using RobControl.Core.Net;
using RobControl.Core.Robots;

namespace RobControl.Core.Backup;

/// <summary>
/// Backups opened from a folder, with no site: a stick from another engineer, a share, a folder copied
/// off a laptop. Read-only - nothing here writes to the folder, and nothing is sent to a robot.
///
/// <para>Three shapes are recognised, so whichever level of the archive someone copied opens:
/// one backup folder (it has a <c>manifest.json</c>), one robot's folder (backup folders under it), or a
/// whole archive (robot folders under it). The robots are made up from the folder names and the newest
/// manifest in each - they are not saved anywhere and never probed.</para>
/// </summary>
public sealed record LooseArchive(string Folder, string Shape, BackupArchive Archive, IReadOnlyList<Robot> Robots)
{
    /// <summary>The address a loose robot gets when its manifest's is missing or not a unicast address: TEST-NET-1, never a real robot.</summary>
    public static readonly IPAddress Placeholder = IPAddress.Parse("192.0.2.1");

    /// <summary>Robot folders read from one archive at most - a mis-chosen drive root should not be read end to end.</summary>
    private const int MaxRobots = 2000;

    public static LooseArchive Open(string folder)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folder);
        string full = Path.GetFullPath(folder).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (!Directory.Exists(full))
        {
            throw new BackupArchiveException($"{full} does not exist or cannot be opened.");
        }

        // One backup: <root>/<robot>/<stamp>/manifest.json. The robot is its parent folder, so the
        // backups beside it (if they were copied too) come with it.
        if (File.Exists(Path.Combine(full, BackupManifest.FileName)))
        {
            string? robotFolder = Path.GetDirectoryName(full);
            string? root = robotFolder is null ? null : Path.GetDirectoryName(robotFolder);
            if (robotFolder is null || root is null)
            {
                throw new BackupArchiveException($"{full} is a backup at the top of a drive. Copy it into a folder named after the robot and open that.");
            }

            return new LooseArchive(full, "one backup", new BackupArchive(root), [RobotFor(robotFolder)]);
        }

        List<string> children = [.. SafeDirectories(full)];
        if (children.Any(HasManifest))
        {
            string root = Path.GetDirectoryName(full)
                ?? throw new BackupArchiveException($"{full} holds backups at the top of a drive. Copy them into a folder named after the robot and open that.");
            return new LooseArchive(full, "one robot", new BackupArchive(root), [RobotFor(full)]);
        }

        List<Robot> robots = [.. children.Where(c => SafeDirectories(c).Any(HasManifest)).Take(MaxRobots).Select(RobotFor)
            .OrderBy(r => r.Name, StringComparer.OrdinalIgnoreCase)];
        if (robots.Count == 0)
        {
            throw new BackupArchiveException(
                $"No RobControl backups found in {full}. Open a backup folder (it has a manifest.json), the robot's folder above it, or the archive folder above that.");
        }

        return new LooseArchive(full, "archive", new BackupArchive(full), robots);
    }

    /// <summary>"3 robots, 41 backups in D:\Stick\Backups (archive)".</summary>
    public string Describe()
    {
        int backups = Robots.Sum(r => Archive.List(r).Count);
        return $"{Robots.Count} robot(s), {backups} backup(s) in {Folder} ({Shape})";
    }

    private static bool HasManifest(string folder) => File.Exists(Path.Combine(folder, BackupManifest.FileName));

    private static IEnumerable<string> SafeDirectories(string folder)
    {
        try
        {
            return Directory.EnumerateDirectories(folder).ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    /// <summary>Named after its folder - that is what the archive looks it up by - with the newest manifest's address when it has a usable one.</summary>
    private static Robot RobotFor(string robotFolder)
    {
        string name = Path.GetFileName(robotFolder);
        IPAddress address = Placeholder;
        BackupManifest? newest = SafeDirectories(robotFolder)
            .Select(BackupArchive.TryRead)
            .Where(s => s is not null)
            .OrderByDescending(s => s!.Manifest.StartedUtc)
            .FirstOrDefault()?.Manifest;
        if (newest is not null && IPAddress.TryParse(newest.Address, out IPAddress? parsed) && UnicastTarget.TryCheck(parsed, null, out _))
        {
            address = parsed;
        }

        return new Robot(name, address) { Line = newest?.Line, Notes = "Opened from a folder - not in any site." };
    }
}
