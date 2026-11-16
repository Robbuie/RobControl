using RobControl.Core.Backup;
using RobControl.Core.History;
using RobControl.Core.Robots;

namespace RobControl.Core.Insight;

/// <summary>A TP program's listing out of a robot's newest complete backup - for "where is it waiting".</summary>
public static class BackupListings
{
    /// <summary>
    /// <c>&lt;PROGRAM&gt;.LS</c> from <paramref name="robot"/>'s newest complete backup, parsed, with when
    /// that backup was taken; null when there is no such backup or file.
    /// </summary>
    public static (ProgramListing Listing, DateTimeOffset TakenUtc)? Find(BackupArchive archive, Robot robot, string program)
    {
        ArgumentNullException.ThrowIfNull(archive);
        ArgumentNullException.ThrowIfNull(robot);
        ArgumentException.ThrowIfNullOrWhiteSpace(program);
        try
        {
            if (archive.LatestComplete(robot) is not { } set)
            {
                return null;
            }

            string name = program.Trim() + ".LS";
            BackupFileRecord? file = set.Manifest.Files.FirstOrDefault(f => Path.GetFileName(f.Name).Equals(name, StringComparison.OrdinalIgnoreCase));
            if (file is null)
            {
                return null;
            }

            ProgramListing? listing = ProgramListingParser.Parse(BackupComparer.ReadLines(set.PathOf(file)));
            return listing is null ? null : (listing, set.Manifest.StartedUtc);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or BackupArchiveException)
        {
            return null;
        }
    }
}
