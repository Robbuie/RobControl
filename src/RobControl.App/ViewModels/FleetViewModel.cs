using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RobControl.Core;
using RobControl.Core.Backup;
using RobControl.Core.Insight;

namespace RobControl.App.ViewModels;

/// <summary>
/// The Fleet tab: what every robot is and how its backups stand, the watched setting changes
/// between backups, CSV export, and the site visit report. All from the archive and the last probe.
/// </summary>
public sealed class FleetViewModel : ObservableObject
{
    /// <summary>A robot whose last complete backup is older than this is flagged. A week suits a weekly visit or a daily schedule.</summary>
    public const int StaleAfterDays = 7;

    private readonly Func<FleetContext> _context;
    private CancellationTokenSource? _running;
    private string _period = InsightPeriods.Default;
    private string _status = "Refresh lists the robots, their controllers and backups, and changes to frames, payload, mastering and other watched settings.";

    public FleetViewModel(Func<FleetContext> context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        RefreshCommand = new AsyncRelayCommand(RefreshAsync, () => _running is null);
        ExportCsvCommand = new RelayCommand(ExportCsv, () => Rows.Count > 0);
        ReportCommand = new AsyncRelayCommand(ReportAsync, () => _running is null);
    }

    public static IReadOnlyList<string> Periods => InsightPeriods.All;

    /// <summary>A save-file picker: title, suggested name and filter in; path out, null when cancelled.</summary>
    public Func<string, string, string, string?>? PickSaveFile { get; set; }

    /// <summary>Opens a file the app just wrote - the report, in the browser.</summary>
    public Action<string>? OpenFile { get; set; }

    public Action<string>? ShowMessage { get; set; }

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
            (IReadOnlyList<InventoryRow> rows, IReadOnlyList<SettingChange> changes) =
                await Task.Run(() => (FleetInventory.Build(context.Robots, context.Archive), Changed(context, since, token)), token).ConfigureAwait(true);

            DateTimeOffset now = DateTimeOffset.UtcNow;
            Rows.Clear();
            foreach (InventoryRow row in rows)
            {
                Rows.Add(new InventoryRowViewModel(row, now, StaleAfterDays));
            }

            Changes.Clear();
            foreach (SettingChange change in changes)
            {
                Changes.Add(change);
            }

            int stale = Rows.Count(r => r.IsStale);
            Status = string.Create(CultureInfo.CurrentCulture,
                $"{Rows.Count} robots; {stale} without a complete backup in {StaleAfterDays} days; {Changes.Count} watched setting changes in the last {Period.ToLowerInvariant()}.");
            ExportCsvCommand.NotifyCanExecuteChanged();
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
                    StaleAfterDays = StaleAfterDays,

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
        RefreshCommand.NotifyCanExecuteChanged();
        ReportCommand.NotifyCanExecuteChanged();
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
            RefreshCommand.NotifyCanExecuteChanged();
            ReportCommand.NotifyCanExecuteChanged();
        }
    }
}
