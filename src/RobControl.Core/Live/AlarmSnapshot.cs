using RobControl.Core.Insight;

namespace RobControl.Core.Live;

/// <summary>
/// The state of a robot at the moment a new alarm appeared: what the alarm was and where the pages
/// read just after it were saved.
/// </summary>
/// <param name="Folder">The snapshot folder: <c>snapshot.md</c>, <c>snapshot.json</c> and the pages under <c>MD/</c>.</param>
public sealed record AlarmSnapshot(string Robot, string Address, DateTimeOffset Utc, IReadOnlyList<AlarmEntry> NewAlarms, string Folder, string Summary)
{
    public string When => Utc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", System.Globalization.CultureInfo.CurrentCulture);

    public string AlarmsText => string.Join("; ", NewAlarms.Select(a => $"{a.Code} {a.Message}"));
}
