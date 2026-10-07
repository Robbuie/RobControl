namespace RobControl.Core.Insight;

/// <summary>
/// One robot's alarms over the period. <see cref="MeanTimeBetween"/> is the span from first to last
/// alarm divided by the gaps between them - a rough MTBF that only means something with several alarms.
/// </summary>
public sealed record RobotAlarmSummary(string Robot, int Count, int DistinctCodes, string TopCode, DateTime? FirstSeen, DateTime? LastSeen, TimeSpan? MeanTimeBetween, int Concerns)
{
    public string MeanTimeBetweenText => MeanTimeBetween switch
    {
        null => string.Empty,
        { TotalHours: < 1 } t => $"{t.TotalMinutes:0} min",
        { TotalDays: < 2 } t => $"{t.TotalHours:0.#} h",
        { } t => $"{t.TotalDays:0.#} days",
    };
}
