using System.Collections.ObjectModel;
using System.Globalization;
// UseWPF drops System.IO from the implicit usings; IOException and Directory are used below.
using System.IO;
using System.Net;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RobControl.App.Composition;
using RobControl.Core;
using RobControl.Core.Backup;
using RobControl.Core.Controllers;
using RobControl.Core.Diagnostics;
using RobControl.Core.Events;
using RobControl.Core.Persistence;
using RobControl.Core.Robots;
using RobControl.Core.Sites;
using RobControl.Core.Trending;

namespace RobControl.App.ViewModels;

/// <summary>
/// The whole window's logic. Free of WPF types on purpose (as in NetControl): anything that needs
/// a dialog asks for it through a delegate the window sets, and anything from a worker thread comes
/// back through <see cref="IUiDispatcher"/>.
///
/// <para><b>What can run at once.</b> One operation per robot (a probe or a backup), and any number
/// of robots up to <see cref="SiteSettings.Concurrency"/> for a fleet backup. A robot that is busy
/// is skipped rather than queued twice - two sessions on one controller is what the Safety section
/// rules out.</para>
///
/// <para><b>One site.</b> A view model is built on one site's database and lives as long as that site
/// is open. Switching site is asked of <see cref="ISiteHost"/>, which disposes this one and builds
/// another - nothing in here ever holds two sites' robots at once.</para>
/// </summary>
public sealed class MainViewModel : ObservableObject, IDisposable
{
    private const int MaxEventRows = 500;

    private readonly IUiDispatcher _ui;
    private readonly FleetStore _store;
    private readonly ISiteHost _sites;
    private readonly ITraceLog _trace;
    private readonly string _tool;
    private readonly CancellationTokenSource _shutdown = new();
    private Site _site;
    private SiteSettings _settings;
    private BackupArchive _archive;
    private CancellationTokenSource? _operation;
    private RobotRowViewModel? _selected;
    private CompareViewModel? _compare;
    private string _status = "Ready.";
    private string? _updateStatus;
    private int _running;
    private DateTimeOffset? _nextScheduled;
    private bool _eventsForSelectedOnly;
    private readonly TrendRecorder _recorder;
    private IReadOnlyList<RobotRowViewModel> _selectedRobots = [];

    public MainViewModel(IUiDispatcher ui, FleetStore store, Site site, ISiteHost sites, string tool, ITraceLog? trace = null, TrendRecorder? recorder = null)
    {
        _ui = ui ?? throw new ArgumentNullException(nameof(ui));
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _site = site ?? throw new ArgumentNullException(nameof(site));
        _sites = sites ?? throw new ArgumentNullException(nameof(sites));
        _settings = site.Settings;
        _tool = tool;
        _trace = trace ?? NullTraceLog.Instance;
        _archive = new BackupArchive(_settings.ArchiveRoot);
        _recorder = recorder ?? new TrendRecorder(store, store);
        Trend = new TrendViewModel(ui, store, _recorder, () => Targets);
        Search = new SearchViewModel(Snapshot);
        Alarms = new AlarmsViewModel(Snapshot);
        Fleet = new FleetViewModel(Snapshot);

        AddRobotCommand = new RelayCommand(AddRobot);
        EditRobotCommand = new RelayCommand(EditRobot, () => Selected is { IsBusy: false });
        RemoveRobotCommand = new RelayCommand(RemoveRobot, () => Selected is { IsBusy: false });
        ProbeSelectedCommand = new AsyncRelayCommand(ProbeSelectedAsync, () => Targets.Any(r => !r.IsBusy));
        ProbeAllCommand = new AsyncRelayCommand(ProbeAllAsync, () => Robots.Count > 0 && !IsBusy);
        BackupSelectedCommand = new AsyncRelayCommand(BackupSelectedAsync, () => Targets.Any(r => !r.IsBusy));
        BackupAllCommand = new AsyncRelayCommand(BackupAllAsync, () => Robots.Count > 0 && !IsBusy);
        StopCommand = new RelayCommand(Stop, () => IsBusy);
        CompareCommand = new RelayCommand(CompareChecked, () => Selected is { Backups.Count: >= 2 });
        RefreshCommand = new RelayCommand(RefreshAll);
        DiagnoseNetworkCommand = new RelayCommand(DiagnoseNetwork, () => Selected is not null);
        OpenBackupFolderCommand = new RelayCommand<BackupRowViewModel>(row => OpenFolder?.Invoke(row?.Folder ?? RobotFolderOrRoot()));
        OpenArchiveCommand = new RelayCommand(() => OpenFolder?.Invoke(RobotFolderOrRoot()));
        ChooseArchiveCommand = new RelayCommand(ChooseArchive);
        SetScheduleCommand = new RelayCommand<string>(SetSchedule);
        NewSiteCommand = new RelayCommand(NewSite);
        EditSiteCommand = new RelayCommand(EditSite);
        ImportSiteCommand = new RelayCommand(ImportSite);
        ExportSiteCommand = new RelayCommand(ExportSite);
        OpenSiteFolderCommand = new RelayCommand(() => OpenFolder?.Invoke(_site.Folder));

        _store.EventRecorded += OnEventRecorded;
        LoadRobots();
        ReloadEvents();
        Trend.Reload();
        ReloadSites();
    }

    // ------------------------------------------------------------------ the view's delegates

    /// <summary>Shows the robot dialog. A draft with no Id means "add"; null out means cancelled.</summary>
    public Func<RobotDraft, RobotDraft?>? EditRobotDialog { get; set; }

    /// <summary>Shows the site dialog. The flag is true for a new site; null out means cancelled.</summary>
    public Func<SiteDraft, bool, SiteDraft?>? EditSiteDialog { get; set; }

    /// <summary>A file picker for a site file to import. Null when cancelled.</summary>
    public Func<string?>? PickSiteFileToOpen { get; set; }

    /// <summary>A save-as picker for a site export, with a suggested file name. Null when cancelled.</summary>
    public Func<string, string?>? PickSiteFileToSave { get; set; }

    /// <summary>A yes/no question, defaulting to No.</summary>
    public Func<string, bool>? Confirm { get; set; }

    public Action<string>? ShowMessage { get; set; }

    /// <summary>A folder picker, starting at the given folder. Null when cancelled.</summary>
    public Func<string, string?>? PickFolder { get; set; }

    public Action<string>? OpenFolder { get; set; }

    /// <summary>Raised when a comparison has been built, so the view can bring its tab forward.</summary>
    public event EventHandler? CompareOpened;

    // ------------------------------------------------------------------ state

    public ObservableCollection<RobotRowViewModel> Robots { get; } = [];

    public ObservableCollection<EventRowViewModel> Events { get; } = [];

    public RobotRowViewModel? Selected
    {
        get => _selected;
        set
        {
            if (SetProperty(ref _selected, value))
            {
                if (value is not null)
                {
                    RefreshBackups(value);
                }

                if (EventsForSelectedOnly)
                {
                    ReloadEvents();
                }

                if (_selectedRobots.Count == 0)
                {
                    Trend.Reload();
                }

                OnPropertyChanged(nameof(TargetText));

                RaiseCanExecute();
            }
        }
    }

    /// <summary>
    /// The robots the toolbar acts on: every row selected in the list (Ctrl/Shift-click), or the one
    /// in focus when the view has not said otherwise.
    /// </summary>
    public IReadOnlyList<RobotRowViewModel> Targets =>
        _selectedRobots.Count > 0 ? _selectedRobots : Selected is { } one ? [one] : [];

    public string TargetText => Targets.Count switch
    {
        0 => "No robot selected",
        1 => Targets[0].Name,
        _ => string.Create(CultureInfo.CurrentCulture, $"{Targets.Count} robots selected"),
    };

    public TrendViewModel Trend { get; }

    /// <summary>The Search tab - across this site's backups.</summary>
    public SearchViewModel Search { get; }

    /// <summary>The Alarms tab - alarm history from this site's backups.</summary>
    public AlarmsViewModel Alarms { get; }

    /// <summary>The Fleet tab - inventory, watched setting changes, the site report.</summary>
    public FleetViewModel Fleet { get; }

    /// <summary>The open site as the fleet-wide tabs read it, at this moment.</summary>
    public FleetContext Snapshot() => new(
        _site.Name,
        _settings.Notes,
        _archive,
        [.. Robots.Select(r => (r.Robot, r.Identity))],
        _tool);

    /// <summary>Called by the view when the list's selection changes - a DataGrid's SelectedItems cannot be bound.</summary>
    public void SetSelection(IEnumerable<RobotRowViewModel> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);
        _selectedRobots = [.. rows];
        OnPropertyChanged(nameof(Targets));
        OnPropertyChanged(nameof(TargetText));
        Trend.Reload();
        RaiseCanExecute();
    }

    public CompareViewModel? Compare
    {
        get => _compare;
        private set => SetProperty(ref _compare, value);
    }

    public string Status
    {
        get => _status;
        private set => SetProperty(ref _status, value);
    }

    /// <summary>Only set when a newer build exists. See AppHost.</summary>
    public string? UpdateStatus
    {
        get => _updateStatus;
        set => SetProperty(ref _updateStatus, value);
    }

    public bool IsBusy => _running > 0;

    /// <summary>The open site's name: in the title bar and the status bar, so nobody backs up the wrong plant.</summary>
    public string SiteName => _site.Name;

    public string WindowTitle => $"RobControl - {_site.Name}";

    /// <summary>Every site on this PC, for the Site > Switch to menu.</summary>
    public ObservableCollection<SiteChoiceViewModel> SiteChoices { get; } = [];

    public string ArchiveRoot => _archive.Root;

    public int ScheduleHours => _settings.ScheduleHours;

    public string ScheduleText => _settings.ScheduleHours == 0
        ? "Scheduled backups off"
        : _nextScheduled is { } next
            ? string.Create(CultureInfo.CurrentCulture, $"Every {_settings.ScheduleHours} h - next {next.ToLocalTime():ddd HH:mm}")
            : string.Create(CultureInfo.CurrentCulture, $"Every {_settings.ScheduleHours} h");

    public bool IncludeFrom
    {
        get => _settings.IncludeFrom;
        set
        {
            if (_settings.IncludeFrom != value)
            {
                _settings = _settings with { IncludeFrom = value };
                SaveSettings();
                OnPropertyChanged();
            }
        }
    }

    public bool EventsForSelectedOnly
    {
        get => _eventsForSelectedOnly;
        set
        {
            if (SetProperty(ref _eventsForSelectedOnly, value))
            {
                ReloadEvents();
            }
        }
    }

    public string Version => _tool;

    // ------------------------------------------------------------------ commands

    public IRelayCommand AddRobotCommand { get; }

    public IRelayCommand EditRobotCommand { get; }

    public IRelayCommand RemoveRobotCommand { get; }

    public IAsyncRelayCommand ProbeSelectedCommand { get; }

    public IAsyncRelayCommand ProbeAllCommand { get; }

    public IAsyncRelayCommand BackupSelectedCommand { get; }

    public IAsyncRelayCommand BackupAllCommand { get; }

    public IRelayCommand StopCommand { get; }

    public IRelayCommand CompareCommand { get; }

    public IRelayCommand RefreshCommand { get; }

    public IRelayCommand DiagnoseNetworkCommand { get; }

    public IRelayCommand<BackupRowViewModel> OpenBackupFolderCommand { get; }

    public IRelayCommand OpenArchiveCommand { get; }

    public IRelayCommand ChooseArchiveCommand { get; }

    public IRelayCommand<string> SetScheduleCommand { get; }

    public IRelayCommand NewSiteCommand { get; }

    public IRelayCommand EditSiteCommand { get; }

    public IRelayCommand ImportSiteCommand { get; }

    public IRelayCommand ExportSiteCommand { get; }

    public IRelayCommand OpenSiteFolderCommand { get; }

    /// <summary>Puts a sentence in the status bar - for things the host has to say at startup.</summary>
    public void ReportStatus(string message) => Status = message;

    /// <summary>Selects the robot at <paramref name="address"/> - for <c>RobControl.exe --robot 10.20.1.54</c>.</summary>
    public bool SelectByAddress(string address)
    {
        if (!IPAddress.TryParse(address, out IPAddress? ip))
        {
            return false;
        }

        RobotRowViewModel? row = Robots.FirstOrDefault(r => r.Robot.Address.Equals(ip));
        if (row is not null)
        {
            Selected = row;
        }

        return row is not null;
    }

    /// <summary>
    /// The schedule loop: checks once a minute whether a fleet backup is due. Started by AppHost;
    /// stops when the view model is disposed. A due backup while something else runs waits for the
    /// next minute rather than overlapping it.
    /// </summary>
    public async Task RunScheduleAsync()
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));
        PlanNextRun();
        try
        {
            while (await timer.WaitForNextTickAsync(_shutdown.Token).ConfigureAwait(false))
            {
                PruneTrends();

                if (_settings.ScheduleHours > 0 && _nextScheduled is { } due && DateTimeOffset.UtcNow >= due)
                {
                    _ui.Post(() =>
                    {
                        if (IsBusy || Robots.Count == 0)
                        {
                            return;
                        }

                        _store.Info(EventCategory.Backup, null, "Scheduled fleet backup starting.");
                        PlanNextRun();
                        _ = BackupAllCommand.ExecuteAsync(null);
                    });
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    /// <summary>
    /// Redraws the chart every few seconds while anything is recording. Started by AppHost beside the
    /// schedule loop.
    /// </summary>
    public async Task RunChartRefreshAsync()
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(3));
        try
        {
            while (await timer.WaitForNextTickAsync(_shutdown.Token).ConfigureAwait(false))
            {
                if (_recorder.Recording.Count > 0)
                {
                    _ui.Post(Trend.RefreshChart);
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    /// <summary>Drops samples older than the retention setting. Cheap; runs with the schedule check.</summary>
    public void PruneTrends()
    {
        try
        {
            int removed = _store.PruneSamples(DateTimeOffset.UtcNow.AddDays(-_settings.TrendRetentionDays));
            if (removed > 0)
            {
                _trace.Info($"Pruned {removed} trend samples older than {_settings.TrendRetentionDays} days.");
            }
        }
        catch (Exception ex) when (ex is RobControlException or InvalidOperationException)
        {
            _trace.Warn("Could not prune trend samples.", ex);
        }
    }

    public void Dispose()
    {
        _store.EventRecorded -= OnEventRecorded;
        Search.Cancel();
        Alarms.Cancel();
        Fleet.Cancel();
        Trend.Dispose();
        _recorder.StopAllAsync().GetAwaiter().GetResult();
        _shutdown.Cancel();
        _operation?.Cancel();
        _shutdown.Dispose();
    }

    // ------------------------------------------------------------------ robots

    private void LoadRobots()
    {
        Robots.Clear();
        foreach (Robot robot in _store.Robots())
        {
            var row = new RobotRowViewModel(robot);
            (ControllerIdentity? identity, DateTimeOffset? utc, string? summary) = _store.LastProbe(robot);
            row.ApplyProbe(identity, utc, summary);
            Robots.Add(row);
            RefreshBackups(row);
        }

        Selected ??= Robots.FirstOrDefault();
        RaiseCanExecute();
    }

    private void AddRobot()
    {
        // A new robot starts with the site's FTP login: one plant tends to use one login everywhere.
        RobotDraft? draft = EditRobotDialog?.Invoke(new RobotDraft
        {
            FtpUser = _settings.DefaultFtpUser,
            FtpPassword = _settings.DefaultFtpPassword,
        });
        if (draft is null || !TrySave(draft, out Robot? saved))
        {
            return;
        }

        var row = new RobotRowViewModel(saved!);
        Robots.Add(row);
        Selected = row;
        _store.Info(EventCategory.App, saved, "Robot added.");
        Status = $"Added {saved!.Describe()}. Probe it to see what it is.";
        RaiseCanExecute();
    }

    private void EditRobot()
    {
        if (Selected is not { } row)
        {
            return;
        }

        RobotDraft? draft = EditRobotDialog?.Invoke(RobotDraft.From(row.Robot));
        if (draft is null || !TrySave(draft, out Robot? saved))
        {
            return;
        }

        bool renamed = !string.Equals(row.Robot.Name, saved!.Name, StringComparison.Ordinal);
        row.Robot = saved;
        _store.Info(EventCategory.App, saved, "Robot details changed.");
        if (renamed)
        {
            // The archive folder is named after the robot. Old backups stay where they are, under
            // the old name, and say so - moving folders behind somebody's back is worse.
            ShowMessage?.Invoke($"Backups taken under the old name stay in their old folder. New backups go to {_archive.RobotFolder(saved)}.");
        }

        RefreshBackups(row);
    }

    private bool TrySave(RobotDraft draft, out Robot? saved)
    {
        saved = null;
        if (!draft.TryBuild(out Robot? robot, out string? problem))
        {
            ShowMessage?.Invoke(problem ?? "The robot details are not valid.");
            return false;
        }

        if (Robots.Any(r => r.Robot.Id != robot!.Id && r.Robot.Address.Equals(robot.Address) && r.Robot.FtpPort == robot.FtpPort))
        {
            ShowMessage?.Invoke($"{robot!.Address} is already in the list. Two rows for one controller would mean two sessions on it.");
            return false;
        }

        try
        {
            saved = _store.Save(robot!);
            return true;
        }
        catch (RobControlException ex)
        {
            ShowMessage?.Invoke(ex.Message + (ex.Remediation is null ? string.Empty : Environment.NewLine + Environment.NewLine + ex.Remediation));
            return false;
        }
    }

    private void RemoveRobot()
    {
        if (Selected is not { } row)
        {
            return;
        }

        if (Confirm?.Invoke($"Remove {row.Robot.Describe()} from the list?\n\nIts backups stay on disk in {_archive.RobotFolder(row.Robot)}, and its rows stay in the event log. Its trend signals and recorded samples are deleted.") != true)
        {
            return;
        }

        _recorder.StopAsync(row.Robot.Id).GetAwaiter().GetResult();

        _store.Remove(row.Robot);
        _store.Info(EventCategory.App, row.Robot, "Robot removed from the list. Backups left in place.");
        Robots.Remove(row);
        Selected = Robots.FirstOrDefault();
        RaiseCanExecute();
    }

    // ------------------------------------------------------------------ probe

    private Task ProbeSelectedAsync() => ProbeAsync(Targets);

    private Task ProbeAllAsync() => ProbeAsync([.. Robots]);

    private async Task ProbeAsync(IReadOnlyList<RobotRowViewModel> rows)
    {
        CancellationToken token = Begin();
        var probe = new CapabilityProbe(_store);
        int done = 0;
        try
        {
            // Probes are short and read-only; still at most Concurrency at once, one per robot.
            using var gate = new SemaphoreSlim(_settings.Concurrency);
            await Task.WhenAll(rows.Where(r => !r.IsBusy).Select(async row =>
            {
                await gate.WaitAsync(token).ConfigureAwait(true);
                try
                {
                    row.IsBusy = true;
                    row.Activity = "Probing...";
                    ProbeReport report = await Task.Run(() => probe.RunAsync(row.Robot, token), token).ConfigureAwait(true);
                    _store.SaveProbe(report);
                    row.ApplyProbe(report.Identity, report.Utc, report.Summary, report.Steps);
                    Status = string.Create(CultureInfo.CurrentCulture, $"Probed {++done} of {rows.Count}: {row.Name} - {report.Summary}");
                }
                finally
                {
                    row.IsBusy = false;
                    row.Activity = null;
                    gate.Release();
                }
            })).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            Status = "Probe stopped.";
        }
        finally
        {
            End();
        }
    }

    // ------------------------------------------------------------------ backup

    private Task BackupSelectedAsync() => BackupAsync(Targets);

    private Task BackupAllAsync() => BackupAsync([.. Robots]);

    private async Task BackupAsync(IReadOnlyList<RobotRowViewModel> rows)
    {
        List<RobotRowViewModel> todo = [.. rows.Where(r => !r.IsBusy)];
        if (todo.Count == 0)
        {
            return;
        }

        CancellationToken token = Begin();
        var runner = new BackupRunner(_archive, _tool, _store);
        var options = new BackupOptions { Devices = _settings.IncludeFrom ? ["md:", "fr:"] : ["md:"] };
        Dictionary<Robot, RobotRowViewModel> byRobot = todo.ToDictionary(r => r.Robot);
        int finished = 0;
        int complete = 0;

        foreach (RobotRowViewModel row in todo)
        {
            row.IsBusy = true;
            row.Progress = 0;
            row.Activity = "Waiting...";
        }

        RaiseCanExecute();
        Status = string.Create(CultureInfo.CurrentCulture, $"Backing up {todo.Count} robot(s) into {_archive.Root}...");

        var progress = new Progress<BackupProgress>(p =>
        {
            RobotRowViewModel? row = todo.FirstOrDefault(r => r.Name == p.Robot);
            if (row is null)
            {
                return;
            }

            row.Activity = p.FileCount == 0 ? p.Stage : $"{p.Stage} {p.FileIndex}/{p.FileCount} {p.CurrentFile}";
            row.Progress = p.FileCount == 0 ? 0 : 100.0 * p.FileIndex / p.FileCount;
        });

        try
        {
            await Task.Run(() => new FleetBackup(runner).RunAsync(
                todo.Select(r => r.Robot),
                options,
                _settings.Concurrency,
                robot => byRobot[robot].Identity,
                progress,
                (robot, result) => _ui.Post(() =>
                {
                    RobotRowViewModel row = byRobot[robot];
                    row.IsBusy = false;
                    row.Activity = null;
                    row.Progress = 0;
                    RefreshBackups(row);
                    finished++;
                    if (result.Outcome == BackupOutcome.Complete)
                    {
                        complete++;
                    }

                    Status = string.Create(CultureInfo.CurrentCulture,
                        $"{finished} of {todo.Count} done, {complete} complete. Last: {robot.Name} - {result.Manifest.Summary}");
                }),
                token), token).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            Status = "Backups stopped. Anything cut short is marked INCOMPLETE.";
        }
        catch (Exception ex) when (ex is RobControlException or IOException or UnauthorizedAccessException)
        {
            // The archive folder itself, most likely: unwritable, a share gone away, a full disk.
            _trace.Error("Fleet backup stopped.", ex);
            ShowMessage?.Invoke($"Backups stopped: {ex.Message}\n\nArchive folder: {_archive.Root}");
        }
        finally
        {
            foreach (RobotRowViewModel row in todo.Where(r => r.IsBusy))
            {
                row.IsBusy = false;
                row.Activity = null;
                row.Progress = 0;
                RefreshBackups(row);
            }

            End();
        }
    }

    private void Stop()
    {
        _operation?.Cancel();
        Status = "Stopping after the current file...";
    }

    private void RefreshBackups(RobotRowViewModel row)
    {
        try
        {
            var unreadable = new List<string>();
            row.ApplyBackups(_archive.List(row.Robot, unreadable));
            if (unreadable.Count > 0)
            {
                _trace.Warn($"{unreadable.Count} folder(s) under {_archive.RobotFolder(row.Robot)} have no readable manifest.");
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Status = $"Could not read the archive for {row.Name}: {ex.Message}";
        }

        RaiseCanExecute();
    }

    private void RefreshAll()
    {
        foreach (RobotRowViewModel row in Robots)
        {
            RefreshBackups(row);
        }

        ReloadEvents();
    }

    // ------------------------------------------------------------------ compare

    private void CompareChecked()
    {
        if (Selected is not { } row)
        {
            return;
        }

        List<BackupRowViewModel> ticked = [.. row.Backups.Where(b => b.IsChecked)];
        (BackupRowViewModel a, BackupRowViewModel b) pair;

        if (ticked.Count == 2)
        {
            pair = (ticked[0], ticked[1]);
        }
        else if (ticked.Count == 0 && row.Backups.Count >= 2)
        {
            // Nothing ticked: the newest against the one before it, which is the usual question.
            pair = (row.Backups[1], row.Backups[0]);
        }
        else
        {
            ShowMessage?.Invoke("Tick exactly two backups to compare - or none, to compare the newest with the one before it.");
            return;
        }

        try
        {
            Compare = new CompareViewModel(pair.a.Set, pair.b.Set);
            CompareOpened?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or BackupArchiveException)
        {
            ShowMessage?.Invoke($"Could not compare: {ex.Message}");
        }
    }

    // ------------------------------------------------------------------ settings and handoff

    private void ChooseArchive()
    {
        string? folder = PickFolder?.Invoke(_archive.Root);
        if (folder is null || string.Equals(folder, _archive.Root, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        _settings = _settings with { ArchiveRoot = folder };
        SaveSettings();
        _archive = new BackupArchive(folder);
        _store.Info(EventCategory.App, null, $"Archive folder set to {folder}.");
        OnPropertyChanged(nameof(ArchiveRoot));
        RefreshAll();
    }

    private void SetSchedule(string? hours)
    {
        int value = int.TryParse(hours, NumberStyles.None, CultureInfo.InvariantCulture, out int h) ? Math.Clamp(h, 0, 168) : 0;
        _settings = _settings with { ScheduleHours = value };
        SaveSettings();
        _store.Info(EventCategory.App, null, value == 0 ? "Scheduled backups turned off." : $"Scheduled backups every {value} h while RobControl is open.");
        PlanNextRun();
        OnPropertyChanged(nameof(ScheduleHours));
    }

    private void PlanNextRun()
    {
        _nextScheduled = _settings.ScheduleHours > 0 ? DateTimeOffset.UtcNow.AddHours(_settings.ScheduleHours) : null;
        _ui.Post(() => OnPropertyChanged(nameof(ScheduleText)));
    }

    /// <summary>
    /// Writes the site's settings. Never throws: a setting that did not save is a nuisance, and the
    /// status bar says so rather than a dialog in the middle of a backup.
    /// </summary>
    private void SaveSettings()
    {
        try
        {
            _site = _sites.SaveCurrent(_settings);
            _settings = _site.Settings;
        }
        catch (SiteException ex)
        {
            _trace.Warn("Site settings could not be saved.", ex);
            Status = $"Setting not saved: {ex.Message}";
        }
    }

    // ------------------------------------------------------------------ sites

    private void ReloadSites()
    {
        SiteChoices.Clear();
        try
        {
            foreach (Site site in _sites.Sites())
            {
                bool current = string.Equals(site.Key, _site.Key, StringComparison.OrdinalIgnoreCase);
                SiteChoices.Add(new SiteChoiceViewModel(site.Key, site.Name, current, new RelayCommand(() => SwitchTo(site.Key))));
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _trace.Warn("The site list could not be read.", ex);
        }
    }

    /// <summary>
    /// Whether it is all right to close this site now. A probe or backup in flight is a hard no - it
    /// would be cut off - and recording is a question, because stopping it is what the person may want.
    /// </summary>
    private bool ReadyToLeave(string otherName)
    {
        if (IsBusy)
        {
            ShowMessage?.Invoke($"A probe or backup is running on {_site.Name}. Let it finish, or press Stop, then switch to {otherName}.");
            return false;
        }

        int recording = _recorder.Recording.Count;
        return recording == 0
            || Confirm?.Invoke(string.Create(CultureInfo.CurrentCulture,
                $"Trend recording on {recording} robot(s) at {_site.Name} stops when you switch to {otherName}. Switch anyway?")) == true;
    }

    private void SwitchTo(string key)
    {
        if (string.Equals(key, _site.Key, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        string name = SiteChoices.FirstOrDefault(c => c.Key == key)?.Name ?? key;
        if (!ReadyToLeave(name))
        {
            return;
        }

        // On success this view model has been disposed and replaced by the time Switch returns;
        // nothing after it may touch the store.
        if (_sites.Switch(key) is { } problem)
        {
            ShowMessage?.Invoke(problem);
        }
    }

    private void NewSite()
    {
        SiteDraft? draft = EditSiteDialog?.Invoke(new SiteDraft(), true);
        if (draft is null)
        {
            return;
        }

        if (!draft.TryBuild(new SiteSettings(), out SiteSettings? settings, out string? problem))
        {
            ShowMessage?.Invoke(problem ?? "The site details are not valid.");
            return;
        }

        Site created;
        try
        {
            created = _sites.Create(settings!);
        }
        catch (SiteException ex)
        {
            ShowMessage?.Invoke(ex.Message + (ex.Remediation is null ? string.Empty : Environment.NewLine + Environment.NewLine + ex.Remediation));
            return;
        }

        _store.Info(EventCategory.App, null, $"Site '{created.Name}' created.");
        ReloadSites();
        SwitchTo(created.Key);
    }

    private void EditSite()
    {
        SiteDraft? draft = EditSiteDialog?.Invoke(SiteDraft.From(_settings), false);
        if (draft is null)
        {
            return;
        }

        if (!draft.TryBuild(_settings, out SiteSettings? settings, out string? problem))
        {
            ShowMessage?.Invoke(problem ?? "The site details are not valid.");
            return;
        }

        string oldArchive = _archive.Root;
        try
        {
            _site = _sites.SaveCurrent(settings!);
            _settings = _site.Settings;
        }
        catch (SiteException ex)
        {
            ShowMessage?.Invoke(ex.Message + (ex.Remediation is null ? string.Empty : Environment.NewLine + Environment.NewLine + ex.Remediation));
            return;
        }

        _store.Info(EventCategory.App, null, $"Site settings changed: '{_site.Name}'.");
        if (!string.Equals(oldArchive, _settings.ArchiveRoot, StringComparison.OrdinalIgnoreCase))
        {
            // Backups already taken stay where they are; the history now reads the new folder.
            _archive = new BackupArchive(_settings.ArchiveRoot);
            _store.Info(EventCategory.App, null, $"Archive folder set to {_settings.ArchiveRoot}.");
            OnPropertyChanged(nameof(ArchiveRoot));
            RefreshAll();
        }

        OnPropertyChanged(nameof(SiteName));
        OnPropertyChanged(nameof(WindowTitle));
        ReloadSites();
    }

    private void ExportSite()
    {
        string suggested = ArchiveNames.RobotFolder(_site.Name) + ".robcontrol-site.json";
        if (PickSiteFileToSave?.Invoke(suggested) is not { } path)
        {
            return;
        }

        List<Robot> robots = [.. Robots.Select(r => r.Robot)];
        try
        {
            SiteCatalog.Export(_site, robots, path, _tool);
        }
        catch (SiteException ex)
        {
            ShowMessage?.Invoke(ex.Message + (ex.Remediation is null ? string.Empty : Environment.NewLine + Environment.NewLine + ex.Remediation));
            return;
        }

        _store.Info(EventCategory.App, null, string.Create(CultureInfo.InvariantCulture, $"Site exported to {path}: {robots.Count} robot(s)."));
        bool passwords = _settings.DefaultFtpPassword.Length > 0 || robots.Any(r => r.Ftp.Password.Length > 0);
        Status = string.Create(CultureInfo.CurrentCulture, $"Exported {_site.Name} and {robots.Count} robot(s) to {path}.")
            + (passwords ? " It contains FTP passwords - keep it like a list of logins." : string.Empty);
    }

    private void ImportSite()
    {
        if (PickSiteFileToOpen?.Invoke() is not { } path)
        {
            return;
        }

        SiteImportResult result;
        try
        {
            result = _sites.Import(path);
        }
        catch (RobControlException ex)
        {
            ShowMessage?.Invoke(ex.Message + (ex.Remediation is null ? string.Empty : Environment.NewLine + Environment.NewLine + ex.Remediation));
            return;
        }

        ReloadSites();
        string summary = string.Create(CultureInfo.CurrentCulture, $"Imported '{result.Site.Name}' with {result.Added} robot(s).")
            + (result.Skipped.Count == 0
                ? string.Empty
                : Environment.NewLine + Environment.NewLine + "Not imported:" + Environment.NewLine + "- " + string.Join(Environment.NewLine + "- ", result.Skipped))
            + Environment.NewLine + Environment.NewLine
            + $"Backups go to {result.Site.Settings.ArchiveRoot}. Switch to it now?";

        if (Confirm?.Invoke(summary) == true)
        {
            SwitchTo(result.Site.Key);
        }
    }

    private void DiagnoseNetwork()
    {
        if (Selected is { } row && NetControlHandoff.Open(row.Robot.Address) is { } problem)
        {
            ShowMessage?.Invoke(problem);
        }
    }

    private string RobotFolderOrRoot()
    {
        string folder = Selected is { } row ? _archive.RobotFolder(row.Robot) : _archive.Root;
        return Directory.Exists(folder) ? folder : _archive.Root;
    }

    // ------------------------------------------------------------------ events

    private void OnEventRecorded(object? sender, RobotEvent e) => _ui.Post(() =>
    {
        if (EventsForSelectedOnly && Selected is { } row && e.Robot != row.Robot.Describe())
        {
            return;
        }

        Events.Insert(0, new EventRowViewModel(e));
        while (Events.Count > MaxEventRows)
        {
            Events.RemoveAt(Events.Count - 1);
        }
    });

    private void ReloadEvents()
    {
        Events.Clear();
        Robot? filter = EventsForSelectedOnly ? Selected?.Robot : null;
        foreach (RobotEvent e in _store.RecentEvents(MaxEventRows, filter))
        {
            Events.Add(new EventRowViewModel(e));
        }
    }

    // ------------------------------------------------------------------ busy tracking

    private CancellationToken Begin()
    {
        if (_running++ == 0)
        {
            _operation = CancellationTokenSource.CreateLinkedTokenSource(_shutdown.Token);
        }

        OnPropertyChanged(nameof(IsBusy));
        RaiseCanExecute();
        return _operation!.Token;
    }

    private void End()
    {
        if (--_running == 0)
        {
            _operation?.Dispose();
            _operation = null;
        }

        OnPropertyChanged(nameof(IsBusy));
        RaiseCanExecute();
    }

    private void RaiseCanExecute()
    {
        EditRobotCommand.NotifyCanExecuteChanged();
        RemoveRobotCommand.NotifyCanExecuteChanged();
        ProbeSelectedCommand.NotifyCanExecuteChanged();
        Trend.ReadNowCommand.NotifyCanExecuteChanged();
        ProbeAllCommand.NotifyCanExecuteChanged();
        BackupSelectedCommand.NotifyCanExecuteChanged();
        BackupAllCommand.NotifyCanExecuteChanged();
        StopCommand.NotifyCanExecuteChanged();
        CompareCommand.NotifyCanExecuteChanged();
        DiagnoseNetworkCommand.NotifyCanExecuteChanged();
    }
}
