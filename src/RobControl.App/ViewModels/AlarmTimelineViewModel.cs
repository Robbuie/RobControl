using System.Collections.ObjectModel;
using System.Globalization;
// UseWPF drops System.IO from the implicit usings.
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RobControl.Core;
using RobControl.Core.Insight;

namespace RobControl.App.ViewModels;

/// <summary>
/// Alarms > Cell timeline: every robot's alarms on one time axis, grouped into incidents, with the robot
/// that went first named - from the backups' alarm logs and, when asked, the robots' logs read now.
/// Each robot's clock offset is taken out where this session knows it (probes, site visits, reads now).
/// </summary>
public sealed class AlarmTimelineViewModel : ObservableObject
{
    private readonly Func<FleetContext> _context;
    private readonly Func<IReadOnlyDictionary<string, TimeSpan>> _clocks;
    private readonly Func<IProgress<string>, CancellationToken, Task<IReadOnlyList<AlarmEntry>>>? _readLive;
    private IReadOnlyList<AlarmEntry> _fromBackups = [];
    private IReadOnlyList<AlarmEntry> _fromRobots = [];
    private AlarmTimelineResult _result = new([], [], 0);
    private CancellationTokenSource? _running;
    private string _period = "7 days";
    private string _gapSeconds = "120";
    private bool _onlyCellStops;
    private AlarmIncident? _selectedIncident;
    private string _status = "Refresh lays this site's alarm logs (from the backups) on one time axis. Nothing is read from the robots unless you press Read robots now.";

    /// <param name="clocks">Per robot name: controller clock minus this PC's, where known.</param>
    /// <param name="readLive">Reads each robot's alarm log now (GET only), or null where there are no robots to ask (a folder of backups).</param>
    public AlarmTimelineViewModel(
        Func<FleetContext> context,
        Func<IReadOnlyDictionary<string, TimeSpan>> clocks,
        Func<IProgress<string>, CancellationToken, Task<IReadOnlyList<AlarmEntry>>>? readLive = null)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _clocks = clocks ?? throw new ArgumentNullException(nameof(clocks));
        _readLive = readLive;
        RefreshCommand = new AsyncRelayCommand(RefreshAsync, () => _running is null);
        ReadLiveCommand = new AsyncRelayCommand(ReadLiveAsync, () => _running is null && _readLive is not null);
        CancelCommand = new RelayCommand(Cancel, () => _running is not null);
    }

    public IAsyncRelayCommand RefreshCommand { get; }

    public IAsyncRelayCommand ReadLiveCommand { get; }

    public IRelayCommand CancelCommand { get; }

    public bool CanReadLive => _readLive is not null;

    public static IReadOnlyList<string> Periods => InsightPeriods.All;

    public ObservableCollection<AlarmIncident> Incidents { get; } = [];

    public ObservableCollection<TimelineEntry> Entries { get; } = [];

    public string Period
    {
        get => _period;
        set
        {
            if (SetProperty(ref _period, value ?? "7 days"))
            {
                Build();
            }
        }
    }

    /// <summary>Alarms closer together than this are one incident.</summary>
    public string GapSeconds
    {
        get => _gapSeconds;
        set
        {
            if (SetProperty(ref _gapSeconds, value ?? "120"))
            {
                Build();
            }
        }
    }

    /// <summary>Only incidents on more than one robot - what a cell stop looks like.</summary>
    public bool OnlyCellStops
    {
        get => _onlyCellStops;
        set
        {
            if (SetProperty(ref _onlyCellStops, value))
            {
                Show();
            }
        }
    }

    /// <summary>The incident whose alarms are listed below; null lists every alarm.</summary>
    public AlarmIncident? SelectedIncident
    {
        get => _selectedIncident;
        set
        {
            if (SetProperty(ref _selectedIncident, value))
            {
                ShowEntries();
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
        Begin();
        Status = "Reading alarm logs from the backups...";
        try
        {
            CancellationToken token = _running!.Token;
            _fromBackups = await Task.Run(() => AlarmHistory.Collect(FleetBackups.All(context.Archive, context.RobotList), token), token).ConfigureAwait(true);
            Build();
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
            End();
        }
    }

    private async Task ReadLiveAsync()
    {
        if (_readLive is null)
        {
            return;
        }

        Begin();
        try
        {
            var progress = new Progress<string>(line => Status = line);
            _fromRobots = await _readLive(progress, _running!.Token).ConfigureAwait(true);
            if (_fromBackups.Count == 0)
            {
                FleetContext context = _context();
                CancellationToken token = _running.Token;
                _fromBackups = await Task.Run(() => AlarmHistory.Collect(FleetBackups.All(context.Archive, context.RobotList), token), token).ConfigureAwait(true);
            }

            Build();
        }
        catch (OperationCanceledException)
        {
            Status = "Stopped.";
        }
        catch (Exception ex) when (ex is RobControlException or IOException or UnauthorizedAccessException or System.Net.Http.HttpRequestException)
        {
            Status = $"Could not read the robots: {ex.Message}";
        }
        finally
        {
            End();
        }
    }

    private void Build()
    {
        TimeSpan gap = int.TryParse(GapSeconds, NumberStyles.None, CultureInfo.InvariantCulture, out int s) && s is >= 1 and <= 3600
            ? TimeSpan.FromSeconds(s)
            : AlarmTimeline.DefaultGap;
        DateTime? since = InsightPeriods.Length(Period) is { } length ? DateTime.Now - length : null;
        IReadOnlyDictionary<string, TimeSpan> clocks = _clocks();
        _result = AlarmTimeline.Build(_fromBackups.Concat(_fromRobots), clocks, gap, since);
        Show();

        int robots = _result.Entries.Select(e => e.Robot).Distinct(StringComparer.OrdinalIgnoreCase).Count();
        int corrected = _result.Entries.Where(e => e.Correction is not null).Select(e => e.Robot).Distinct(StringComparer.OrdinalIgnoreCase).Count();
        int cell = _result.Incidents.Count(i => i.SpansRobots);
        string clockNote = robots == 0 ? string.Empty
            : corrected == robots ? " Every robot's clock offset is taken out."
            : _readLive is null
                ? " Times are each controller's own clock."
                : string.Create(CultureInfo.CurrentCulture, $" Clock offset known for {corrected} of {robots} robot(s) - probe or Read robots now for the rest; their times are their own clocks.");
        string undated = _result.Undated > 0 ? string.Create(CultureInfo.CurrentCulture, $" {_result.Undated} undated line(s) left out.") : string.Empty;
        Status = _result.Entries.Count == 0
            ? "No dated alarms in the period. Back up the robots (or Read robots now), then Refresh."
            : string.Create(CultureInfo.CurrentCulture,
                $"{_result.Entries.Count} alarms on {robots} robot(s), {_result.Incidents.Count} incident(s), {cell} on more than one robot.{clockNote}{undated}");
    }

    private void Show()
    {
        int? keep = SelectedIncident?.Number;
        Replace(Incidents, OnlyCellStops ? _result.Incidents.Where(i => i.SpansRobots) : _result.Incidents);
        _selectedIncident = Incidents.FirstOrDefault(i => i.Number == keep);
        OnPropertyChanged(nameof(SelectedIncident));
        ShowEntries();
    }

    private void ShowEntries() =>
        Replace(Entries, SelectedIncident is { } incident
            ? _result.Entries.Where(e => e.Incident == incident.Number)
            : OnlyCellStops ? _result.Entries.Where(e => Incidents.Any(i => i.Number == e.Incident)) : _result.Entries);

    private void Begin()
    {
        _running = new CancellationTokenSource();
        RaiseCanExecute();
    }

    private void End()
    {
        _running?.Dispose();
        _running = null;
        RaiseCanExecute();
    }

    private void RaiseCanExecute()
    {
        RefreshCommand.NotifyCanExecuteChanged();
        ReadLiveCommand.NotifyCanExecuteChanged();
        CancelCommand.NotifyCanExecuteChanged();
    }

    private static void Replace<T>(ObservableCollection<T> target, IEnumerable<T> items)
    {
        target.Clear();
        foreach (T item in items)
        {
            target.Add(item);
        }
    }
}
