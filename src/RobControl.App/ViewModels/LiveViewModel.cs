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
using RobControl.Core.Trending;
using RobControl.Core.Transports.Http;

namespace RobControl.App.ViewModels;

/// <summary>
/// The Live tab: what the selected robot says right now - active alarms, recent alarm history,
/// I/O (every type on the page, safe I/O included, plus the stop signals), tasks and programs,
/// current position - from its own diagnostic pages over HTTP GET, laid out as tables that can be
/// filtered. The Pages view keeps each page's text for whatever the tables do not show.
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
    private string _ioCategory = AllCategories;
    private bool _onlyOn;
    private bool _namedOnly;
    private string _ioCount = string.Empty;
    private IReadOnlyList<LiveIoRow> _io = [];
    private IReadOnlyList<LiveIoRow> _allIo = [];
    private IReadOnlyList<string> _ioCategories = [AllCategories];
    private string _programFilter = string.Empty;
    private bool _runningOnly;
    private IReadOnlyList<LiveTaskRow> _tasks = [];
    private IReadOnlyList<LiveProgramRow> _programs = [];
    private string _programCount = string.Empty;
    private IReadOnlyList<LivePositionRow> _joints = [];
    private IReadOnlyList<LiveCartesianRow> _cartesian = [];
    private string _positionNote = string.Empty;
    private LiveFile? _selectedPage;
    private LiveSnapshot? _snapshot;

    /// <summary>The I/O category filter's "everything" entry.</summary>
    public const string AllCategories = "All types";

    /// <summary>The category stop signals (SFTYSIG.DG) are listed under.</summary>
    public const string StopSignals = "Stop signals";

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

    public ObservableCollection<LiveFile> Pages { get; } = [];

    /// <summary>The I/O shown: every point the page listed, through the filters below. Replaced whole, not edited - there can be 5,000.</summary>
    public IReadOnlyList<LiveIoRow> Io
    {
        get => _io;
        private set => SetProperty(ref _io, value);
    }

    /// <summary>"All types" and each category the last read had, in the pendant's order.</summary>
    public IReadOnlyList<string> IoCategories
    {
        get => _ioCategories;
        private set => SetProperty(ref _ioCategories, value);
    }

    public string IoCategory
    {
        get => _ioCategory;
        set
        {
            if (SetProperty(ref _ioCategory, value ?? AllCategories))
            {
                FillIo();
            }
        }
    }

    /// <summary>Only points that are ON (or TRUE, or a non-zero group value).</summary>
    public bool OnlyOn
    {
        get => _onlyOn;
        set
        {
            if (SetProperty(ref _onlyOn, value))
            {
                FillIo();
            }
        }
    }

    /// <summary>Only points with a comment - hides the hundreds of unused, unnamed ones.</summary>
    public bool NamedOnly
    {
        get => _namedOnly;
        set
        {
            if (SetProperty(ref _namedOnly, value))
            {
                FillIo();
            }
        }
    }

    /// <summary>"Showing 37 of 5,183 points".</summary>
    public string IoCount
    {
        get => _ioCount;
        private set => SetProperty(ref _ioCount, value);
    }

    /// <summary>Matches task and program names, comments, status, type, where a task is and source file.</summary>
    public string ProgramFilter
    {
        get => _programFilter;
        set
        {
            if (SetProperty(ref _programFilter, value ?? string.Empty))
            {
                FillPrograms();
            }
        }
    }

    /// <summary>Only running tasks, and only the programs that are running as tasks.</summary>
    public bool RunningOnly
    {
        get => _runningOnly;
        set
        {
            if (SetProperty(ref _runningOnly, value))
            {
                FillPrograms();
            }
        }
    }

    public IReadOnlyList<LiveTaskRow> Tasks
    {
        get => _tasks;
        private set => SetProperty(ref _tasks, value);
    }

    public IReadOnlyList<LiveProgramRow> Programs
    {
        get => _programs;
        private set => SetProperty(ref _programs, value);
    }

    /// <summary>"16 tasks (8 running), 212 programs".</summary>
    public string ProgramCount
    {
        get => _programCount;
        private set => SetProperty(ref _programCount, value);
    }

    public IReadOnlyList<LivePositionRow> Joints
    {
        get => _joints;
        private set => SetProperty(ref _joints, value);
    }

    public IReadOnlyList<LiveCartesianRow> Cartesian
    {
        get => _cartesian;
        private set => SetProperty(ref _cartesian, value);
    }

    /// <summary>"Group 1: user frame 0, tool 1, config N U T, 0, 0, 0".</summary>
    public string PositionNote
    {
        get => _positionNote;
        private set => SetProperty(ref _positionNote, value);
    }

    /// <summary>The page picked on the Pages view; its text shows under the list.</summary>
    public LiveFile? SelectedPage
    {
        get => _selectedPage;
        set
        {
            if (SetProperty(ref _selectedPage, value))
            {
                OnPropertyChanged(nameof(SelectedPageText));
            }
        }
    }

    public string SelectedPageText => _selectedPage switch
    {
        null => "Pick a page above to see its text.",
        { IsOk: true, Text: var text } => text.Trim(),
        { Message: var message, Text: var text } => string.Join("\n\n", new[] { message, text.Trim() }.Where(t => !string.IsNullOrEmpty(t))),
    };

    public string Status
    {
        get => _status;
        private set => SetProperty(ref _status, value);
    }

    /// <summary>Shows only I/O whose name or comment contains this: "DO[", "UI[", "gun", "estop".</summary>
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
            // IOSTATE.DG on a full controller is ~130 KB that the controller writes on request - it
            // can take several seconds, so the timeout is per page and generous.
            using var web = new ControllerWebClient(row.Robot.Address, row.Robot.HttpPort, timeout: TimeSpan.FromSeconds(45));
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

        string? selected = SelectedPage?.FileName;
        Replace(Pages, snapshot?.Files ?? []);
        SelectedPage = selected is null ? null : Pages.FirstOrDefault(p => p.FileName == selected);
        OnPropertyChanged(nameof(SelectedPageText));

        _allIo = IoRows(snapshot);
        IoCategories = [AllCategories, .. _allIo.Select(r => r.Category).Distinct()];
        if (!IoCategories.Contains(_ioCategory))
        {
            _ioCategory = AllCategories;
            OnPropertyChanged(nameof(IoCategory));
        }

        FillIo();
        FillPrograms();
        FillPosition(snapshot);
    }

    /// <summary>Stop signals first (few, and what someone looking at a stopped robot wants), then the I/O page in category order.</summary>
    private static List<LiveIoRow> IoRows(LiveSnapshot? snapshot)
    {
        if (snapshot is null)
        {
            return [];
        }

        var rows = new List<LiveIoRow>(snapshot.IoPoints.Count + snapshot.SafetySignals.Count);
        rows.AddRange(snapshot.SafetySignals.Select(s => new LiveIoRow(
            StopSignals, "Input", s.Name, s.IsOn ? "TRUE" : "FALSE", string.Empty, s.IsOn, LiveReader.SafetyFile)));

        List<string> order = [.. IoStateParser.Categories];
        rows.AddRange(snapshot.IoPoints
            .Select((p, i) => (Point: p, At: i))
            .OrderBy(x => order.IndexOf(x.Point.Category) is >= 0 and var c ? c : order.Count)
            .ThenBy(x => x.Point.Type, StringComparer.Ordinal)
            .ThenBy(x => x.Point.Index)
            .Select(x => new LiveIoRow(
                x.Point.Category,
                x.Point.Direction,
                x.Point.Key,
                x.Point.State,
                x.Point.Comment,
                x.Point.IsDigital ? x.Point.IsOn : x.Point.Value != 0,
                $"{x.Point.Listed}[{x.Point.Index.ToString(CultureInfo.InvariantCulture)}]")));
        return rows;
    }

    private void FillIo()
    {
        string filter = _filter.Trim();
        List<LiveIoRow> shown =
        [
            .. _allIo.Where(r =>
                (_ioCategory == AllCategories || r.Category == _ioCategory)
                && (!_onlyOn || r.IsOn)
                && (!_namedOnly || r.Comment.Length > 0 || r.Category == StopSignals)
                && (filter.Length == 0
                    || r.Name.Contains(filter, StringComparison.OrdinalIgnoreCase)
                    || r.Listed.Contains(filter, StringComparison.OrdinalIgnoreCase)
                    || r.Comment.Contains(filter, StringComparison.OrdinalIgnoreCase))),
        ];
        Io = shown;
        IoCount = _allIo.Count == 0
            ? (_snapshot is null ? string.Empty : NoIoReason())
            : string.Create(CultureInfo.CurrentCulture, $"Showing {shown.Count:N0} of {_allIo.Count:N0}");
    }

    private string NoIoReason() => _snapshot?.File(RobotSampler.IoFile) switch
    {
        null => string.Empty,
        { IsOk: true } => "IOSTATE.DG arrived but no I/O was recognised in it - see its text on the Pages view.",
        { Message: var message } => "IOSTATE.DG was not read: " + message,
    };

    private void FillPrograms()
    {
        ProgramStateReport report = _snapshot?.Programs ?? ProgramStateReport.Empty;
        string filter = _programFilter.Trim();
        var status = report.Tasks
            .GroupBy(t => t.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First().Status, StringComparer.OrdinalIgnoreCase);

        Tasks =
        [
            .. report.Tasks
                .Where(t => !_runningOnly || t.IsRunning)
                .Select(t => new LiveTaskRow(t.Number, t.Name, Title(t.Status), t.Where, t.CallStack, t.IsRunning))
                .Where(t => Matches(filter, t.Name, t.Status, t.Where, t.CallStack)),
        ];

        Programs =
        [
            .. report.Programs
                .Select(p =>
                {
                    string running = p.IsTask && status.TryGetValue(p.Name, out string? s) ? Title(s) : p.IsTask ? "Task" : string.Empty;
                    return new LiveProgramRow(
                        p.Name,
                        p.Type,
                        p.Comment,
                        running,
                        p.Lines,
                        p.SizeBytes,
                        p.ModifiedTime,
                        p.ModifiedTime is { } when ? when.ToString("g", CultureInfo.CurrentCulture) : p.Modified ?? string.Empty,
                        p.SourceFile ?? string.Empty,
                        p.WriteProtected switch { true => "Protected", false => string.Empty, null => string.Empty },
                        string.Join("\n", p.Fields.Select(f => $"{f.Key}: {f.Value}")));
                })
                .Where(p => !_runningOnly || p.Status.Length > 0)
                .Where(p => Matches(filter, p.Name, p.Type, p.Comment, p.Status, p.Source)),
        ];

        int running = report.Tasks.Count(t => t.IsRunning);
        string loaded = string.Create(CultureInfo.CurrentCulture, $"{report.Tasks.Count} task(s), {running} running - {report.Programs.Count:N0} program(s) loaded");
        string showing = filter.Length > 0 || _runningOnly
            ? string.Create(CultureInfo.CurrentCulture, $"; showing {Tasks.Count} task(s), {Programs.Count:N0} program(s)")
            : string.Empty;
        ProgramCount = report.IsEmpty ? NoPageReason("PRGSTATE.DG", "no tasks or programs were recognised in it") : loaded + showing;
    }

    private void FillPosition(LiveSnapshot? snapshot)
    {
        IReadOnlyList<PositionFrame> frames = snapshot?.Position ?? [];
        bool groups = frames.Select(f => f.Group).Distinct().Count() > 1;
        string G(int group) => groups ? group.ToString(CultureInfo.InvariantCulture) : string.Empty;

        Joints =
        [
            .. frames.Where(f => f.IsJoint).SelectMany(f => f.Axes.Select(a =>
                new LivePositionRow(G(f.Group), a.Axis, Number(a.Value), a.Axis.StartsWith('J') ? "deg" : string.Empty))),
        ];

        var cartesian = new List<LiveCartesianRow>();
        var notes = new List<string>();
        foreach (IGrouping<int, PositionFrame> group in frames.Where(f => !f.IsJoint).GroupBy(f => f.Group))
        {
            PositionFrame? user = group.FirstOrDefault(f => f.Kind == PositionParser.UserFrame);
            PositionFrame? world = group.FirstOrDefault(f => f.Kind == PositionParser.World);
            IEnumerable<string> axes = group.SelectMany(f => f.Axes.Select(a => a.Axis)).Distinct();
            foreach (string axis in axes)
            {
                cartesian.Add(new LiveCartesianRow(
                    G(group.Key),
                    axis,
                    user?[axis] is { } u ? Number(u) : string.Empty,
                    world?[axis] is { } w ? Number(w) : string.Empty,
                    axis is "X" or "Y" or "Z" ? "mm" : axis is "W" or "P" or "R" ? "deg" : string.Empty));
            }

            PositionFrame? any = user ?? world;
            var parts = new List<string>();
            if (user?.UserFrame is { } uf)
            {
                parts.Add($"user frame {uf}");
            }

            if (any?.Tool is { } tool)
            {
                parts.Add($"tool {tool}");
            }

            if (any?.Config is { Length: > 0 } config)
            {
                parts.Add($"config {config}");
            }

            if (parts.Count > 0)
            {
                notes.Add($"Group {group.Key}: {string.Join(", ", parts)}");
            }
        }

        Cartesian = cartesian;
        PositionNote = frames.Count == 0
            ? NoPageReason("CURPOS.DG", "no position was recognised in it")
            : string.Join("   ", notes);
    }

    private string NoPageReason(string file, string whenRead) => _snapshot?.File(file) switch
    {
        null => string.Empty,
        { IsOk: true } => $"{file} arrived but {whenRead} - see its text on the Pages view.",
        { Message: var message } => $"{file} was not read: {message}",
    };

    private static bool Matches(string filter, params string[] fields) =>
        filter.Length == 0 || fields.Any(f => f.Contains(filter, StringComparison.OrdinalIgnoreCase));

    /// <summary>RUNNING -> Running.</summary>
    private static string Title(string status) =>
        status.Length == 0 ? status : status[0] + status[1..].ToLowerInvariant();

    private static string Number(double value) => value.ToString("0.00#", CultureInfo.CurrentCulture);

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
