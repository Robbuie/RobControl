namespace RobControl.Core.Events;

/// <summary>Records nothing. For tools and tests that do not care.</summary>
public sealed class NullEventSink : IEventSink
{
    public static NullEventSink Instance { get; } = new();

    private NullEventSink() { }

    public void Record(RobotEvent entry) { }
}
