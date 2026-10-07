using RobControl.Core.Sites;

namespace RobControl.App.Composition;

/// <summary>
/// What the view model may ask of whatever owns the open site. Switching replaces the view model
/// itself (a new database, a new robot list, a new recorder), so it cannot be done from inside it -
/// it asks, and <see cref="AppHost"/> does it.
/// </summary>
public interface ISiteHost
{
    Site Current { get; }

    IReadOnlyList<Site> Sites();

    /// <summary>Closes the open site and opens <paramref name="key"/>. Null when done; otherwise why not, in one sentence.</summary>
    string? Switch(string key);

    /// <summary>Writes the open site's settings. Throws <see cref="SiteException"/> with the reason.</summary>
    Site SaveCurrent(SiteSettings settings);

    /// <summary>Makes an empty site without opening it. Throws <see cref="SiteException"/> with the reason.</summary>
    Site Create(SiteSettings settings);

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
