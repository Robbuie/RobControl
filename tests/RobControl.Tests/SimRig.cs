using System.Net;
using RobControl.Core.Events;
using RobControl.Core.Robots;
using RobControl.RobotSim;

namespace RobControl.Tests;

/// <summary>A running simulated controller, and a <see cref="Robot"/> pointed at it.</summary>
internal sealed class SimRig : IAsyncDisposable
{
    private SimRig(SimController sim, Robot robot)
    {
        Sim = sim;
        Robot = robot;
    }

    public SimController Sim { get; }

    public Robot Robot { get; }

    public List<RobotEvent> Events { get; } = [];

    public IEventSink Sink => new ListSink(Events);

    public static SimRig Start(SimProfile profile, Func<Robot, Robot>? change = null)
    {
        var sim = new SimController(profile, IPAddress.Loopback);
        sim.Start();
        var robot = new Robot("R2-14", IPAddress.Loopback) { FtpPort = sim.FtpPort, HttpPort = sim.HttpPort, Line = "Body shop" };
        return new SimRig(sim, change is null ? robot : change(robot));
    }

    /// <summary>Every command the simulator saw that was not a read. Must be empty after anything RobControl does.</summary>
    public IReadOnlyList<string> Refused => [.. Sim.RefusedCommands];

    public ValueTask DisposeAsync() => Sim.DisposeAsync();

    private sealed class ListSink(List<RobotEvent> events) : IEventSink
    {
        public void Record(RobotEvent entry)
        {
            lock (events)
            {
                events.Add(entry);
            }
        }
    }
}
