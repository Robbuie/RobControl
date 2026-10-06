// UseWPF drops System.IO from the implicit usings.
using System.IO;
using RobControl.App.Diagnostics;
using RobControl.App.ViewModels;
using RobControl.Core;
using RobControl.Core.Diagnostics;
using RobControl.Core.Events;
using RobControl.Core.Persistence;
using RobControl.Core.Robots;
using RobControl.Core.Sites;

namespace RobControl.App.Composition;

/// <summary>
/// Builds the object graph and owns everything with a lifetime. No container: the graph is a
/// handful of objects, and what talks to what should be readable off one page.
///
/// <para><b>One site open at a time.</b> The open site owns the fleet database and so the view model
/// built on it. Switching site disposes both - which stops any trend recording through the recorder's
/// own stop path, so the event log still says it stopped - and builds them again on the other
/// site's database. <see cref="ViewModelChanged"/> tells the window to rebind.</para>
/// </summary>
public sealed class AppHost : IDisposable, ISiteHost
{
    /// <summary>What the first site is called, on a new PC or when 0.2.0's robot list is moved into one. Renamable.</summary>
    public const string FirstSiteName = "My site";

    private readonly IUiDispatcher _dispatcher;
    private readonly ITraceLog _trace;
    private readonly SiteCatalog _catalog;
    private FleetStore _store;
    private Site _site;
    private bool _disposed;

    /// <param name="requestedSite">From <c>--site</c>: a site key or name. Null opens the one used last.</param>
    public AppHost(IUiDispatcher dispatcher, ITraceLog? trace = null, string? requestedSite = null)
    {
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        _trace = trace ?? NullTraceLog.Instance;
        _catalog = new SiteCatalog(AppPaths.Sites, AppPaths.ArchiveBase, _trace);

        _site = ChooseStartingSite(requestedSite, out string? notice);
        _store = FleetStore.Open(_site.DatabasePath, _trace);
        _store.Info(EventCategory.App, null, $"{BuildInfo.Describe()} - site '{_site.Name}'.");
        Remember(_site);

        ViewModel = Build();
        if (notice is not null)
        {
            ViewModel.ReportStatus(notice);
        }
    }

    /// <summary>Raised on the UI thread after a site switch has replaced <see cref="ViewModel"/>.</summary>
    public event EventHandler? ViewModelChanged;

    public MainViewModel ViewModel { get; private set; }

    public Site Current => _site;

    /// <summary>
    /// Starts the background loops and the update check. Not in the constructor: the window should be
    /// up before anything that can take seconds.
    /// </summary>
    public async Task StartAsync()
    {
        StartLoops(ViewModel);

        AppSettings settings = AppSettings.Load(AppPaths.SettingsFile, _trace);
        UpdateResult result = await UpdateCheck.RunAsync(settings, BuildInfo.Version, trace: _trace).ConfigureAwait(true);
        ViewModel.UpdateStatus = result.IsUpdateAvailable ? result.StatusText : null;
    }

    public IReadOnlyList<Site> Sites() => _catalog.List();

    public string? Switch(string key)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (string.Equals(key, _site.Key, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        if (_catalog.Find(key) is not { } next)
        {
            return $"There is no site '{key}' any more. Its folder may have been moved or deleted: {AppPaths.Sites}";
        }

        // Open the new database before letting go of the old one, so a site that will not open
        // leaves the person exactly where they were rather than with nothing open.
        FleetStore store;
        try
        {
            store = FleetStore.Open(next.DatabasePath, _trace);
        }
        catch (PersistenceException ex)
        {
            _trace.Error($"Site '{next.Name}' could not be opened.", ex);
            return $"Site '{next.Name}' could not be opened: {ex.Message}"
                + (ex.Remediation is null ? string.Empty : Environment.NewLine + Environment.NewLine + ex.Remediation);
        }

        _store.Info(EventCategory.App, null, $"Switched to site '{next.Name}'.");
        string updateStatus = ViewModel.UpdateStatus ?? string.Empty;
        ViewModel.Dispose();
        _store.Dispose();

        _site = next;
        _store = store;
        _store.Info(EventCategory.App, null, $"{BuildInfo.Describe()} - site '{_site.Name}' opened.");
        Remember(_site);
        _trace.Info($"Site '{_site.Name}' opened from {_site.Folder}.");

        ViewModel = Build();
        ViewModel.UpdateStatus = updateStatus.Length == 0 ? null : updateStatus;
        StartLoops(ViewModel);
        ViewModelChanged?.Invoke(this, EventArgs.Empty);
        return null;
    }

    public Site SaveCurrent(SiteSettings settings)
    {
        _site = _catalog.Save(_site, settings);
        return _site;
    }

    public Site Create(SiteSettings settings) => _catalog.Create(settings);

    public SiteImportResult Import(string path)
    {
        SiteFile file = SiteCatalog.ReadExport(path);
        Site site = _catalog.Import(file);
        var skipped = new List<string>();
        int added = 0;

        using (FleetStore store = FleetStore.Open(site.DatabasePath, _trace))
        {
            var taken = new List<Robot>();
            foreach (SiteRobot entry in file.Robots)
            {
                if (!entry.TryBuild(out Robot? robot, out string? problem))
                {
                    skipped.Add(problem!);
                    continue;
                }

                // The same rule as the robot dialog: two rows for one controller would mean two sessions on it.
                if (taken.Any(r => r.Address.Equals(robot!.Address) && r.FtpPort == robot.FtpPort))
                {
                    skipped.Add($"'{robot!.Name}': {robot.Address} is already in the list.");
                    continue;
                }

                try
                {
                    taken.Add(store.Save(robot!));
                    added++;
                }
                catch (RobControlException ex)
                {
                    skipped.Add($"'{robot!.Name}': {ex.Message}");
                }
            }

            store.Info(EventCategory.App, null, $"Site '{site.Name}' imported from {path}: {added} robot(s) added, {skipped.Count} skipped.");
        }

        _trace.Info($"Imported site '{site.Name}' from {path}: {added} added, {skipped.Count} skipped.");
        return new SiteImportResult(site, added, skipped);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _trace.Info("Releasing the fleet database.");
        ViewModel.Dispose();
        _store.Dispose();
    }

    private MainViewModel Build() => new(_dispatcher, _store, _site, this, BuildInfo.Stamp(), _trace);

    private static void StartLoops(MainViewModel viewModel)
    {
        // Each loop ends when its view model is disposed, so a switch never leaves the old site's
        // schedule running against a closed database.
        _ = Task.Run(viewModel.RunScheduleAsync);
        _ = Task.Run(viewModel.RunChartRefreshAsync);
        _ = Task.Run(viewModel.PruneTrends);
    }

    private void Remember(Site site)
    {
        UserSettings user = UserSettings.Load(_trace);
        if (!string.Equals(user.Site, site.Key, StringComparison.Ordinal))
        {
            (user with { Site = site.Key }).Save(_trace);
        }
    }

    /// <summary>
    /// The site to open: the one asked for on the command line, else the one used last, else the
    /// first. On the first run there is none, and one is made - out of 0.2.0's robot list if there
    /// is one, so nobody's robots disappear on upgrade.
    /// </summary>
    private Site ChooseStartingSite(string? requested, out string? notice)
    {
        notice = null;
        UserSettings user = UserSettings.Load(_trace);

        if (_catalog.List().Count == 0)
        {
            Site first = File.Exists(AppPaths.LegacyDatabase)
                ? _catalog.Adopt(AppPaths.LegacyDatabase, user.ToFirstSite(FirstSiteName, AppPaths.ArchiveBase))
                : _catalog.Create(new SiteSettings { Name = FirstSiteName });

            user.WithoutLegacy().Save(_trace);
            notice = $"Robots now belong to a site - this one is '{first.Name}'. Rename it, or add a site per plant, from the Site menu.";
            return first;
        }

        if (requested is not null)
        {
            if (_catalog.Find(requested) is { } asked)
            {
                return asked;
            }

            notice = $"There is no site called '{requested}'.";
        }

        Site chosen = _catalog.Find(user.Site) ?? _catalog.List()[0];
        if (notice is not null)
        {
            notice += $" Opened '{chosen.Name}' instead.";
        }

        return chosen;
    }
}
