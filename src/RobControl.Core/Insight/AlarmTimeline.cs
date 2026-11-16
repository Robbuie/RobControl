namespace RobControl.Core.Insight;

/// <summary>A cell's alarms on one time axis and grouped into incidents.</summary>
public sealed record AlarmTimelineResult(IReadOnlyList<TimelineEntry> Entries, IReadOnlyList<AlarmIncident> Incidents, int Undated);

/// <summary>
/// Every robot's alarms on one time axis, so a line stop shows which robot went first.
///
/// <para><b>Clocks.</b> An alarm log is in the controller's own wall-clock time, and controller clocks
/// drift. Where a robot's offset from this PC is known (its web server's <c>Date</c> header, read by a
/// probe or a live read), it is taken out; where it is not, the log's time is used and the row says so.
/// Logs often give minutes, not seconds: robots that share the first minute of an incident are all named
/// as first, rather than one picked at random.</para>
///
/// <para><b>Incidents.</b> Alarms less than <c>gap</c> apart (default two minutes) belong together; an
/// incident on several robots is what a cell stop looks like.</para>
/// </summary>
public static class AlarmTimeline
{
    public static readonly TimeSpan DefaultGap = TimeSpan.FromMinutes(2);

    /// <param name="skew">Per robot name: controller clock minus this PC's (as <see cref="Controllers.ClockReading.Skew"/>).</param>
    public static AlarmTimelineResult Build(IEnumerable<AlarmEntry> alarms, IReadOnlyDictionary<string, TimeSpan>? skew = null, TimeSpan? gap = null, DateTime? since = null)
    {
        ArgumentNullException.ThrowIfNull(alarms);
        TimeSpan between = gap ?? DefaultGap;
        var seen = new HashSet<(string, DateTime?, string, string)>();
        var dated = new List<(AlarmEntry Alarm, DateTime At, TimeSpan? Fix)>();
        int undated = 0;
        foreach (AlarmEntry alarm in alarms)
        {
            if (!seen.Add((alarm.Robot.ToUpperInvariant(), alarm.When, alarm.Code, alarm.When is null ? alarm.Line : alarm.Message)))
            {
                continue;
            }

            if (alarm.When is not { } when)
            {
                undated++;
                continue;
            }

            TimeSpan? fix = skew is not null && skew.TryGetValue(alarm.Robot, out TimeSpan s) ? s : null;
            DateTime at = fix is { } f ? when - f : when;
            if (since is null || at >= since)
            {
                dated.Add((alarm, at, fix));
            }
        }

        // Oldest first to group; a stable sort keeps each log's own order for alarms in the same minute.
        List<(AlarmEntry Alarm, DateTime At, TimeSpan? Fix)> ordered = [.. dated.OrderBy(d => d.At)];
        var incidents = new List<AlarmIncident>();
        var entries = new List<TimelineEntry>(ordered.Count);
        int start = 0;
        while (start < ordered.Count)
        {
            int end = start;
            while (end + 1 < ordered.Count && ordered[end + 1].At - ordered[end].At < between)
            {
                end++;
            }

            int number = incidents.Count + 1;
            DateTime first = ordered[start].At;
            List<(AlarmEntry Alarm, DateTime At, TimeSpan? Fix)> group = ordered.GetRange(start, end - start + 1);
            List<string> firstRobots = [.. group.Where(g => g.At == first).Select(g => g.Alarm.Robot).Distinct(StringComparer.OrdinalIgnoreCase)];
            List<string> robots = [.. group.Select(g => g.Alarm.Robot).Distinct(StringComparer.OrdinalIgnoreCase)];
            incidents.Add(new AlarmIncident(number, first, ordered[end].At, firstRobots, group[0].Alarm, robots, group.Count));
            foreach ((AlarmEntry alarm, DateTime at, TimeSpan? fix) in group)
            {
                entries.Add(new TimelineEntry(alarm, at, fix, number, at == first));
            }

            start = end + 1;
        }

        entries.Reverse();
        incidents.Reverse();
        return new AlarmTimelineResult(entries, incidents, undated);
    }
}
