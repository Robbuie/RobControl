using System.Globalization;

namespace RobControl.Core.Insight;

/// <summary>One alarm on the cell timeline, at the time it happened by this PC's clock where the robot's clock offset is known.</summary>
/// <param name="Corrected">The alarm's time with the robot's clock offset taken out; the log's own time when the offset is not known.</param>
/// <param name="Correction">What was taken out (controller minus PC), or null when not known.</param>
/// <param name="Incident">The incident it belongs to - alarms close together across the cell.</param>
/// <param name="IsFirst">The earliest alarm of its incident (one of them, when several share the first time).</param>
public sealed record TimelineEntry(AlarmEntry Alarm, DateTime Corrected, TimeSpan? Correction, int Incident, bool IsFirst)
{
    public string Robot => Alarm.Robot;

    public string Code => Alarm.Code;

    public string Message => Alarm.Message;

    /// <summary>"-12 s", "+3 min", "not known".</summary>
    public string CorrectionText => Correction switch
    {
        null => "clock not known",
        { } c when c.Duration() < TimeSpan.FromSeconds(1) => "clock matches",
        { } c => (c > TimeSpan.Zero ? "-" : "+") + (c.Duration().TotalMinutes >= 1
            ? string.Create(CultureInfo.InvariantCulture, $"{c.Duration().TotalMinutes:0.#} min")
            : string.Create(CultureInfo.InvariantCulture, $"{c.Duration().TotalSeconds:0} s")),
    };
}
