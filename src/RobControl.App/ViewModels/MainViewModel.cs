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
using RobControl.Core.Systems;
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
/// <para><b>One site, one system.</b> A view model is built on one site's database and one of its
/// systems, and shows that system's robots only. Switching is asked of <see cref="ISiteHost"/>, which
/// disposes this one and builds another - nothing in here ever holds two sites' robots at once.</para>
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
    private RobotSystem _system;
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

    public MainViewModel(
        IUiDispatcher ui, FleetStore store, Site site, RobotSystem system, ISiteHost sites, string tool, ITraceLog? trace = null, TrendRecorder? recorder = null)
    {
        _ui = ui ?? throw new ArgumentNullException(nameof(ui));
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _site = site ?? throw new ArgumentNullException(nameof(site));
        _system = system ?? throw new ArgumentNullException(nameof(system));
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
        Live = new LiveViewModel(ui, store, () => Selected, _recorder.IsRecording);

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
        ImportSiteCommand = new AsyncRelayCommand(ImportSiteAsync, () => !IsBusy);
        ExportBundleCommand = new AsyncRelayCommand(ExportBundleAsync, () => !IsBusy);
        ExportSiteCommand = new RelayCommand(ExportSite);
        OpenSiteFolderCommand = new RelayCommand(() => OpenFolder?.Invoke(_site.Folder));
        ExportEventsCommand = new RelayCommand(ExportEvents);
        ChooseSystemCommand = new RelayCommand(ChooseSystem);
        EditSystemCommand = new RelayCommand(EditSystem, () => !_system.IsUnassigned);
        OpenSystemFolderCommand = new RelayCommand(() => OpenFolder?.Invoke(_system.IsUnassigned ? _site.ConfigFolder : _system.Folder));

        _store.EventRecorded += OnEventRecorded;
        LoadRobots();
        ReloadEvents();
        Trend.Reload();
        ReloadSites();
        ReloadSystems();
    }

    // ------------------------------------------------------------------ the view's delegates

    /// <summary>Shows the robot dialog. A draft with no Id means "add"; null out means cancelled.</summary>
    public Func<RobotDraft, RobotDraft?>? EditRobotDialog { get; set; }

    /// <summary>Shows the system dialog. Null out means cancelled.</summary>
    public Func<SystemDraft, SystemDraft?>? EditSystemDialog { get; set; }

    /// <summary>Shows the system picker. Null out means cancelled.</summary>
    public Func<SystemPickerViewModel, SystemChoice?>? PickSystemDialog { get; set; }

    /// <summary>
    /// The dialog on every Back up: which devices, folders or not. Null out means cancelled. Not shown
    /// for a scheduled backup - nobody is there - which uses each robot's settings as they are.
    /// </summary>
    public Func<BackupChoiceViewModel, bool>? ChooseBackupDialog { get; set; }

    /// <summary>Shows the site dialog. The flag is true for a new site; null out means cancelled.</summary>
    public Func<SiteDraft, bool, SiteDraft?>? EditSiteDialog { get; set; }

    /// <summary>A file picker for a site file to import. Null when cancelled.</summary>
    public Func<string?>? PickSiteFileToOpen { get; set; }

    /// <summary>A save-as picker for a site export, with a suggested file name. Null when cancelled.</summary>
    public Func<string, string?>? PickSiteFileToSave { get; set; }

    /// <summary>A yes/no question, defaulting to No.</summary>
    public Func<string, bool>? Confirm { get; set; }

    public Action<string>? ShowMessage { get; set; }

    /// <summary>A save-file picker: title, suggested name and filter in; path out, null when cancelled.</summary>
    public Func<string, string, string, string?>? PickSaveFile { get; set; }

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
                Live.SelectionChanged();

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

    /// <summary>The Live tab: what the selected robot says right now, read-only.</summary>
    public LiveViewModel Live { get; }

    /// <summary>The open site as the fleet-wide tabs read it, at this moment.</summary>
    public FleetContext Snapshot() => new(
        _site.Name,
        _settings.Notes,
        _archive,
        [.. Robots.Select(r => (r.Robot, r.Identity))],
        _tool,
        _settings.StaleAfterDays,
        _settings.KeepBackups,
        _store);

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

    public string WindowTitle => $"RobControl - {_site.Name} / {_system.Name}";

    /// <summary>The open system's name: beside the site's in the status bar.</summary>
    public string SystemName => _system.Name;

    /// <summary>Every system of the open site, for Robots > Move to system.</summary>
    public ObservableCollection<SiteChoiceViewModel> MoveChoices { get; } = [];

    /// <summary>Every system of the open site, for System > Switch to.</summary>
    public ObservableCollection<SiteChoiceViewModel> SystemChoices { get; } = [];

    /// <summary>What a backup of this system takes unless a robot says otherwise: "MD:, FR: and subfolders".</summary>
    public string BackupDefaultsText
    {
        get
        {
            BackupOptions options = BackupProfile.Resolve(_system.Settings.Backup, null, _settings.IncludeFrom);
            string folders = options.IncludeSubfolders ? " with subfolders" : string.Empty;
            string excluded = options.Exclude.Count == 0 ? string.Empty : $", leaving out {string.Join(" ", options.Exclude)}";
            return $"Backs up {BackupProfile.Describe(options.Devices)}{folders}{excluded}";
        }
    }

    /// <summary>Every site on this PC, for the Site > Switch to menu.</summary>
    public ObservableCollection<SiteChoiceViewModel> SiteChoices { get; } = [];

    public string ArchiveRoot => _archive.Root;

    public int ScheduleHours => _settings.ScheduleHours;

    public string ScheduleText => _settings.ScheduleHours == 0
        ? "Scheduled backups off"
        : _nextScheduled is { } next
            ? string.Create(CultureInfo.CurrentCulture, $"Every {_settings.ScheduleHours} h - next {next.ToLocalTime():ddd HH:mm}")
            : string.Create(CultureInfo.CurrentCulture, $"Every {_settings.ScheduleHours} h");

    /// <summary>
    /// The whole event log of this site - or the selected robot's, when the tab shows only that - as
    /// CSV. The record of everything RobControl has sent to a controller here.
    /// </summary>
    public IRelayCommand ExportEventsCommand { get; }

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

    public IAsyncRelayCommand ImportSiteCommand { get; }

    public IRelayCommand ExportSiteCommand { get; }

    /// <summary>The whole site in one zip: settings, robots, event log, trends and - if asked - every backup.</summary>
    public IAsyncRelayCommand ExportBundleCommand { get; }

    public IRelayCommand OpenSiteFolderCommand { get; }

    /// <summary>The picker: another system of this site, another site, or a new one.</summary>
    public IRelayCommand ChooseSystemCommand { get; }

    public IRelayCommand EditSystemCommand { get; }

    /// <summary>The open system's folder - its system.json - in Explorer.</summary>
    public IRelayCommand OpenSystemFolderCommand { get; }

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

                        // Only a scheduled run retries: nobody is watching it, and a robot that was
                        // rebooting at 3 am usually answers five minutes later.
                        _ = BackupAsync([.. Robots], _settings.ScheduleRetries, ask: false);
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
        Live.Dispose();
        _shutdown.Cancel();
        _operation?.Cancel();
        _shutdown.Dispose();
    }

    // ------------------------------------------------------------------ robots

    private void LoadRobots()
    {
        Robots.Clear();
        IReadOnlyList<Robot> members;
        try
        {
            members = SystemMembership.Members(_system, _store.Robots(), _sites.Systems());
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _trace.Warn("The system list could not be read.", ex);
            members = [];
        }

        foreach (Robot robot in members)
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
            SystemName = _system.IsUnassigned ? null : _system.Name,
            SystemDevices = SystemDevicesText(),
        });
        if (draft is null || !TrySave(draft, out Robot? saved, out BackupProfile? backup))
        {
            return;
        }

        // Into this system's file, so it is listed here - and in the file someone copies to the next laptop.
        if (!_system.IsUnassigned)
        {
            SaveSystemQuietly(_system.Settings.With(saved!, backup, replaceBackup: true));
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

        RobotDraft? draft = EditRobotDialog?.Invoke(RobotDraft.From(row.Robot, _system.Settings.EntryFor(row.Robot)?.Backup) with
        {
            SystemName = _system.IsUnassigned ? null : _system.Name,
            SystemDevices = SystemDevicesText(),
        });
        if (draft is null || !TrySave(draft, out Robot? saved, out BackupProfile? backup))
        {
            return;
        }

        bool renamed = !string.Equals(row.Robot.Name, saved!.Name, StringComparison.Ordinal);
        if (!_system.IsUnassigned)
        {
            // Keyed by address in system.json: an edited address moves the entry, an edited name refreshes it.
            SaveSystemQuietly(_system.Settings.Renamed(row.Robot, saved).With(saved, backup, replaceBackup: true));
        }

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

    private bool TrySave(RobotDraft draft, out Robot? saved, out BackupProfile? backup)
    {
        saved = null;
        backup = null;
        if (!draft.TryBuild(out Robot? robot, out string? problem) || !draft.Backup.TryBuild(out backup, out problem))
        {
            ShowMessage?.Invoke(problem ?? "The robot details are not valid.");
            return false;
        }

        // Against the whole site, not just this system: two rows for one controller anywhere at the
        // site would still be two sessions on it.
        if (_store.Robots().FirstOrDefault(r => r.Id != robot!.Id && r.Address.Equals(robot.Address) && r.FtpPort == robot.FtpPort) is { } other)
        {
            string where = _sites.Systems().FirstOrDefault(s => s.Settings.Contains(other)) is { } system && system.Key != _system.Key
                ? $" (as '{other.Name}' in system '{system.Name}')"
                : $" (as '{other.Name}')";
            ShowMessage?.Invoke($"{robot!.Address} is already in this site's list{where}. Two rows for one controller would mean two sessions on it.");
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
        try
        {
            _sites.ForgetRobot(row.Robot);
            _system = _sites.CurrentSystem;
        }
        catch (RobControlException ex)
        {
            Status = $"Removed, but its line in system.json could not be taken out: {ex.Message}";
        }
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

    /// <param name="ask">Show the device dialog first. False for a scheduled backup: nobody is there to answer.</param>
    private async Task BackupAsync(IReadOnlyList<RobotRowViewModel> rows, int retries = 0, bool ask = true)
    {
        List<RobotRowViewModel> todo = [.. rows.Where(r => !r.IsBusy)];
        if (todo.Count == 0)
        {
            return;
        }

        // Each robot's options from its system and its own settings; then, when someone is there,
        // the dialog to change the devices for this one backup.
        IReadOnlyDictionary<Robot, BackupOptions> chosen = todo.ToDictionary(r => r.Robot, r => OptionsFor(r.Robot));
        if (ask && ChooseBackupDialog is { } dialog)
        {
            var choice = new BackupChoiceViewModel([.. todo.Select(r => (r.Robot, chosen[r.Robot]))]);
            if (!dialog(choice) || !choice.TryBuild(out IReadOnlyDictionary<Robot, BackupOptions>? picked))
            {
                return;
            }

            chosen = picked!;
        }

        CancellationToken token = Begin();
        var runner = new BackupRunner(_archive, _tool, _store);
        var options = BackupProfile.Resolve(_system.Settings.Backup, null, _settings.IncludeFrom);
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
        string devices = string.Join(" / ", chosen.Values.Select(o => BackupProfile.Describe(o.Devices)).Distinct(StringComparer.Ordinal));
        Status = string.Create(CultureInfo.CurrentCulture, $"Backing up {todo.Count} robot(s) - {devices} - into {_archive.Root}...");

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
                token,
                retries,
                TimeSpan.FromMinutes(_settings.RetryDelayMinutes),
                (again, delay) => _ui.Post(() =>
                {
                    string names = string.Join(", ", again.Select(r => r.Name));
                    _store.Info(EventCategory.Backup, null, string.Create(CultureInfo.InvariantCulture,
                        $"Retrying {again.Count} robot(s) in {delay.TotalMinutes:0} min: {names}"));
                    foreach (Robot robot in again)
                    {
                        RobotRowViewModel row = byRobot[robot];
                        row.IsBusy = true;
                        row.Activity = string.Create(CultureInfo.CurrentCulture, $"Retry at {DateTime.Now.Add(delay):HH:mm}...");
                        finished--;
                    }

                    Status = string.Create(CultureInfo.CurrentCulture,
                        $"{again.Count} robot(s) did not complete; trying them again in {delay.TotalMinutes:0} min: {names}");
                }),
                robot => chosen.TryGetValue(robot, out BackupOptions? own) ? own : options), token).ConfigureAwait(true);
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

    private void ExportEvents()
    {
        Robot? robot = EventsForSelectedOnly ? Selected?.Robot : null;
        IReadOnlyList<RobotEvent> events;
        try
        {
            events = _store.RecentEvents(int.MaxValue, robot);
        }
        catch (Exception ex) when (ex is RobControlException or InvalidOperationException or System.Data.Common.DbException)
        {
            ShowMessage?.Invoke($"The event log could not be read: {ex.Message}");
            return;
        }

        string what = robot is null ? "event log" : $"{ArchiveNames.RobotFolder(robot.Name)} event log";
        string? status = CsvExport.Save(PickSaveFile, ShowMessage, "Export event log", _site.Name, what,
            () => EventLogCsv.ToCsv(events), string.Create(CultureInfo.CurrentCulture, $"Exported {events.Count} events to"));
        if (status is not null)
        {
            Status = status;
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

    // ------------------------------------------------------------------ systems

    /// <summary>What <paramref name="robot"/>'s backup takes: built-in defaults, its system, then its own line in system.json.</summary>
    private BackupOptions OptionsFor(Robot robot) =>
        BackupProfile.Resolve(_system.Settings.Backup, _system.Settings.EntryFor(robot)?.Backup, _settings.IncludeFrom);

    private string SystemDevicesText() =>
        BackupProfile.Describe(BackupProfile.Resolve(_system.Settings.Backup, null, _settings.IncludeFrom).Devices);

    /// <summary>Writes the open system's file. Never throws: the status bar says when it did not save.</summary>
    private void SaveSystemQuietly(SystemSettings settings)
    {
        try
        {
            _system = _sites.SaveSystem(settings);
            OnPropertyChanged(nameof(BackupDefaultsText));
        }
        catch (RobControlException ex)
        {
            _trace.Warn("System settings could not be saved.", ex);
            Status = $"{_system.SettingsPath} not saved: {ex.Message}";
        }
    }

    private void ReloadSystems()
    {
        MoveChoices.Clear();
        SystemChoices.Clear();
        try
        {
            foreach (RobotSystem system in _sites.Systems())
            {
                bool current = system.Key == _system.Key;
                string key = system.Key;
                SystemChoices.Add(new SiteChoiceViewModel(key, system.Name, current,
                    new RelayCommand(() => OpenSystem(new SystemChoice(_site.Key, key)))));
                MoveChoices.Add(new SiteChoiceViewModel(key, system.Name, current,
                    new RelayCommand(() => MoveSelectedTo(key), () => !current && Targets.Count > 0 && Targets.All(r => !r.IsBusy))));
            }

            if (!_system.IsUnassigned && SystemMembership.Members(RobotSystem.Unassigned(_site.Key), _store.Robots(), _sites.Systems()).Count is > 0 and int loose)
            {
                SystemChoices.Add(new SiteChoiceViewModel(RobotSystem.UnassignedKey,
                    string.Create(CultureInfo.CurrentCulture, $"Not in a system ({loose})"), false,
                    new RelayCommand(() => OpenSystem(new SystemChoice(_site.Key, RobotSystem.UnassignedKey)))));
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _trace.Warn("The system list could not be read.", ex);
        }
    }

    private void ChooseSystem()
    {
        var picker = new SystemPickerViewModel(_sites, new SystemChoice(_site.Key, _system.IsUnassigned ? null : _system.Key), isStartup: false);
        SystemChoice? choice = PickSystemDialog?.Invoke(picker);
        if (choice is not null)
        {
            OpenSystem(choice);
        }
    }

    private void OpenSystem(SystemChoice choice)
    {
        if (string.Equals(choice.SiteKey, _site.Key, StringComparison.OrdinalIgnoreCase)
            && string.Equals(choice.SystemKey, _system.Key, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        string name = choice.SystemKey ?? choice.SiteKey;
        if (!ReadyToLeave(name))
        {
            return;
        }

        // On success this view model has been disposed and replaced by the time Open returns;
        // nothing after it may touch the store.
        if (_sites.Open(choice) is { } problem)
        {
            ShowMessage?.Invoke(problem);
            ReloadSites();
            ReloadSystems();
        }
    }

    private void EditSystem()
    {
        if (_system.IsUnassigned)
        {
            return;
        }

        SystemDraft? draft = EditSystemDialog?.Invoke(SystemDraft.From(_system));
        if (draft is null)
        {
            return;
        }

        if (!draft.TryBuild(_system.Settings, out SystemSettings? settings, out string? problem))
        {
            ShowMessage?.Invoke(problem ?? "The system details are not valid.");
            return;
        }

        try
        {
            _system = _sites.SaveSystem(settings!);
        }
        catch (RobControlException ex)
        {
            ShowMessage?.Invoke(ex.Message + (ex.Remediation is null ? string.Empty : Environment.NewLine + Environment.NewLine + ex.Remediation));
            return;
        }

        _store.Info(EventCategory.App, null, $"System settings changed: '{_system.Name}' - {BackupDefaultsText}.");
        OnPropertyChanged(nameof(SystemName));
        OnPropertyChanged(nameof(WindowTitle));
        OnPropertyChanged(nameof(BackupDefaultsText));
        ReloadSystems();
    }

    /// <summary>Moves the selected robots into another system of this site. They leave this list.</summary>
    private void MoveSelectedTo(string systemKey)
    {
        List<RobotRowViewModel> rows = [.. Targets.Where(r => !r.IsBusy)];
        if (rows.Count == 0)
        {
            return;
        }

        string name = _sites.Systems().FirstOrDefault(s => s.Key == systemKey)?.Name ?? systemKey;
        int recording = rows.Count(r => _recorder.IsRecording(r.Robot.Id));
        if (recording > 0 && Confirm?.Invoke(string.Create(CultureInfo.CurrentCulture,
                $"Trend recording on {recording} of them stops when they move to {name}. Move anyway?")) != true)
        {
            return;
        }

        foreach (RobotRowViewModel row in rows)
        {
            try
            {
                _recorder.StopAsync(row.Robot.Id).GetAwaiter().GetResult();
                _sites.MoveRobot(row.Robot, systemKey);
                _store.Info(EventCategory.App, row.Robot, $"Moved from system '{_system.Name}' to '{name}'.");
                Robots.Remove(row);
            }
            catch (RobControlException ex)
            {
                ShowMessage?.Invoke($"{row.Name} could not be moved: {ex.Message}");
                break;
            }
        }

        _system = _sites.CurrentSystem;
        Selected = Robots.FirstOrDefault();
        Status = string.Create(CultureInfo.CurrentCulture, $"Moved {rows.Count} robot(s) to {name}. System menu to switch to it.");
        ReloadSystems();
        RaiseCanExecute();
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

        // The whole site, every system: an export is the site's robot list.
        List<Robot> robots = [.. _store.Robots()];
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

    private async Task ImportSiteAsync()
    {
        if (PickSiteFileToOpen?.Invoke() is not { } path)
        {
            return;
        }

        if (path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
        {
            await ImportBundleAsync(path).ConfigureAwait(true);
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
            + NotImported(result.Skipped)
            + Environment.NewLine + Environment.NewLine
            + $"Backups go to {result.Site.Settings.ArchiveRoot}. Switch to it now?";

        if (Confirm?.Invoke(summary) == true)
        {
            SwitchTo(result.Site.Key);
        }
    }

    private async Task ImportBundleAsync(string path)
    {
        CancellationToken token = Begin();
        SiteBundleImportResult? result = null;
        try
        {
            Status = $"Importing {Path.GetFileName(path)}...";
            var progress = new Progress<string>(item => Status = $"Importing: {item}");
            result = await Task.Run(() => _sites.ImportBundle(path, progress, token), token).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            Status = "Import stopped. The new site is in the Site menu with what had been unpacked; remove its folder if it is not wanted.";
        }
        catch (Exception ex) when (ex is RobControlException or IOException or UnauthorizedAccessException or InvalidDataException)
        {
            ShowMessage?.Invoke(ex.Message + (ex is RobControlException { Remediation: { } fix } ? Environment.NewLine + Environment.NewLine + fix : string.Empty));
        }
        finally
        {
            End();
        }

        ReloadSites();
        if (result is null)
        {
            return;
        }

        Status = string.Create(CultureInfo.CurrentCulture, $"Imported '{result.Site.Name}' from {Path.GetFileName(path)}.");
        string summary = string.Create(CultureInfo.CurrentCulture, $"Imported '{result.Site.Name}': {result.Robots} robot(s), ")
            + (result.History ? "its event log and trends, " : "no event log or trends, ")
            + string.Create(CultureInfo.CurrentCulture, $"{result.BackupFolders} backup folder(s).")
            + NotImported(result.Skipped)
            + Environment.NewLine + Environment.NewLine
            + $"Its backups are in {result.Site.Settings.ArchiveRoot}. Switch to it now?";

        if (Confirm?.Invoke(summary) == true)
        {
            SwitchTo(result.Site.Key);
        }
    }

    private async Task ExportBundleAsync()
    {
        string suggested = ArchiveNames.RobotFolder(_site.Name) + SiteBundle.Extension;
        if (PickSaveFile?.Invoke("Export site bundle", suggested, "RobControl site bundle (*.robcontrol-bundle.zip)|*.robcontrol-bundle.zip") is not { } path)
        {
            return;
        }

        bool includeBackups = Confirm?.Invoke(
            $"Put every backup in {_archive.Root} into the bundle as well?\n\n"
            + "Yes: the other PC gets the full history, backups included. The file can be large.\n"
            + "No: settings, robot list, event log and trends only.") == true;

        // The whole site, every system: an export is the site's robot list.
        List<Robot> robots = [.. _store.Robots()];
        CancellationToken token = Begin();
        try
        {
            var progress = new Progress<string>(item => Status = $"Bundling: {item}");
            SiteBundleInfo info = await Task.Run(
                () => SiteBundle.Export(_site, robots, _store, _archive, includeBackups, path, _tool, progress, token), token).ConfigureAwait(true);

            _store.Info(EventCategory.App, null, string.Create(CultureInfo.InvariantCulture,
                $"Site bundle exported to {path}: {info.Robots} robot(s), {info.BackupFolders} backup folder(s), {info.BackupBytes} bytes of backups."));
            bool passwords = _settings.DefaultFtpPassword.Length > 0 || robots.Any(r => r.Ftp.Password.Length > 0);
            Status = string.Create(CultureInfo.CurrentCulture, $"Exported {_site.Name} to {path}: {info.Robots} robot(s), {info.BackupFolders} backup folder(s).")
                + (passwords ? " It contains FTP passwords - keep it like a list of logins." : string.Empty);
        }
        catch (OperationCanceledException)
        {
            Status = "Bundle export stopped. Nothing was written.";
        }
        catch (RobControlException ex)
        {
            ShowMessage?.Invoke(ex.Message + (ex.Remediation is null ? string.Empty : Environment.NewLine + Environment.NewLine + ex.Remediation));
        }
        finally
        {
            End();
        }
    }

    private static string NotImported(IReadOnlyList<string> skipped)
    {
        if (skipped.Count == 0)
        {
            return string.Empty;
        }

        // A message box with a thousand lines is no use to anyone; the event log has the full list.
        IEnumerable<string> shown = skipped.Take(12);
        string more = skipped.Count > 12 ? string.Create(CultureInfo.CurrentCulture, $"{Environment.NewLine}...and {skipped.Count - 12} more - see the event log.") : string.Empty;
        return Environment.NewLine + Environment.NewLine + "Not imported:" + Environment.NewLine + "- " + string.Join(Environment.NewLine + "- ", shown) + more;
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
        ImportSiteCommand.NotifyCanExecuteChanged();
        ExportBundleCommand.NotifyCanExecuteChanged();
        Live.RaiseCanExecute();
        foreach (SiteChoiceViewModel move in MoveChoices)
        {
            move.Command.NotifyCanExecuteChanged();
        }
    }
}
