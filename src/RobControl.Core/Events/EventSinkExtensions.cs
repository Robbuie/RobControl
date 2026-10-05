using RobControl.Core.Persistence;
using RobControl.Core.Robots;

namespace RobControl.Core.Events;

public static class EventSinkExtensions
{
    public static void Info(this IEventSink sink, EventCategory category, Robot? robot, string message, string? detail = null) =>
        Write(sink, EventSeverity.Info, category, robot, message, detail);

    public static void Warn(this IEventSink sink, EventCategory category, Robot? robot, string message, string? detail = null) =>
        Write(sink, EventSeverity.Warn, category, robot, message, detail);

    public static void Error(this IEventSink sink, EventCategory category, Robot? robot, string message, string? detail = null) =>
        Write(sink, EventSeverity.Error, category, robot, message, detail);

    private static void Write(IEventSink sink, EventSeverity severity, EventCategory category, Robot? robot, string message, string? detail)
    {
        ArgumentNullException.ThrowIfNull(sink);
        sink.Record(new RobotEvent(DateTimeOffset.UtcNow, severity, category, robot?.Describe(), message, detail));
    }
}
