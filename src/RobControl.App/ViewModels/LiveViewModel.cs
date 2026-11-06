using System.Collections.ObjectModel;
using System.Globalization;
using System.Net.Http;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RobControl.App.Composition;
using RobControl.Core;
using RobControl.Core.Events;
using RobControl.Core.Insight;
using RobControl.Core.Live;
using RobControl.Core.Transports.Http;

namespace RobControl.App.ViewModels;

/// <summary>One I/O point on the Live tab.</summary>
public sealed record LiveIoRow(string Name, string Value);

/// <summary>
/// The Live tab: what the selected robot says right now - active alarms, recent alarm history,
/// I/O, current position, program state - from its own diagnostic pages over HTTP GET.
///
/// <para><b>Read-only and gentle.</b> One read at a time, never more often than
/// <see cref="LiveReader.MinimumInterval"/>, and never on a robot that is being backed up, probed or
/// recorded - those already have their one connection. Auto-refresh stops when the selection changes,
/// so it never follows the person to another robot by itself.</para>
/// </summary>
public sealed class LiveViewModel : ObservableObject, IDisposable
{
    private readonly IUiDispatcher _ui;
    private readonly IEventSink _events;
    private readonly Func<RobotRowViewModel?> _selected;
    private readonly Func<long, bool> _isRecording;
    private readonly CancellationTokenSource _shutdown = new();
    private CancellationTokenSource? _auto;
    private RobotRowViewModel? _robot;
    private bool _reading;
    private bool _autoRefresh;
    private int _intervalSeconds = 10;
    private string _status = "Select a robot and press Read now. Reads only - nothing is sent but GET.";
    private string _filter = string.Empty;
    private LiveSnapshot? _snapshot;

    public LiveViewModel(IUiDispatcher ui, IEventSink events, Func<RobotRowViewModel?> selected, Func<long, bool> isRecording)
    {
        _ui = ui ?? throw new ArgumentNullException(nameof(ui));
        _events = events ?? throw new ArgumentNullException(nameof(events));
        _selected = selected ?? throw new ArgumentNullException(nameof(selected));
        _isRecording = isRecording ?? throw new ArgumentNullException(nameof(isRecording));
        ReadNowCommand = new AsyncRelayCommand(ReadNowAsync, CanRead);
    }

    public IAsyncRelayCommand ReadNowCommand { get; }

    public ObservableCollection<AlarmEntry> ActiveAlarms { get; } = [];

    public ObservableCollection<AlarmEntry> RecentAlarms { get; } = [];

    public ObservableCollection<LiveIoRow> Io { get; } = [];

    public ObservableCollection<LiveFile> Pages { get; } = [];

    public string ProgramState => Page("PRGSTATE.DG");

    public string Position => Page("CURPOS.DG");

    public string Status
    {
        get => _status;
        private set => SetProperty(ref _status, value);
    }

    /// <summary>Shows only I/O whose name contains this: "DO", "UI[".</summary>
    public string Filter
    {
        get => _filter;
        set
        {
            if (SetProperty(ref _filter, value ?? string.Empty))
            {
                FillIo();
            }
        }
    }

    public bool AutoRefresh
    {
        get => _autoRefresh;
        set
        {
            if (SetProperty(ref _autoRefresh, value))
            {
                if (value)
                {
                    StartAuto();
                }
                else
                {
                    StopAuto();
                }
            }
        }
    }

    /// <summary>Seconds between automatic reads. Never under <see cref="LiveReader.MinimumInterval"/>.</summary>
    public int IntervalSeconds
    {
        get => _intervalSeconds;
        set
        {
            int clamped = Math.Clamp(value, (int)LiveReader.MinimumInterval.TotalSeconds, 600);
            if (SetProperty(ref _intervalSeconds, clamped) && AutoRefresh)
            {
                StartAuto();
            }
        }
    }

    /// <summary>The selection moved. Whatever was shown belonged to the last robot, so it goes, and so does auto-refresh.</summary>
    public void SelectionChanged()
    {
        RobotRowViewModel? now = _selected();
        if (ReferenceEquals(now, _robot))
        {
            return;
        }

        _robot = now;
        AutoRefresh = false;
        Show(null);
        Status = now is null ? "Select a robot." : $"{now.Name}: press Read now.";
        ReadNowCommand.NotifyCanExecuteChanged();
    }

    public void RaiseCanExecute() => ReadNowCommand.NotifyCanExecuteChanged();

    public void Dispose()
    {
        StopAuto();
        _shutdown.Cancel();
        _shutdown.Dispose();
    }

    private bool CanRead() => !_reading && _selected() is { IsBusy: false } row && !_isRecording(row.Robot.Id);

    private async Task ReadNowAsync()
    {
        if (_selected() is not { } row)
        {
            return;
        }

        if (row.IsBusy || _isRecording(row.Robot.Id))
        {
            Status = $"{row.Name} is busy (backup, probe or trend recording) - Live waits so it never opens a second connection.";
            return;
        }

        _reading = true;
        ReadNowCommand.NotifyCanExecuteChanged();
        Status = $"Reading {row.Name}...";
        try
        {
            using var web = new ControllerWebClient(row.Robot.Address, row.Robot.HttpPort, timeout: TimeSpan.FromSeconds(10));
            LiveSnapshot snapshot = await Task.Run(() => LiveReader.ReadAsync(row.Robot, web, _events, _shutdown.Token), _shutdown.Token)
                .ConfigureAwait(true);

            if (!ReferenceEquals(_selected(), row))
            {
                return;
            }

            Show(snapshot);
            Status = string.Create(CultureInfo.CurrentCulture, $"{row.Name} at {snapshot.Utc.ToLocalTime():HH:mm:ss}: {snapshot.Summary}");
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex) when (ex is RobControlException or HttpRequestException)
        {
            Status = $"{row.Name}: {ex.Message}";
        }
        finally
        {
            _reading = false;
            ReadNowCommand.NotifyCanExecuteChanged();
        }
    }

    private void Show(LiveSnapshot? snapshot)
    {
        _snapshot = snapshot;
        Replace(ActiveAlarms, snapshot?.ActiveAlarms ?? []);
        Replace(RecentAlarms, snapshot?.RecentAlarms ?? []);
        Replace(Pages, snapshot?.Files ?? []);
        FillIo();
        OnPropertyChanged(nameof(ProgramState));
        OnPropertyChanged(nameof(Position));
    }

    private void FillIo()
    {
        IEnumerable<KeyValuePair<string, double>> points = _snapshot?.Io ?? new Dictionary<string, double>();
        string filter = _filter.Trim();
        Replace(Io, [.. points
            .Where(p => filter.Length == 0 || p.Key.Contains(filter, StringComparison.OrdinalIgnoreCase))
            .OrderBy(p => p.Key.Split('[')[0], StringComparer.Ordinal)
            .ThenBy(p => int.TryParse(p.Key.Split('[', ']')[1], NumberStyles.None, CultureInfo.InvariantCulture, out int n) ? n : 0)
            .Select(p => new LiveIoRow(p.Key, Describe(p.Key, p.Value)))]);
    }

    private static string Describe(string name, double value) =>
        name.StartsWith('G') || name.StartsWith('A')
            ? value.ToString(CultureInfo.CurrentCulture)
            : value != 0 ? "ON" : "OFF";

    private string Page(string file) => _snapshot?.File(file) switch
    {
        null => string.Empty,
        { IsOk: true, Text: var text } => text.Trim(),
        { Message: var message } => message ?? string.Empty,
    };

    private void StartAuto()
    {
        StopAuto();
        var cts = CancellationTokenSource.CreateLinkedTokenSource(_shutdown.Token);
        _auto = cts;
        CancellationToken token = cts.Token;
        TimeSpan every = TimeSpan.FromSeconds(Math.Max(IntervalSeconds, LiveReader.MinimumInterval.TotalSeconds));
        _ = Task.Run(async () =>
        {
            using var timer = new PeriodicTimer(every);
            try
            {
                _ui.Post(() => _ = ReadIfIdleAsync());
                while (await timer.WaitForNextTickAsync(token).ConfigureAwait(false))
                {
                    _ui.Post(() => _ = ReadIfIdleAsync());
                }
            }
            catch (OperationCanceledException)
            {
            }
        });
    }

    private Task ReadIfIdleAsync() => _reading || _auto is null || _auto.IsCancellationRequested ? Task.CompletedTask : ReadNowAsync();

    private void StopAuto()
    {
        _auto?.Cancel();
        _auto?.Dispose();
        _auto = null;
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
