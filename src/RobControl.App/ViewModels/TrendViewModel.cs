using System.Collections.ObjectModel;
using System.Globalization;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RobControl.App.Composition;
using RobControl.Core;
using RobControl.Core.Events;
using RobControl.Core.Persistence;
using RobControl.Core.Robots;
using RobControl.Core.Trending;

namespace RobControl.App.ViewModels;

/// <summary>
/// The Trends tab: which signals are watched on the selected robots, their latest values, recording
/// on and off, and the chart.
///
/// <para><b>Everything acts on every selected robot.</b> Adding <c>R[1-5], DI[1]</c> with four
/// robots selected adds those signals to all four; Start records all four at once, each on its own
/// loop; the chart overlays them, so the same register on four robots can be compared directly.</para>
/// </summary>
public sealed class TrendViewModel : ObservableObject, IDisposable
{
    public static IReadOnlyList<string> Windows { get; } = ["5 min", "30 min", "1 h", "8 h", "24 h", "7 days"];

    private readonly IUiDispatcher _ui;
    private readonly FleetStore _store;
    private readonly TrendRecorder _recorder;
    private readonly Func<IReadOnlyList<RobotRowViewModel>> _targets;
    private string _addText = string.Empty;
    private string _intervalSeconds = "5";
    private string _window = "30 min";
    private SignalRowViewModel? _selectedSignal;
    private IReadOnlyList<TrendSeries> _series = [];
    private DateTimeOffset _from;
    private DateTimeOffset _to;
    private string _status = "Select robots, add signals, then Read now or Start recording.";

    public TrendViewModel(IUiDispatcher ui, FleetStore store, TrendRecorder recorder, Func<IReadOnlyList<RobotRowViewModel>> targets)
    {
        _ui = ui ?? throw new ArgumentNullException(nameof(ui));
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _recorder = recorder ?? throw new ArgumentNullException(nameof(recorder));
        _targets = targets ?? throw new ArgumentNullException(nameof(targets));

        AddCommand = new RelayCommand(Add, () => _targets().Count > 0 && AddText.Trim().Length > 0);
        RemoveCommand = new RelayCommand(Remove, () => SelectedSignal is not null);
        ReadNowCommand = new AsyncRelayCommand(ReadNowAsync, () => Rows.Count > 0);
        StartCommand = new AsyncRelayCommand(StartAsync, () => Rows.Count > 0);
        StopCommand = new AsyncRelayCommand(StopAsync, () => _targets().Any(r => _recorder.IsRecording(r.Robot.Id)));
        StopAllCommand = new AsyncRelayCommand(_recorder.StopAllAsync, () => _recorder.Recording.Count > 0);
        RefreshChartCommand = new RelayCommand(RefreshChart);
        ExportCommand = new RelayCommand(ExportCsv, () => Rows.Count > 0);

        _recorder.StatusChanged += OnStatus;
    }

    /// <summary>A save-file picker for CSV export: suggested name in, chosen path out, null when cancelled.</summary>
    public Func<string, string?>? PickSaveFile { get; set; }

    public Action<string>? ShowMessage { get; set; }

    public ObservableCollection<SignalRowViewModel> Rows { get; } = [];

    public string AddText
    {
        get => _addText;
        set
        {
            if (SetProperty(ref _addText, value))
            {
                AddCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public string IntervalSeconds
    {
        get => _intervalSeconds;
        set => SetProperty(ref _intervalSeconds, value);
    }

    public string Window
    {
        get => _window;
        set
        {
            if (SetProperty(ref _window, value))
            {
                RefreshChart();
            }
        }
    }

    public SignalRowViewModel? SelectedSignal
    {
        get => _selectedSignal;
        set
        {
            if (SetProperty(ref _selectedSignal, value))
            {
                RemoveCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public IReadOnlyList<TrendSeries> Series
    {
        get => _series;
        private set => SetProperty(ref _series, value);
    }

    public DateTimeOffset From
    {
        get => _from;
        private set => SetProperty(ref _from, value);
    }

    public DateTimeOffset To
    {
        get => _to;
        private set => SetProperty(ref _to, value);
    }

    public string Status
    {
        get => _status;
        private set => SetProperty(ref _status, value);
    }

    public string Targets
    {
        get
        {
            IReadOnlyList<RobotRowViewModel> t = _targets();
            return t.Count switch
            {
                0 => "No robot selected",
                1 => t[0].Name,
                _ => string.Create(CultureInfo.CurrentCulture, $"{t.Count} robots: {string.Join(", ", t.Select(r => r.Name))}"),
            };
        }
    }

    public IRelayCommand AddCommand { get; }

    public IRelayCommand RemoveCommand { get; }

    public IAsyncRelayCommand ReadNowCommand { get; }

    public IAsyncRelayCommand StartCommand { get; }

    public IAsyncRelayCommand StopCommand { get; }

    public IAsyncRelayCommand StopAllCommand { get; }

    public IRelayCommand RefreshChartCommand { get; }

    public IRelayCommand ExportCommand { get; }

    /// <summary>Rebuilds the signal list for the robots now selected. Called by the main view model.</summary>
    public void Reload()
    {
        HashSet<string> unplotted = [.. Rows.Where(r => !r.IsPlotted).Select(r => r.SeriesName)];
        Rows.Clear();
        foreach (RobotRowViewModel robot in _targets())
        {
            foreach (TrendSignal signal in _store.TrendSignals(robot.Robot.Id))
            {
                var row = new SignalRowViewModel(signal, robot);
                row.IsPlotted = !unplotted.Contains(row.SeriesName);
                row.PropertyChanged += (_, e) =>
                {
                    if (e.PropertyName == nameof(SignalRowViewModel.IsPlotted))
                    {
                        RefreshChart();
                    }
                };
                Rows.Add(row);
            }
        }

        OnPropertyChanged(nameof(Targets));
        RaiseCanExecute();
        RefreshChart();
    }

    /// <summary>Re-reads the plotted series for the window from the database. Cheap: indexed range scans.</summary>
    public void RefreshChart()
    {
        DateTimeOffset to = DateTimeOffset.UtcNow;
        DateTimeOffset from = to - WindowSpan(Window);
        var series = new List<TrendSeries>();
        int colour = 0;
        foreach (SignalRowViewModel row in Rows.Where(r => r.IsPlotted))
        {
            IReadOnlyList<TrendPoint> points = _store.Samples(row.Signal.Id, from, to);
            series.Add(new TrendSeries(row.SeriesName, row.Signal.Address.IsDigital, colour++, points));
        }

        From = from;
        To = to;
        Series = series;
    }

    /// <summary>Exports the plotted series in the current window as CSV - one row per stored sample.</summary>
    public void ExportCsv()
    {
        string? path = PickSaveFile?.Invoke(string.Create(CultureInfo.InvariantCulture, $"RobControl trend {DateTime.Now:yyyy-MM-dd HHmm}.csv"));
        if (path is null)
        {
            return;
        }

        var csv = new StringBuilder("robot,signal,label,utc,local,value\r\n");
        foreach (SignalRowViewModel row in Rows.Where(r => r.IsPlotted))
        {
            foreach (TrendPoint point in _store.Samples(row.Signal.Id, From, To))
            {
                csv.Append(CultureInfo.InvariantCulture,
                    $"{Quote(row.RobotName)},{row.Address},{Quote(row.Label)},{point.Utc:O},{point.Utc.ToLocalTime():yyyy-MM-dd HH:mm:ss},{point.Value}\r\n");
            }
        }

        try
        {
            System.IO.File.WriteAllText(path, csv.ToString(), Encoding.UTF8);
            Status = $"Exported to {path}.";
        }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException)
        {
            ShowMessage?.Invoke($"Could not write {path}: {ex.Message}");
        }
    }

    public void Dispose() => _recorder.StatusChanged -= OnStatus;

    private void Add()
    {
        IReadOnlyList<SignalAddress> addresses = SignalAddress.ParseList(AddText, out IReadOnlyList<string> problems);
        if (problems.Count > 0)
        {
            ShowMessage?.Invoke("Nothing was added:\n\n" + string.Join("\n", problems.Take(10)));
            return;
        }

        IReadOnlyList<RobotRowViewModel> targets = _targets();
        foreach (RobotRowViewModel robot in targets)
        {
            _store.AddSignals(robot.Robot, addresses);
        }

        int variables = addresses.Count(a => a.Kind == SignalKind.SystemVariable);
        Status = string.Create(CultureInfo.CurrentCulture, $"Added {addresses.Count} signal(s) to {targets.Count} robot(s).")
            + (variables > 0 ? string.Create(CultureInfo.CurrentCulture, $" {variables} are system variables: one KCL request each per poll.") : string.Empty);
        AddText = string.Empty;
        Reload();
    }

    private void Remove()
    {
        if (SelectedSignal is not { } row)
        {
            return;
        }

        if (_recorder.IsRecording(row.Robot.Robot.Id))
        {
            ShowMessage?.Invoke($"{row.RobotName} is recording. Stop it first, then remove the signal.");
            return;
        }

        _store.RemoveSignal(row.Signal);
        Reload();
    }

    private async Task ReadNowAsync()
    {
        // A robot being recorded is already being read on its own loop; a second reader on it would
        // put two requests on one controller at once, which the Safety section rules out.
        List<IGrouping<RobotRowViewModel, SignalRowViewModel>> byRobot =
            [.. Rows.Where(r => !_recorder.IsRecording(r.Robot.Robot.Id)).GroupBy(r => r.Robot)];
        if (byRobot.Count == 0)
        {
            Status = "Every selected robot is recording - its values are already live.";
            return;
        }

        Status = string.Create(CultureInfo.CurrentCulture, $"Reading {Rows.Count} signal(s) on {byRobot.Count} robot(s)...");

        int answered = 0;
        await Task.WhenAll(byRobot.Select(async group =>
        {
            Robot robot = group.Key.Robot;
            SignalAddress[] addresses = [.. group.Select(r => r.Signal.Address)];
            SampleRead read = await Task.Run(async () =>
            {
                using var web = new Core.Transports.Http.ControllerWebClient(robot.Address, robot.HttpPort, timeout: TimeSpan.FromSeconds(10));
                return await RobotSampler.ReadAsync(web, addresses).ConfigureAwait(false);
            }).ConfigureAwait(true);

            foreach (SignalRowViewModel row in group)
            {
                row.Apply(read.Utc, read.Values, read.Problems);
            }

            if (read.AnyValue)
            {
                answered++;
            }

            _store.Info(EventCategory.Trend, robot, string.Create(CultureInfo.InvariantCulture,
                $"Read {read.Values.Count} of {addresses.Length} signals once."),
                read.Problems.Count == 0 ? null : string.Join("\n", read.Problems.Select(p => $"{p.Key}: {p.Value}")));
        })).ConfigureAwait(true);

        Status = string.Create(CultureInfo.CurrentCulture, $"Read at {DateTime.Now:HH:mm:ss}: {answered} of {byRobot.Count} robot(s) answered.");
    }

    private async Task StartAsync()
    {
        if (!double.TryParse(IntervalSeconds, NumberStyles.Float, CultureInfo.CurrentCulture, out double seconds) || seconds <= 0)
        {
            ShowMessage?.Invoke("The interval is a number of seconds, e.g. 5.");
            return;
        }

        TimeSpan interval = TimeSpan.FromSeconds(seconds);
        string floor = interval < TrendRecorder.MinimumInterval
            ? string.Create(CultureInfo.CurrentCulture, $" (raised to the {TrendRecorder.MinimumInterval.TotalSeconds:0} s minimum)")
            : string.Empty;

        int started = 0;
        foreach (IGrouping<RobotRowViewModel, SignalRowViewModel> group in Rows.GroupBy(r => r.Robot))
        {
            try
            {
                await _recorder.StartAsync(group.Key.Robot, [.. group.Select(r => r.Signal)], interval).ConfigureAwait(true);
                group.Key.Activity = string.Create(CultureInfo.CurrentCulture, $"Recording every {Math.Max(seconds, TrendRecorder.MinimumInterval.TotalSeconds):0.#} s");
                started++;
            }
            catch (Exception ex) when (ex is ArgumentException or RobControlException)
            {
                ShowMessage?.Invoke($"{group.Key.Name}: {ex.Message}");
            }
        }

        Status = string.Create(CultureInfo.CurrentCulture, $"Recording {started} robot(s){floor}. Values update below; the chart refreshes every few seconds.");
        RaiseCanExecute();
    }

    private async Task StopAsync()
    {
        foreach (RobotRowViewModel robot in _targets())
        {
            await _recorder.StopAsync(robot.Robot.Id).ConfigureAwait(true);
            robot.Activity = null;
        }

        Status = "Stopped recording the selected robot(s).";
        RaiseCanExecute();
    }

    private void OnStatus(object? sender, RecorderStatus status) => _ui.Post(() =>
    {
        foreach (SignalRowViewModel row in Rows.Where(r => r.Robot.Robot.Id == status.RobotId))
        {
            if (status.LastPollUtc is { } utc)
            {
                row.Apply(utc, status.LastValues, status.Problems);
            }
        }

        if (!status.Running)
        {
            RaiseCanExecute();
        }
    });

    private void RaiseCanExecute()
    {
        AddCommand.NotifyCanExecuteChanged();
        RemoveCommand.NotifyCanExecuteChanged();
        ReadNowCommand.NotifyCanExecuteChanged();
        StartCommand.NotifyCanExecuteChanged();
        StopCommand.NotifyCanExecuteChanged();
        StopAllCommand.NotifyCanExecuteChanged();
        ExportCommand.NotifyCanExecuteChanged();
    }

    private static TimeSpan WindowSpan(string window) => window switch
    {
        "5 min" => TimeSpan.FromMinutes(5),
        "1 h" => TimeSpan.FromHours(1),
        "8 h" => TimeSpan.FromHours(8),
        "24 h" => TimeSpan.FromDays(1),
        "7 days" => TimeSpan.FromDays(7),
        _ => TimeSpan.FromMinutes(30),
    };

    private static string Quote(string text) =>
        text.Contains(',', StringComparison.Ordinal) || text.Contains('"', StringComparison.Ordinal)
            ? "\"" + text.Replace("\"", "\"\"", StringComparison.Ordinal) + "\""
            : text;
}
