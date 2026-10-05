using RobControl.Core.Persistence;

namespace RobControl.Core.Events;

/// <summary>
/// One row of the record: who did what to which robot, and what came back.
/// </summary>
/// <param name="Robot">"R2-14 (10.20.1.54)", or null for an app-level event.</param>
/// <param name="Detail">The long version - a transcript excerpt, a file list. Optional.</param>
public sealed record RobotEvent(
    DateTimeOffset Utc,
    EventSeverity Severity,
    EventCategory Category,
    string? Robot,
    string Message,
    string? Detail = null);
