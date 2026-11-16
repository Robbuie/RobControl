using System.Globalization;

namespace RobControl.Core.Insight;

/// <summary>Alarms close together in time across a cell - what probably was one event.</summary>
/// <param name="FirstRobots">The robot(s) with the earliest alarm. More than one when they share the first time - the logs give minutes, often not seconds.</param>
public sealed record AlarmIncident(int Number, DateTime Start, DateTime End, IReadOnlyList<string> FirstRobots, AlarmEntry First, IReadOnlyList<string> Robots, int Count)
{
    public bool SpansRobots => Robots.Count > 1;

    /// <summary>"R1-04" or "R1-04 or R1-05 (same time in their logs)".</summary>
    public string FirstText => FirstRobots.Count == 1
        ? FirstRobots[0]
        : string.Join(" or ", FirstRobots) + " (same time in their logs)";

    public string RobotsText => string.Join(", ", Robots);

    public string Duration
    {
        get
        {
            TimeSpan span = End - Start;
            return span < TimeSpan.FromMinutes(1)
                ? string.Create(CultureInfo.InvariantCulture, $"{span.TotalSeconds:0} s")
                : string.Create(CultureInfo.InvariantCulture, $"{span.TotalMinutes:0} min");
        }
    }

    public string FirstAlarm => $"{First.Code} {First.Message}";
}
