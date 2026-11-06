using RobControl.Core.Robots;
using RobControl.Core.Sites;
using RobControl.Core.Systems;

namespace RobControl.App.Composition;

/// <summary>
/// What the view model may ask of whatever owns the open site and system. Switching replaces the view
/// model itself (a new robot list, a new recorder - and, for another site, a new database), so it
/// cannot be done from inside it - it asks, and <see cref="AppHost"/> does it.
/// </summary>
public interface ISiteHost : ISystemDirectory
{
    Site Current { get; }

    /// <summary>The open system - or the "Not in a system" entry of the open site.</summary>
    RobotSystem CurrentSystem { get; }

    IReadOnlyList<Site> Sites();

    /// <summary>The open site's systems, by name.</summary>
    IReadOnlyList<RobotSystem> Systems();

    /// <summary>Closes what is open and opens <paramref name="choice"/>. Null when done; otherwise why not, in one sentence.</summary>
    string? Open(SystemChoice choice);

    /// <summary>Closes the open site and opens <paramref name="key"/> at its first system. Null when done; otherwise why not.</summary>
    string? Switch(string key);

    /// <summary>Writes the open site's settings. Throws <see cref="SiteException"/> with the reason.</summary>
    Site SaveCurrent(SiteSettings settings);

    /// <summary>Writes the open system's settings. Throws <see cref="RobotSystemException"/> with the reason.</summary>
    RobotSystem SaveSystem(SystemSettings settings);

    /// <summary>Moves a robot of the open site into another of its systems. Throws <see cref="RobotSystemException"/>.</summary>
    RobotSystem MoveRobot(Robot robot, string systemKey);

    /// <summary>Takes a robot out of every system of the open site - it is being removed.</summary>
    void ForgetRobot(Robot robot);

    /// <summary>
    /// Makes a new site from an export file, robots and all, without opening it. Throws
    /// <see cref="Core.RobControlException"/> with the reason.
    /// </summary>
    SiteImportResult Import(string path);

    /// <summary>
    /// Makes a new site from a site bundle - settings, robots, history and backups - without opening
    /// it. Slow with backups inside: call it off the UI thread. Throws <see cref="Core.RobControlException"/>.
    /// </summary>
    SiteBundleImportResult ImportBundle(string path, IProgress<string>? progress = null, CancellationToken cancellationToken = default);
}
