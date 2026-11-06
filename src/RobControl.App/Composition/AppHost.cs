// UseWPF drops System.IO from the implicit usings.
using System.Globalization;
using System.IO;
using RobControl.App.Diagnostics;
using RobControl.App.ViewModels;
using RobControl.Core;
using RobControl.Core.Diagnostics;
using RobControl.Core.Events;
using RobControl.Core.Persistence;
using RobControl.Core.Robots;
using RobControl.Core.Sites;
using RobControl.Core.Systems;
using RobControl.Core.Transports.Ftp;

namespace RobControl.App.Composition;

/// <summary>
/// Builds the object graph and owns everything with a lifetime. No container: the graph is a
/// handful of objects, and what talks to what should be readable off one page.
///
/// <para><b>One site and one system open at a time.</b> The open site owns the fleet database; the
/// open system decides which of its robots the view model shows and how they are backed up. Switching
/// system disposes the view model - which stops any trend recording through the recorder's own stop
/// path, so the event log still says it stopped - and builds it again; switching site also swaps the
/// database. <see cref="ViewModelChanged"/> tells the window to rebind.</para>
///
/// <para><b>Nothing is open until <see cref="Open"/>.</b> The constructor only finds the sites (and
/// makes the first one on a new PC), so the picker can run before any database is touched.</para>
/// </summary>
public sealed class AppHost : IDisposable, ISiteHost
{
    /// <summary>What the first site is called, on a new PC or when 0.2.0's robot list is moved into one. Renamable.</summary>
    public const string FirstSiteName = "My site";

    private readonly IUiDispatcher _dispatcher;
    private readonly ITraceLog _trace;
    private readonly SiteCatalog _catalog;
    private readonly SystemCatalog _systems;
    private FleetStore? _store;
    private Site? _site;
    private RobotSystem? _system;
    private MainViewModel? _viewModel;
    private string? _firstRunNotice;
    private bool _disposed;

    public AppHost(IUiDispatcher dispatcher, ITraceLog? trace = null)
    {
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        _trace = trace ?? NullTraceLog.Instance;
        _catalog = new SiteCatalog(AppPaths.Sites, AppPaths.ArchiveBase, _trace, AppPaths.Systems);
        _systems = new SystemCatalog(AppPaths.Systems, _trace);
        EnsureFirstSite();
    }

    /// <summary>Raised on the UI thread after a switch has replaced <see cref="ViewModel"/>.</summary>
    public event EventHandler? ViewModelChanged;

    /// <summary>The open system's view model. Only valid after <see cref="Open"/> has succeeded.</summary>
    public MainViewModel ViewModel => _viewModel ?? throw new InvalidOperationException("Nothing is open yet.");

    public bool IsOpen => _viewModel is not null;

    public Site Current => _site ?? throw new InvalidOperationException("No site is open.");

    public RobotSystem CurrentSystem => _system ?? throw new InvalidOperationException("No system is open.");

    public string SystemsFolder => _systems.Root;

    /// <summary>
    /// What to open with no picker: <c>--site</c> and <c>--system</c> if given, else the system used
    /// last, else the first site's first system. A name that is not found is said in <paramref name="notice"/>.
    /// </summary>
    public SystemChoice DefaultChoice(string? requestedSite, string? requestedSystem, out string? notice)
    {
        notice = null;
        UserSettings user = UserSettings.Load(_trace);
        Site? site = null;

        if (requestedSite is not null)
        {
            site = _catalog.Find(requestedSite);
            if (site is null)
            {
                notice = $"There is no site called '{requestedSite}'.";
            }
        }

        site ??= _catalog.Find(user.Site) ?? _catalog.List().FirstOrDefault();
        if (site is null)
        {
            throw new InvalidOperationException("There is no site to open.");
        }

        string? system = null;
        if (requestedSystem is not null)
        {
            system = _systems.Find(site.Key, requestedSystem)?.Key;
            if (system is null)
            {
                notice = (notice is null ? string.Empty : notice + " ") + $"Site '{site.Name}' has no system called '{requestedSystem}'.";
            }
        }
        else if (string.Equals(site.Key, user.Site, StringComparison.OrdinalIgnoreCase))
        {
            system = _systems.Find(site.Key, user.SystemKey)?.Key;
        }

        return new SystemChoice(site.Key, system);
    }

    /// <summary>
    /// Starts the background loops and the update check. Not in <see cref="Open"/>: the window should be
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

    public IReadOnlyList<RobotSystem> Systems() => _site is null ? [] : _systems.List(_site.Key);

    public IReadOnlyList<SitePick> Directory()
    {
        var picks = new List<SitePick>();
        foreach (Site site in _catalog.List())
        {
            picks.Add(new SitePick(site.Key, site.Name, site.Settings.Notes, [.. _systems.List(site.Key)
                .Select(s => new SystemPick(site.Key, s.Key, s.Name, s.Settings.Robots.Count, s.Settings.Notes, s.Folder))]));
        }

        return picks;
    }

    public string? Switch(string key) => Open(new SystemChoice(key, null));

    /// <summary>
    /// Opens <paramref name="choice"/>: the site's database (unless it is the one already open), its
    /// systems (making "Main" if it has none), and a view model on the chosen system. Anything that
    /// fails leaves what was open exactly as it was.
    /// </summary>
    public string? Open(SystemChoice choice)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(choice);

        if (_catalog.Find(choice.SiteKey) is not { } site)
        {
            return $"There is no site '{choice.SiteKey}' any more. Its folder may have been moved or deleted: {AppPaths.Sites}";
        }

        bool sameSite = _site is not null && _store is not null && string.Equals(site.Key, _site.Key, StringComparison.OrdinalIgnoreCase);
        if (sameSite && _system is not null && choice.SystemKey is not null
            && string.Equals(choice.SystemKey, _system.Key, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        // Open the new database before letting go of the old one, so a site that will not open
        // leaves the person exactly where they were rather than with nothing open.
        FleetStore store;
        if (sameSite)
        {
            store = _store!;
        }
        else
        {
            try
            {
                store = FleetStore.Open(site.DatabasePath, _trace);
            }
            catch (PersistenceException ex)
            {
                _trace.Error($"Site '{site.Name}' could not be opened.", ex);
                return $"Site '{site.Name}' could not be opened: {ex.Message}"
                    + (ex.Remediation is null ? string.Empty : Environment.NewLine + Environment.NewLine + ex.Remediation);
            }
        }

        RobotSystem system;
        string? adopted;
        try
        {
            IReadOnlyList<RobotSystem> systems = _systems.EnsureAny(site.Key, store.Robots());
            system = (choice.SystemKey is null ? null : _systems.Find(site.Key, choice.SystemKey)) ?? systems[0];
            adopted = Adopt(store, site, system);
        }
        catch (Exception ex) when (ex is RobControlException or IOException or UnauthorizedAccessException)
        {
            if (!sameSite)
            {
                store.Dispose();
            }

            _trace.Error($"The systems of site '{site.Name}' could not be read.", ex);
            return $"The systems of site '{site.Name}' could not be read: {ex.Message}";
        }

        string updateStatus = _viewModel?.UpdateStatus ?? string.Empty;
        bool wasOpen = _viewModel is not null;
        if (_viewModel is not null)
        {
            _store?.Info(EventCategory.App, null, sameSite
                ? $"Switched to system '{system.Name}'."
                : $"Switched to site '{site.Name}', system '{system.Name}'.");
            _viewModel.Dispose();
        }

        if (!sameSite)
        {
            _store?.Dispose();
            _store = store;
            _store.Info(EventCategory.App, null, $"{BuildInfo.Describe()} - site '{site.Name}' opened.");
            _trace.Info($"Site '{site.Name}' opened from {site.Folder}; settings in {site.ConfigFolder}.");
        }

        _site = site;
        _system = system;
        Remember(site, system);

        _viewModel = new MainViewModel(_dispatcher, _store!, _site, _system, this, BuildInfo.Stamp(), _trace);
        _viewModel.UpdateStatus = updateStatus.Length == 0 ? null : updateStatus;
        string? notice = _firstRunNotice ?? adopted;
        _firstRunNotice = null;
        if (notice is not null)
        {
            _viewModel.ReportStatus(notice);
        }

        if (wasOpen)
        {
            StartLoops(_viewModel);
            ViewModelChanged?.Invoke(this, EventArgs.Empty);
        }

        return null;
    }

    public RobotSystem CreateSystem(string siteKey, SystemSettings settings) => _systems.Create(siteKey, settings);

    public RobotSystem SaveSystem(SystemSettings settings)
    {
        _system = _systems.Save(CurrentSystem, settings);
        return _system;
    }

    public RobotSystem MoveRobot(Robot robot, string systemKey)
    {
        RobotSystem to = _systems.Find(Current.Key, systemKey)
            ?? throw new RobotSystemException($"Site '{Current.Name}' has no system '{systemKey}' any more.");
        RobotSystem moved = _systems.Move(robot, to);
        if (_system is { IsUnassigned: false } && _system.Key != moved.Key)
        {
            _system = _systems.Find(Current.Key, _system.Key) ?? _system;
        }

        return moved;
    }

    public void ForgetRobot(Robot robot)
    {
        foreach (RobotSystem system in _systems.List(Current.Key).Where(s => s.Settings.Contains(robot)))
        {
            RobotSystem saved = _systems.Save(system, system.Settings.Without(robot));
            if (_system is not null && saved.Key == _system.Key)
            {
                _system = saved;
            }
        }
    }

    public Site SaveCurrent(SiteSettings settings)
    {
        _site = _catalog.Save(Current, settings);
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

    public SiteBundleImportResult ImportBundle(string path, IProgress<string>? progress = null, CancellationToken cancellationToken = default) =>
        SiteBundleImport.Import(_catalog, path, _trace, progress, cancellationToken);

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _trace.Info("Releasing the fleet database.");
        _viewModel?.Dispose();
        _store?.Dispose();
    }

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
    /// On a new PC there is no site, and one is made - out of 0.2.0's robot list if there is one, so
    /// nobody's robots disappear on upgrade.
    /// </summary>
    private void EnsureFirstSite()
    {
        if (_catalog.List().Count > 0)
        {
            return;
        }

        UserSettings user = UserSettings.Load(_trace);
        Site first = File.Exists(AppPaths.LegacyDatabase)
            ? _catalog.Adopt(AppPaths.LegacyDatabase, user.ToFirstSite(FirstSiteName, AppPaths.ArchiveBase))
            : _catalog.Create(new SiteSettings { Name = FirstSiteName });

        user.WithoutLegacy().Save(_trace);
        _firstRunNotice = $"Robots now belong to a site and a system - this is '{first.Name}', system '{SystemCatalog.DefaultName}'. "
            + "Rename them, or add more, from the System menu.";
    }

    /// <summary>
    /// Robots the system's file lists that the site's database does not have yet - typed into
    /// system.json, or a system folder copied from another laptop - are added, with the site's FTP
    /// login. A sentence for the status bar when anything was added or refused.
    /// </summary>
    private string? Adopt(FleetStore store, Site site, RobotSystem system)
    {
        var login = new FtpCredentials(site.Settings.DefaultFtpUser, site.Settings.DefaultFtpPassword);
        IReadOnlyList<Robot> found = SystemMembership.Adoptable(system, store.Robots(), login, out IReadOnlyList<string> problems);
        var refused = new List<string>(problems);
        int added = 0;
        foreach (Robot robot in found)
        {
            try
            {
                Robot saved = store.Save(robot);
                store.Info(EventCategory.App, saved, $"Robot added from {system.SettingsPath}.");
                added++;
            }
            catch (RobControlException ex)
            {
                refused.Add($"'{robot.Name}': {ex.Message}");
            }
        }

        foreach (string problem in refused)
        {
            _trace.Warn($"{system.SettingsPath}: {problem}");
        }

        if (added == 0 && refused.Count == 0)
        {
            return null;
        }

        string text = string.Create(CultureInfo.CurrentCulture, $"{added} robot(s) added from {system.Name}'s system.json.");
        return refused.Count == 0 ? text : text + $" {refused.Count} entry(ies) could not be added: " + string.Join(" ", refused);
    }

    private void Remember(Site site, RobotSystem system)
    {
        UserSettings user = UserSettings.Load(_trace);
        string? key = system.IsUnassigned ? user.SystemKey : system.Key;
        if (!string.Equals(user.Site, site.Key, StringComparison.Ordinal) || !string.Equals(user.SystemKey, key, StringComparison.Ordinal))
        {
            (user with { Site = site.Key, SystemKey = key }).Save(_trace);
        }
    }
}
