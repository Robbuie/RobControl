using System.Text;
using RobControl.Core.Insight;

namespace RobControl.Core.Events;

/// <summary>
/// The event log as CSV - the record of everything RobControl sent to a controller at this site:
/// every probe, backup, KCL read and trend session, who it went to, and what came back. What to
/// hand plant IT or a FANUC engineer who asks what this laptop has been doing on their network.
/// </summary>
public static class EventLogCsv
{
    public static string ToCsv(IEnumerable<RobotEvent> events)
    {
        ArgumentNullException.ThrowIfNull(events);
        var csv = new StringBuilder();
        csv.AppendLine("When,UTC,Severity,What,Robot,Message,Detail");
        foreach (RobotEvent e in events.OrderBy(e => e.Utc))
        {
            csv.AppendLine(Csv.Row(
                Csv.TimeSeconds(e.Utc),
                e.Utc.UtcDateTime.ToString("O", System.Globalization.CultureInfo.InvariantCulture),
                e.Severity.ToString(),
                e.Category.ToString(),
                e.Robot,
                e.Message,
                e.Detail));
        }

        return csv.ToString();
    }
}
