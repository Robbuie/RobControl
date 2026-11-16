namespace RobControl.Core.Capture;

/// <summary>A finished site visit: its folder, its zip, one line per robot and the written report.</summary>
public sealed record VisitResult(string Folder, string? ZipPath, IReadOnlyList<VisitRobot> Robots, string Report)
{
    /// <summary>"8 robots: 6 all good, 2 with problems" - the status line and the event row.</summary>
    public string Summary
    {
        get
        {
            int good = Robots.Count(r => r.AllGood);
            int changed = Robots.Count(r => r.Changes?.HasRealChanges == true);
            string changes = changed == 0 ? string.Empty : $"; {changed} changed since the previous backup";
            return Robots.Count - good == 0
                ? $"{Robots.Count} robot(s), all probed, backed up and captured{changes}"
                : $"{Robots.Count} robot(s): {good} all good, {Robots.Count - good} with something to look at{changes}";
        }
    }
}
