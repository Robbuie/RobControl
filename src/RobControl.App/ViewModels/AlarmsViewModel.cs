using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RobControl.Core.Insight;

namespace RobControl.App.ViewModels;

/// <summary>
/// The Alarms tab: the alarm logs in every backup of the site, merged and de-duplicated - most
/// frequent alarms, per-robot counts and mean time between alarms, and the occurrences of one code.
/// </summary>
public sealed class AlarmsViewModel : ObservableObject
{
    private readonly Func<FleetContext> _context;
    private IReadOnlyList<AlarmEntry> _all = [];
    private CancellationTokenSource? _running;
    private string _period = InsightPeriods.Default;
    private bool _onlyConcerns;
    private AlarmCodeSummary? _selectedCode;
    private string _status = "Refresh reads the alarm log in every backup of this site. Nothing is read from the robots.";

    public AlarmsViewModel(Func<FleetContext> context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        RefreshCommand = new AsyncRelayCommand(RefreshAsync, () => _running is null);
    }

    public static IReadOnlyList<string> Periods => InsightPeriods.All;

    public ObservableCollection<AlarmCodeSummary> Codes { get; } = [];

    public ObservableCollection<RobotAlarmSummary> Robots { get; } = [];

    public ObservableCollection<AlarmEntry> Occurrences { get; } = [];

    public IAsyncRelayCommand RefreshCommand { get; }

    public string Period
    {
        get => _period;
        set
        {
            if (SetProperty(ref _period, value ?? InsightPeriods.Default))
            {
                Aggregate();
            }
        }
    }

    /// <summary>Only battery, collision and mastering alarms - the ones that mean a job to plan.</summary>
    public bool OnlyConcerns
    {
        get => _onlyConcerns;
        set
        {
            if (SetProperty(ref _onlyConcerns, value))
            {
                Aggregate();
            }
        }
    }

    public AlarmCodeSummary? SelectedCode
    {
        get => _selectedCode;
        set
        {
            if (SetProperty(ref _selectedCode, value))
            {
                ShowOccurrences();
            }
        }
    }

    public string Status
    {
        get => _status;
        private set => SetProperty(ref _status, value);
    }

    public void Cancel() => _running?.Cancel();

    private async Task RefreshAsync()
    {
        FleetContext context = _context();
        _running = new CancellationTokenSource();
        RefreshCommand.NotifyCanExecuteChanged();
        Status = "Reading alarm logs from the backups...";
        try
        {
            CancellationToken token = _running.Token;
            _all = await Task.Run(() => AlarmHistory.Collect(FleetBackups.All(context.Archive, context.RobotList), token), token).ConfigureAwait(true);
            Aggregate();
        }
        catch (OperationCanceledException)
        {
            Status = "Stopped.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Status = $"Could not read the archive: {ex.Message}";
        }
        finally
        {
            _running.Dispose();
            _running = null;
            RefreshCommand.NotifyCanExecuteChanged();
        }
    }

    private List<AlarmEntry> InPeriod()
    {
        DateTime? since = InsightPeriods.Length(Period) is { } length ? DateTime.Now - length : null;
        IEnumerable<AlarmEntry> entries = AlarmHistory.Since(_all, since);
        return [.. OnlyConcerns ? entries.Where(e => e.Concern != AlarmConcern.None) : entries];
    }

    private void Aggregate()
    {
        List<AlarmEntry> entries = InPeriod();
        string? keep = SelectedCode?.Code;
        Replace(Codes, AlarmHistory.ByCode(entries));
        Replace(Robots, AlarmHistory.ByRobot(entries));
        SelectedCode = Codes.FirstOrDefault(c => c.Code == keep) ?? Codes.FirstOrDefault();
        ShowOccurrences();
        int concerns = entries.Count(e => e.Concern != AlarmConcern.None);
        Status = _all.Count == 0
            ? "No alarm logs found in this site's backups. Back up, then Refresh."
            : string.Create(CultureInfo.CurrentCulture,
                $"{entries.Count} alarms ({Codes.Count} codes) on {Robots.Count} robots in the last {Period.ToLowerInvariant()}; {concerns} battery, collision or mastering. Times are each controller's own clock.");
    }

    private void ShowOccurrences() =>
        Replace(Occurrences, SelectedCode is { } code ? InPeriod().Where(e => e.Code == code.Code) : []);

    private static void Replace<T>(ObservableCollection<T> target, IEnumerable<T> items)
    {
        target.Clear();
        foreach (T item in items)
        {
            target.Add(item);
        }
    }
}
