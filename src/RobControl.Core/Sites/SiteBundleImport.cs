using RobControl.Core.Diagnostics;
using RobControl.Core.Events;
using RobControl.Core.Persistence;
using RobControl.Core.Robots;

namespace RobControl.Core.Sites;

/// <summary>
/// Makes a new site from a <see cref="SiteBundle"/>: settings, robots, and - when the bundle has
/// them - its history and backups.
///
/// <para><b>The robot list is checked exactly as a site file's is</b> (unicast, a real IPv4 address,
/// ports in range, no address twice). The database in the bundle is only used when its robot list is
/// the same as the checked one; otherwise the robots go in from the site file alone and the history
/// stays behind, with a sentence saying so. A database is a file somebody could have edited, and it
/// must not be a way round the checks.</para>
///
/// <para>Backups always go into a new folder named after the new site - never into one that already
/// holds another site's backups.</para>
/// </summary>
public static class SiteBundleImport
{
    public static SiteBundleImportResult Import(
        SiteCatalog catalog,
        string zipPath,
        ITraceLog? trace = null,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        trace ??= NullTraceLog.Instance;
        SiteBundleContents contents = SiteBundle.Read(zipPath);

        // Blank archive root: Import gives the new site the default folder for its (possibly suffixed) name.
        SiteFile file = contents.Site with { Settings = contents.Site.Settings with { ArchiveRoot = string.Empty } };
        Site site = catalog.Import(file);
        var skipped = new List<string>();
        List<Robot> robots = Admit(file.Robots, skipped);

        bool history = contents.Info.Database && TryTakeDatabase(zipPath, site, robots, skipped, trace);
        int count;
        using (FleetStore store = FleetStore.Open(site.DatabasePath, trace))
        {
            if (!history)
            {
                foreach (Robot robot in robots)
                {
                    try
                    {
                        store.Save(robot);
                    }
                    catch (RobControlException ex)
                    {
                        skipped.Add($"'{robot.Name}': {ex.Message}");
                    }
                }
            }

            count = store.Robots().Count;

            (int folders, IReadOnlyList<string> notUnpacked) = (0, []);
            if (contents.Info.BackupFolders > 0)
            {
                progress?.Report("Unpacking backups...");
                (folders, notUnpacked) = SiteBundle.ExtractBackups(zipPath, site.Settings.ArchiveRoot, progress, cancellationToken);
                skipped.AddRange(notUnpacked);
            }

            store.Info(EventCategory.App, null,
                $"Site '{site.Name}' imported from the bundle {Path.GetFileName(zipPath)}: {count} robot(s), "
                + (history ? "with its event log and trends" : "without history")
                + $", {folders} backup folder(s); {skipped.Count} item(s) not imported.",
                skipped.Count == 0 ? null : string.Join(Environment.NewLine, skipped));

            trace.Info($"Imported bundle {zipPath} as site '{site.Name}': {count} robots, history {history}, {folders} backup folders, {skipped.Count} skipped.");
            return new SiteBundleImportResult(site, count, history, folders, skipped);
        }
    }

    /// <summary>The site file's robots that pass the same checks as the robot dialog.</summary>
    internal static List<Robot> Admit(IEnumerable<SiteRobot> entries, List<string> skipped)
    {
        var robots = new List<Robot>();
        foreach (SiteRobot entry in entries)
        {
            if (!entry.TryBuild(out Robot? robot, out string? problem))
            {
                skipped.Add(problem!);
                continue;
            }

            // Two rows for one controller would mean two sessions on it.
            if (robots.Any(r => r.Address.Equals(robot!.Address) && r.FtpPort == robot.FtpPort))
            {
                skipped.Add($"'{robot!.Name}': {robot.Address} is already in the list.");
                continue;
            }

            robots.Add(robot!);
        }

        return robots;
    }

    private static bool TryTakeDatabase(string zipPath, Site site, List<Robot> admitted, List<string> skipped, ITraceLog trace)
    {
        string temp = site.DatabasePath + ".import";
        try
        {
            if (!SiteBundle.ExtractDatabase(zipPath, temp))
            {
                return false;
            }

            bool same;
            using (FleetStore candidate = FleetStore.Open(temp, trace))
            {
                same = Key(candidate.Robots()).SetEquals(Key(admitted));
            }

            if (!same)
            {
                skipped.Add("Event log and trends: the database in the bundle does not match its robot list, so it was left out. The robots came from the site file.");
                return false;
            }

            File.Move(temp, site.DatabasePath, overwrite: true);
            return true;
        }
        catch (Exception ex) when (ex is RobControlException or IOException or UnauthorizedAccessException or InvalidDataException
            or System.Data.Common.DbException or FormatException)
        {
            skipped.Add($"Event log and trends: the database in the bundle could not be read ({ex.Message}). The robots came from the site file.");
            return false;
        }
        finally
        {
            foreach (string leftover in new[] { temp, temp + "-wal", temp + "-shm" })
            {
                try
                {
                    File.Delete(leftover);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    trace.Warn($"Could not delete {leftover}.", ex);
                }
            }
        }
    }

    private static HashSet<string> Key(IEnumerable<Robot> robots) =>
        [.. robots.Select(r => $"{r.Name}\n{r.Address}\n{r.FtpPort}\n{r.HttpPort}")];
}
