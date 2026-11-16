using RobControl.Core.Insight;

namespace RobControl.Core.Live;

/// <summary>Turns on alarm snapshots for a recording: where they go, and how often the active alarms are looked at.</summary>
/// <param name="Folder">The site's snapshot folder; each robot gets a folder under it.</param>
public sealed record AlarmWatchOptions(string Folder)
{
    /// <summary>The active-alarm page is read at most this often, on the recording's own connection.</summary>
    public TimeSpan CheckEvery { get; init; } = TimeSpan.FromSeconds(10);

    /// <summary>At most one snapshot this often per robot: a burst of alarms is one event, not twenty folders.</summary>
    public TimeSpan MinimumGap { get; init; } = TimeSpan.FromSeconds(60);

    /// <summary>The program listing from the robot's newest backup, for "where it was" in the snapshot. Optional.</summary>
    public Func<string, (ProgramListing Listing, DateTimeOffset TakenUtc)?>? Listing { get; init; }

    /// <summary>The robot's clock offset at the time, when known - written into the snapshot.</summary>
    public Func<string?>? ClockNote { get; init; }
}
