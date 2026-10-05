using System.Net;
using System.Text;
using RobControl.Core.Events;
using RobControl.Core.Robots;
using RobControl.Core.Transports.Http;
using RobControl.Core.Trending;
using RobControl.RobotSim;
using Xunit;

namespace RobControl.Tests;

public class TrendingTests
{
    private static SignalAddress[] Addresses(params string[] text) => [.. text.Select(SignalAddress.Parse)];

    [Fact]
    public async Task OneReadCoversRegistersIoAndVariablesWithFewRequests()
    {
        await using SimRig rig = SimRig.Start(Fixtures.Profile(Fixtures.Folder(Fixtures.Synthetic)));
        using var web = new ControllerWebClient(IPAddress.Loopback, rig.Sim.HttpPort);

        SampleRead read = await RobotSampler.ReadAsync(web, Addresses("R[1]", "R[2]", "R[3]", "DI[1]", "DO[1]", "$NUMREG[2]"));

        Assert.Equal(1287, read.Values["R[2]"]);
        Assert.Equal(1, read.Values["DI[1]"]);
        Assert.Equal(1287, read.Values["$NUMREG[2]"]);
        Assert.Empty(read.Problems);

        // One file for registers, one for I/O, one KCL per variable - and all of them GET.
        Assert.Equal(3, rig.Sim.HttpRequests.Count);
        Assert.All(rig.Sim.HttpRequests, r => Assert.StartsWith("GET ", r, StringComparison.Ordinal));
    }

    [Fact]
    public async Task AMissingSignalIsAProblemNotAFailure()
    {
        await using SimRig rig = SimRig.Start(Fixtures.Profile(Fixtures.Folder(Fixtures.Synthetic)));
        using var web = new ControllerWebClient(IPAddress.Loopback, rig.Sim.HttpPort);

        SampleRead read = await RobotSampler.ReadAsync(web, Addresses("R[2]", "R[150]", "DI[99]"));

        Assert.Equal(1287, read.Values["R[2]"]);
        Assert.Contains("not in NUMREG.VA", read.Problems["R[150]"], StringComparison.Ordinal);
        Assert.True(read.Problems.ContainsKey("DI[99]"));
    }

    [Fact]
    public async Task LockedKclStillLetsRegistersThrough()
    {
        await using SimRig rig = SimRig.Start(Fixtures.Profile(Fixtures.Folder(Fixtures.Synthetic), p => p with { KclLocked = true }));
        using var web = new ControllerWebClient(IPAddress.Loopback, rig.Sim.HttpPort);

        SampleRead read = await RobotSampler.ReadAsync(web, Addresses("R[2]", "$SCR.$NUM_GROUP", "$VERSION"));

        Assert.Equal(1287, read.Values["R[2]"]);
        Assert.Contains("locked", read.Problems["$SCR.$NUM_GROUP"], StringComparison.Ordinal);
        Assert.Contains("locked", read.Problems["$VERSION"], StringComparison.Ordinal);
        Assert.Single(rig.Sim.HttpRequests, r => r.Contains("/KCL/", StringComparison.Ordinal));
    }

    [Fact]
    public async Task TheRecorderStoresChangesAndHeartbeatsOnly()
    {
        await using SimRig rig = SimRig.Start(Fixtures.Profile(Fixtures.Folder(Fixtures.Synthetic)));
        var store = new ListStore();
        TrendSignal r1 = new(1, rig.Robot.Id, SignalAddress.Parse("R[1]"), "Counter");
        TrendSignal flat = new(2, rig.Robot.Id, SignalAddress.Parse("R[2]"), null);
        SetRegisters(rig.Sim, 0);

        await using var recorder = new TrendRecorder(store, rig.Sink) { Floor = TimeSpan.FromMilliseconds(40), HeartbeatEvery = TimeSpan.FromHours(1) };
        await recorder.StartAsync(rig.Robot, [r1, flat], TimeSpan.FromMilliseconds(40));

        await WaitFor(() => store.Count(1) >= 1);
        SetRegisters(rig.Sim, 1);
        await WaitFor(() => store.Count(1) >= 2);
        SetRegisters(rig.Sim, 2);
        await WaitFor(() => store.Count(1) >= 3);
        await Task.Delay(300);
        await recorder.StopAsync(rig.Robot.Id);

        Assert.Equal([0, 1, 2], store.Values(1));
        Assert.Equal([1287], store.Values(2)); // never changed, heartbeat an hour away: stored once
        Assert.Contains(rig.Events, e => e.Category == EventCategory.Trend && e.Message.StartsWith("Recording started", StringComparison.Ordinal));
        Assert.Contains(rig.Events, e => e.Category == EventCategory.Trend && e.Message.StartsWith("Recording stopped", StringComparison.Ordinal));
        Assert.Empty(rig.Refused);
    }

    [Fact]
    public async Task SeveralRobotsRecordAtOnceIndependently()
    {
        await using SimRig a = SimRig.Start(Fixtures.Profile(Fixtures.Folder(Fixtures.Synthetic)), r => r with { Id = 1 });
        await using SimRig b = SimRig.Start(Fixtures.Profile(Fixtures.Folder(Fixtures.Synthetic)), r => r with { Id = 2 });
        SetRegisters(a.Sim, 10);
        SetRegisters(b.Sim, 20);
        var store = new ListStore();

        await using var recorder = new TrendRecorder(store) { Floor = TimeSpan.FromMilliseconds(40) };
        await recorder.StartAsync(a.Robot, [new TrendSignal(11, 1, SignalAddress.Parse("R[1]"), null)], TimeSpan.FromMilliseconds(40));
        await recorder.StartAsync(b.Robot, [new TrendSignal(21, 2, SignalAddress.Parse("R[1]"), null)], TimeSpan.FromMilliseconds(40));
        Assert.Equal(2, recorder.Recording.Count);

        await WaitFor(() => store.Count(11) >= 1 && store.Count(21) >= 1);
        await recorder.StopAllAsync();

        Assert.Equal(10, store.Values(11)[0]);
        Assert.Equal(20, store.Values(21)[0]);
        Assert.Empty(recorder.Recording);
    }

    [Fact]
    public async Task ARobotThatStopsAnsweringIsBackedOff()
    {
        var listener = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();

        var robot = new Robot("Gone", IPAddress.Loopback) { Id = 9, HttpPort = port };
        var statuses = new List<RecorderStatus>();
        await using var recorder = new TrendRecorder(new ListStore()) { Floor = TimeSpan.FromMilliseconds(20) };
        recorder.StatusChanged += (_, s) => { lock (statuses) { statuses.Add(s); } };
        await recorder.StartAsync(robot, [new TrendSignal(1, 9, SignalAddress.Parse("R[1]"), null)], TimeSpan.FromMilliseconds(20));

        await WaitFor(() => { lock (statuses) { return statuses.Count >= 2; } });
        await recorder.StopAsync(9);

        RecorderStatus second;
        lock (statuses)
        {
            second = statuses[1];
        }

        Assert.True(second.CurrentInterval > TimeSpan.FromMilliseconds(20));
        Assert.StartsWith("No values", second.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TheIntervalNeverGoesBelowTheFloor() =>
        Assert.Equal(TimeSpan.FromSeconds(2), TrendRecorder.MinimumInterval);

    [Fact]
    public async Task TheAnimatorMakesValuesMove()
    {
        await using SimRig rig = SimRig.Start(Fixtures.Profile(Fixtures.Folder(Fixtures.Synthetic)));
        using var animator = new SimAnimator(rig.Sim, TimeSpan.FromMilliseconds(50));
        using var web = new ControllerWebClient(IPAddress.Loopback, rig.Sim.HttpPort);

        SampleRead read = await RobotSampler.ReadAsync(web, Addresses("R[1]", "R[4]", "DI[1]", "GI[1]", "$TIMER[1].$TIMER_VAL"));
        Assert.Empty(read.Problems);
        Assert.InRange(read.Values["R[4]"], 38, 42);
    }

    private static void SetRegisters(SimController sim, int r1) =>
        sim.MdOverrides["NUMREG.VA"] = Encoding.Latin1.GetBytes($"[*NUMREG*]$NUMREG\r\n  [1] = {r1}  'Counter'\r\n  [2] = 1287  'Weld count'\r\n");

    private static async Task WaitFor(Func<bool> condition)
    {
        for (int i = 0; i < 200 && !condition(); i++)
        {
            await Task.Delay(25);
        }

        Assert.True(condition(), "Timed out waiting.");
    }

    private sealed class ListStore : ITrendStore
    {
        private readonly List<TrendSample> _samples = [];

        public bool Append(IReadOnlyList<TrendSample> samples)
        {
            lock (_samples)
            {
                _samples.AddRange(samples);
            }

            return true;
        }

        public int Count(long signal)
        {
            lock (_samples)
            {
                return _samples.Count(s => s.SignalId == signal);
            }
        }

        public double[] Values(long signal)
        {
            lock (_samples)
            {
                return [.. _samples.Where(s => s.SignalId == signal).Select(s => s.Value)];
            }
        }
    }
}
