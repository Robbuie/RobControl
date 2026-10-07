using System.Collections.ObjectModel;
using System.Globalization;
// UseWPF drops System.IO from the implicit usings.
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RobControl.Core;
using RobControl.Core.Backup;
using RobControl.Core.Events;
using RobControl.Core.Insight;

namespace RobControl.App.ViewModels;

/// <summary>
/// The Fleet tab: what every robot is and how its backups stand, the watched setting changes
/// between backups, CSV export, and the site visit report. All from the archive and the last probe.
/// </summary>
public sealed class FleetViewModel : ObservableObject
{
    private readonly Func<FleetContext> _context;
    private CancellationTokenSource? _running;
    private bool _verifyAll;
    private int _detailTab;
    private string _period = InsightPeriods.Default;
    private string _status = "Refresh lists the robots, their controllers and backups, and changes to frames, payload, mastering and other watched settings.";

    public FleetViewModel(Func<FleetContext> context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        RefreshCommand = new AsyncRelayCommand(RefreshAsync, () => _running is null);
        ExportCsvCommand = new RelayCommand(ExportCsv, () => Rows.Count > 0);
        ReportCommand = new AsyncRelayCommand(ReportAsync, () => _running is null);
        VerifyCommand = new AsyncRelayCommand(VerifyAsync, () => _running is null);
        PruneCommand = new AsyncRelayCommand(PruneAsync, () => _running is null);
        ExportNetworkCommand = new RelayCommand(ExportNetwork, () => Network.Count > 0);
    }

    /// <summary>Each robot's hostname, addresses, subnet, router and MAC as its newest backup records them.</summary>
    public ObservableCollection<NetworkRow> Network { get; } = [];

    public IRelayCommand ExportNetworkCommand { get; }

    /// <summary>The lower half's tab: 0 setting changes, 1 network, 2 backup check.</summary>
    public int DetailTab
    {
        get => _detailTab;
        set => SetProperty(ref _detailTab, value);
    }

    public static IReadOnlyList<string> Periods => InsightPeriods.All;

    /// <summary>A save-file picker: title, suggested name and filter in; path out, null when cancelled.</summary>
    public Func<string, string, string, string?>? PickSaveFile { get; set; }

    /// <summary>Opens a file the app just wrote - the report, in the browser.</summary>
    public Action<string>? OpenFile { get; set; }

    public Action<string>? ShowMessage { get; set; }

    /// <summary>A yes/no question; true only on an explicit yes.</summary>
    public Func<string, bool>? Confirm { get; set; }

    /// <summary>
    /// Removes one backup folder. The window sets this to send it to the Recycle Bin, so a prune
    /// can be undone from Explorer; with nothing set, pruning refuses rather than deleting outright.
    /// </summary>
    public Action<string>? RemoveFolder { get; set; }

    /// <summary>The last Verify backups run, damaged first.</summary>
    public ObservableCollection<BackupVerification> Checks { get; } = [];

    public bool HasChecks => Checks.Count > 0;

    /// <summary>Verify every backup in the archive, not only each robot's newest.</summary>
    public bool VerifyAll
    {
        get => _verifyAll;
        set => SetProperty(ref _verifyAll, value);
    }

    public IAsyncRelayCommand VerifyCommand { get; }

    public IAsyncRelayCommand PruneCommand { get; }

    public ObservableCollection<InventoryRowViewModel> Rows { get; } = [];

    public ObservableCollection<SettingChange> Changes { get; } = [];

    public IAsyncRelayCommand RefreshCommand { get; }

    public IRelayCommand ExportCsvCommand { get; }

    public IAsyncRelayCommand ReportCommand { get; }

    /// <summary>How far back setting changes - and the report's alarms - go.</summary>
    public string Period
    {
        get => _period;
        set => SetProperty(ref _period, value ?? InsightPeriods.Default);
    }

    public string Status
    {
        get => _status;
        private set => SetProperty(ref _status, value);
    }

    public void Cancel() => _running?.Cancel();

    private DateTimeOffset? Since => InsightPeriods.Length(Period) is { } length ? DateTimeOffset.UtcNow - length : null;

    private async Task RefreshAsync()
    {
        FleetContext context = _context();
        DateTimeOffset? since = Since;
        await RunAsync(async token =>
        {
            Status = "Reading the archive...";
            (IReadOnlyList<InventoryRow> rows, IReadOnlyList<SettingChange> changes, IReadOnlyList<NetworkRow> network) =
                await Task.Run(() => (
                    FleetInventory.Build(context.Robots, context.Archive),
                    Changed(context, since, token),
                    NetworkInventory.Build(context.Archive, context.RobotList, token)), token).ConfigureAwait(true);

            DateTimeOffset now = DateTimeOffset.UtcNow;
            Rows.Clear();
            foreach (InventoryRow row in rows)
            {
                Rows.Add(new InventoryRowViewModel(row, now, context.StaleAfterDays));
            }

            Changes.Clear();
            foreach (SettingChange change in changes)
            {
                Changes.Add(change);
            }

            Network.Clear();
            foreach (NetworkRow row in network)
            {
                Network.Add(row);
            }

            int mismatched = network.Count(n => n.AddressMismatch);

            int stale = Rows.Count(r => r.IsStale);
            int failing = Rows.Count(r => r.IsFailing);
            Status = string.Create(CultureInfo.CurrentCulture,
                $"{Rows.Count} robots; {stale} without a complete backup in {context.StaleAfterDays} days")
                + (failing > 0 ? string.Create(CultureInfo.CurrentCulture, $"; {failing} whose latest attempts failed") : string.Empty)
                + string.Create(CultureInfo.CurrentCulture, $"; {Changes.Count} watched setting changes in the last {Period.ToLowerInvariant()}")
                + (mismatched > 0 ? string.Create(CultureInfo.CurrentCulture, $"; {mismatched} whose backup does not mention the address in the robot list.") : ".");
            ExportCsvCommand.NotifyCanExecuteChanged();
            ExportNetworkCommand.NotifyCanExecuteChanged();
        }).ConfigureAwait(true);
    }

    private void ExportCsv()
    {
        FleetContext context = _context();
        string suggested = $"{ArchiveNames.RobotFolder(context.SiteName)} robots {DateTime.Now:yyyy-MM-dd}.csv";
        if (PickSaveFile?.Invoke("Export robot list", suggested, "CSV (*.csv)|*.csv") is not { } path)
        {
            return;
        }

        try
        {
            // UTF-8 with a byte-order mark: what Excel needs to read anything beyond ASCII correctly.
            File.WriteAllText(path, FleetInventory.ToCsv(Rows.Select(r => r.Row)), new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
            Status = $"Exported {Rows.Count} robots to {path}.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ShowMessage?.Invoke($"{path} could not be written: {ex.Message}");
        }
    }

    private void ExportNetwork()
    {
        NetworkRow[] rows = [.. Network];
        string? status = CsvExport.Save(PickSaveFile, ShowMessage, "Export network settings", _context().SiteName, "network",
            () => NetworkInventory.ToCsv(rows), string.Create(CultureInfo.CurrentCulture, $"Exported the network settings of {rows.Length} robots to"));
        if (status is not null)
        {
            Status = status;
        }
    }

    private async Task ReportAsync()
    {
        FleetContext context = _context();
        DateTimeOffset? since = Since;
        string suggested = $"{ArchiveNames.RobotFolder(context.SiteName)} site report {DateTime.Now:yyyy-MM-dd}.html";
        if (PickSaveFile?.Invoke("Save site report", suggested, "Web page (*.html)|*.html") is not { } path)
        {
            return;
        }

        await RunAsync(async token =>
        {
            Status = "Building the site report from the backups...";
            string html = await Task.Run(() =>
            {
                IReadOnlyList<RobotBackup> backups = FleetBackups.All(context.Archive, context.RobotList);
                IReadOnlyList<InventoryRow> inventory = FleetInventory.Build(context.Robots, context.Archive);
                List<AlarmEntry> alarms = [.. AlarmHistory.Since(AlarmHistory.Collect(backups, token), since?.LocalDateTime)];

                return SiteReport.Render(new SiteReportData
                {
                    SiteName = context.SiteName,
                    SiteNotes = context.SiteNotes,
                    GeneratedUtc = DateTimeOffset.UtcNow,
                    Tool = context.Tool,
                    ArchiveRoot = context.Archive.Root,
                    StaleAfterDays = context.StaleAfterDays,

                    // "All time" starts at the oldest backup there is.
                    Since = since ?? (backups.Count > 0 ? backups.Min(b => b.StartedUtc) : DateTimeOffset.UtcNow),
                    Inventory = inventory,
                    AlarmCodes = AlarmHistory.ByCode(alarms),
                    RobotAlarms = AlarmHistory.ByRobot(alarms),
                    ConcernAlarms = [.. alarms.Where(e => e.Concern != AlarmConcern.None)],
                    SettingChanges = Changed(context, since, token),
                });
            }, token).ConfigureAwait(true);

            await File.WriteAllTextAsync(path, html, token).ConfigureAwait(true);
            Status = $"Site report saved to {path}. Print it, or save it as PDF, from the browser.";
            OpenFile?.Invoke(path);
        }).ConfigureAwait(true);
    }

    /// <summary>
    /// Re-hashes backups against their manifests - each robot's newest, or with <see cref="VerifyAll"/>
    /// every one. Reads the disk only.
    /// </summary>
    private async Task VerifyAsync()
    {
        FleetContext context = _context();
        bool newestOnly = !VerifyAll;
        await RunAsync(async token =>
        {
            Status = "Checking backups against their manifests...";
            var progress = new Progress<string>(item => Status = $"Checking {item}...");
            IReadOnlyList<BackupVerification> results = await Task.Run(
                () => BackupVerifier.VerifyAll(context.Archive, context.RobotList, newestOnly, progress, token), token).ConfigureAwait(true);

            Checks.Clear();
            foreach (BackupVerification check in results.OrderBy(c => c.IsIntact).ThenBy(c => c.Robot, StringComparer.OrdinalIgnoreCase))
            {
                Checks.Add(check);
            }

            OnPropertyChanged(nameof(HasChecks));
            DetailTab = 2;
            int bad = results.Count(c => !c.IsIntact);
            string scope = newestOnly ? "newest backup of each robot" : "backups";
            Status = bad == 0
                ? string.Create(CultureInfo.CurrentCulture, $"Checked {results.Count} {scope}: every file matches its manifest.")
                : string.Create(CultureInfo.CurrentCulture, $"Checked {results.Count} {scope}: {bad} damaged or uncheckable - listed first below. Do not restore from those.");

            foreach (BackupVerification check in results.Where(c => !c.IsIntact))
            {
                context.Events?.Warn(EventCategory.Backup, null, $"{check.Robot} {check.FolderName}: {check.Summary}", check.FolderPath);
            }

            context.Events?.Info(EventCategory.Backup, null, string.Create(CultureInfo.InvariantCulture,
                $"Verified {results.Count} backup(s) against their manifests; {bad} damaged or uncheckable."));
        }).ConfigureAwait(true);
    }

    /// <summary>
    /// Shows what "keep the newest N" would remove, asks, and sends it to the Recycle Bin. Never runs
    /// on its own: nothing in RobControl deletes a backup without the person saying so.
    /// </summary>
    private async Task PruneAsync()
    {
        FleetContext context = _context();
        if (context.KeepBackups < 1)
        {
            ShowMessage?.Invoke("Pruning is off for this site. Set \"Keep complete backups per robot\" in Site > Site settings first - nothing is ever pruned until you do.");
            return;
        }

        if (RemoveFolder is null)
        {
            return;
        }

        await RunAsync(async token =>
        {
            Status = "Working out which backups are past the limit...";
            RetentionPlan plan = await Task.Run(() => RetentionPlan.Build(context.Archive, context.RobotList, context.KeepBackups), token).ConfigureAwait(true);
            if (plan.Remove.Count == 0)
            {
                Status = string.Create(CultureInfo.CurrentCulture, $"Nothing to prune: no robot has more than {plan.Keep} complete backups.");
                return;
            }

            int robots = plan.Remove.Select(r => r.Robot).Distinct(StringComparer.OrdinalIgnoreCase).Count();
            string oldest = plan.Remove.Min(r => r.StartedUtc).ToLocalTime().ToString("d", CultureInfo.CurrentCulture);
            string question = string.Create(CultureInfo.CurrentCulture,
                $"Send {plan.Remove.Count} old backup(s) of {robots} robot(s) to the Recycle Bin - {Bytes.Describe(plan.Bytes)}, the oldest from {oldest}?\n\n")
                + string.Create(CultureInfo.CurrentCulture, $"Each robot keeps its newest {plan.Keep} complete backup(s) and everything taken after the oldest of them. ")
                + "Robots with fewer complete backups lose nothing. Folders without a manifest are not touched.";
            if (Confirm?.Invoke(question) != true)
            {
                Status = "Prune cancelled. Nothing was removed.";
                return;
            }

            int removed = 0;
            var failed = new List<string>();
            foreach (RetentionCandidate candidate in plan.Remove)
            {
                token.ThrowIfCancellationRequested();
                Status = $"Removing {candidate.Robot} {candidate.FolderName}...";
                try
                {
                    // On the UI thread on purpose: the Recycle Bin can show its own error dialog.
                    RemoveFolder(candidate.FolderPath);
                    removed++;
                    await Task.Yield();
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
                {
                    failed.Add($"{candidate.Robot} {candidate.FolderName}: {ex.Message}");
                }
            }

            context.Events?.Info(EventCategory.Backup, null, string.Create(CultureInfo.InvariantCulture,
                $"Pruned {removed} old backup(s) to the Recycle Bin, keeping the newest {plan.Keep} complete per robot."),
                failed.Count == 0 ? null : string.Join(Environment.NewLine, failed));
            Status = failed.Count == 0
                ? string.Create(CultureInfo.CurrentCulture, $"Sent {removed} old backup(s) to the Recycle Bin.")
                : string.Create(CultureInfo.CurrentCulture, $"Sent {removed} to the Recycle Bin; {failed.Count} could not be moved - see the event log.");
        }).ConfigureAwait(true);
    }

    private static List<SettingChange> Changed(FleetContext context, DateTimeOffset? since, CancellationToken token)
    {
        var changes = new List<SettingChange>();
        foreach (var robot in context.RobotList)
        {
            changes.AddRange(SettingsWatch.History(robot.Name, context.Archive.List(robot), since, token));
        }

        return [.. changes.OrderByDescending(c => c.NewerUtc)];
    }

    private async Task RunAsync(Func<CancellationToken, Task> work)
    {
        _running = new CancellationTokenSource();
        RaiseCanExecute();
        try
        {
            await work(_running.Token).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            Status = "Stopped.";
        }
        catch (RobControlException ex)
        {
            Status = ex.Message;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Status = $"Could not read or write: {ex.Message}";
        }
        finally
        {
            _running.Dispose();
            _running = null;
            RaiseCanExecute();
        }
    }

    private void RaiseCanExecute()
    {
        RefreshCommand.NotifyCanExecuteChanged();
        ReportCommand.NotifyCanExecuteChanged();
        VerifyCommand.NotifyCanExecuteChanged();
        PruneCommand.NotifyCanExecuteChanged();
    }
}
