using System.Collections.Concurrent;
using System.Globalization;
using RobControl.Core.Events;
using RobControl.Core.Robots;
using RobControl.Core.Transports.Http;

namespace RobControl.Core.Trending;

/// <summary>
/// Polls signals on any number of robots at once and stores what changed.
///
/// <para><b>One loop per robot, one request at a time on it.</b> Robots are independent of each
/// other, so recording ten robots is ten loops - but each loop waits for its previous poll to finish,
/// and <see cref="ControllerWebClient"/> serialises requests per robot, so no controller ever has
/// two requests from RobControl in flight (CLAUDE.md, Safety).</para>
///
/// <para><b>Gentle by default.</b> Never faster than <see cref="MinimumInterval"/>. A robot that
/// stops answering is backed off - the interval doubles up to <see cref="MaxBackoff"/> - and comes
/// back to its normal rate on the first good read.</para>
///
/// <para><b>Stores changes, not every poll.</b> A value is stored when it differs from the last
/// stored value, and at least every <see cref="Heartbeat"/> regardless, so a flat line is still
/// provably flat. A register polled every 5 s that changes twice an hour costs ~60 rows an hour, not
/// 720.</para>
///
/// <para>Every start and stop writes an event row naming the robot and the signals; polls are
/// summarised in the stop row (count, failures, samples) rather than logged one by one.</para>
/// </summary>
public sealed class TrendRecorder : IAsyncDisposable
{
    public static readonly TimeSpan MinimumInterval = TimeSpan.FromSeconds(2);
    public static readonly TimeSpan DefaultInterval = TimeSpan.FromSeconds(5);
    public static readonly TimeSpan Heartbeat = TimeSpan.FromSeconds(60);
    public static readonly TimeSpan MaxBackoff = TimeSpan.FromSeconds(60);

    private readonly ITrendStore _store;
    private readonly IEventSink _events;
    private readonly Func<Robot, ControllerWebClient> _webFactory;
    private readonly TimeProvider _time;
    private readonly ConcurrentDictionary<long, Session> _sessions = new();

    public TrendRecorder(ITrendStore store, IEventSink? events = null, Func<Robot, ControllerWebClient>? webFactory = null, TimeProvider? time = null)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _events = events ?? NullEventSink.Instance;
        _webFactory = webFactory ?? (r => new ControllerWebClient(r.Address, r.HttpPort, timeout: TimeSpan.FromSeconds(10)));
        _time = time ?? TimeProvider.System;
    }

    /// <summary>Raised after every poll of every robot, on a worker thread.</summary>
    public event EventHandler<RecorderStatus>? StatusChanged;

    public IReadOnlyCollection<long> Recording => [.. _sessions.Keys];

    public bool IsRecording(long robotId) => _sessions.ContainsKey(robotId);

    /// <summary>
    /// The floor actually applied. <see cref="MinimumInterval"/> in the product; the tests lower it so
    /// they do not spend seconds waiting. Internal on purpose - nothing a user can reach lowers it.
    /// </summary>
    internal TimeSpan Floor { get; init; } = MinimumInterval;

    /// <summary>Lowered with <see cref="Floor"/> by the tests.</summary>
    internal TimeSpan HeartbeatEvery { get; init; } = Heartbeat;

    /// <summary>
    /// Starts recording <paramref name="signals"/> on <paramref name="robot"/>, replacing whatever was
    /// being recorded on it. The interval is raised to <see cref="MinimumInterval"/> if lower.
    /// </summary>
    public async Task StartAsync(Robot robot, IReadOnlyList<TrendSignal> signals, TimeSpan? interval = null)
    {
        ArgumentNullException.ThrowIfNull(robot);
        ArgumentNullException.ThrowIfNull(signals);
        if (signals.Count == 0)
        {
            throw new ArgumentException("Nothing to record - add signals to this robot first.", nameof(signals));
        }

        await StopAsync(robot.Id).ConfigureAwait(false);

        TimeSpan every = interval ?? DefaultInterval;
        if (every < Floor)
        {
            every = Floor;
        }
        var session = new Session(this, robot, [.. signals], every);
        if (_sessions.TryAdd(robot.Id, session))
        {
            _events.Info(EventCategory.Trend, robot,
                string.Create(CultureInfo.InvariantCulture, $"Recording started: {signals.Count} signals every {every.TotalSeconds:0.#} s."),
                string.Join(", ", signals.Select(s => s.Address.Text)));
            session.Start();
        }
    }

    public async Task StopAsync(long robotId)
    {
        if (_sessions.TryRemove(robotId, out Session? session))
        {
            await session.StopAsync().ConfigureAwait(false);
        }
    }

    public async Task StopAllAsync()
    {
        foreach (long id in _sessions.Keys.ToList())
        {
            await StopAsync(id).ConfigureAwait(false);
        }
    }

    public async ValueTask DisposeAsync() => await StopAllAsync().ConfigureAwait(false);

    private void Raise(RecorderStatus status) => StatusChanged?.Invoke(this, status);

    private sealed class Session(TrendRecorder owner, Robot robot, List<TrendSignal> signals, TimeSpan interval)
    {
        private readonly CancellationTokenSource _stop = new();
        private readonly Dictionary<long, (double Value, DateTimeOffset Utc)> _lastStored = [];
        private Task? _loop;
        private long _polls;
        private long _failedPolls;
        private long _stored;

        public void Start() => _loop = Task.Run(RunAsync);

        public async Task StopAsync()
        {
            await _stop.CancelAsync().ConfigureAwait(false);
            if (_loop is not null)
            {
                try
                {
                    await _loop.ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                }
            }

            _stop.Dispose();
            owner._events.Info(EventCategory.Trend, robot, string.Create(CultureInfo.InvariantCulture,
                $"Recording stopped: {_polls} polls, {_failedPolls} without an answer, {_stored} samples stored."));
            owner.Raise(new RecorderStatus(robot.Id, false, null, new Dictionary<string, double>(), new Dictionary<string, string>(), _stored, interval, "Stopped."));
        }

        private async Task RunAsync()
        {
            CancellationToken token = _stop.Token;
            using ControllerWebClient web = owner._webFactory(robot);
            SignalAddress[] addresses = [.. signals.Select(s => s.Address).DistinctBy(a => a.Text)];
            TimeSpan current = interval;

            while (!token.IsCancellationRequested)
            {
                DateTimeOffset started = owner._time.GetUtcNow();
                SampleRead read;
                try
                {
                    read = await RobotSampler.ReadAsync(web, addresses, token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                    return;
                }

                _polls++;
                string message;
                if (read.AnyValue)
                {
                    current = interval;
                    int stored = Store(read);
                    message = read.Problems.Count == 0
                        ? string.Create(CultureInfo.InvariantCulture, $"OK - {read.Values.Count} values, {stored} stored.")
                        : string.Create(CultureInfo.InvariantCulture, $"{read.Values.Count} values, {read.Problems.Count} not read.");
                }
                else
                {
                    _failedPolls++;
                    current = TimeSpan.FromTicks(Math.Min(current.Ticks * 2, MaxBackoff.Ticks));
                    message = string.Create(CultureInfo.InvariantCulture,
                        $"No values: {read.Problems.Values.FirstOrDefault() ?? "nothing answered"}. Next try in {current.TotalSeconds:0} s.");
                }

                owner.Raise(new RecorderStatus(robot.Id, true, read.Utc, read.Values, read.Problems, _stored, current, message));

                TimeSpan wait = current - (owner._time.GetUtcNow() - started);
                if (wait > TimeSpan.Zero)
                {
                    try
                    {
                        await Task.Delay(wait, owner._time, token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        return;
                    }
                }
            }
        }

        private int Store(SampleRead read)
        {
            var batch = new List<TrendSample>();
            foreach (TrendSignal signal in signals)
            {
                if (!read.Values.TryGetValue(signal.Address.Text, out double value))
                {
                    continue;
                }

                bool due = !_lastStored.TryGetValue(signal.Id, out (double Value, DateTimeOffset Utc) last)
                    || !last.Value.Equals(value)
                    || read.Utc - last.Utc >= owner.HeartbeatEvery;

                if (due)
                {
                    batch.Add(new TrendSample(signal.Id, read.Utc, value));
                }
            }

            if (batch.Count > 0 && owner._store.Append(batch))
            {
                foreach (TrendSample sample in batch)
                {
                    _lastStored[sample.SignalId] = (sample.Value, sample.Utc);
                }

                _stored += batch.Count;
            }

            return batch.Count;
        }
    }
}
