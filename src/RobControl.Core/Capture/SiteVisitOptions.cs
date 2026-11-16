using RobControl.Core.Backup;
using RobControl.Core.Controllers;
using RobControl.Core.Robots;

namespace RobControl.Core.Capture;

/// <summary>Everything a site visit needs from the site that runs it.</summary>
public sealed record SiteVisitOptions
{
    /// <summary>"Lansing Delta / Line 3" - the report's title and the folder name.</summary>
    public required string Title { get; init; }

    /// <summary>"RobControl 0.8.0+abc".</summary>
    public required string Tool { get; init; }

    public required BackupArchive Archive { get; init; }

    /// <summary>Each robot's backup devices and excludes, from its system.</summary>
    public Func<Robot, BackupOptions> BackupOptionsFor { get; init; } = _ => BackupOptions.Default;

    /// <summary>A robot to leave out, with the reason ("recording trends"), or null to visit it.</summary>
    public Func<Robot, string?> Skip { get; init; } = _ => null;

    /// <summary>The site's own "not a change" patterns, for the changes column.</summary>
    public IReadOnlyList<string> IgnoreChanges { get; init; } = [];

    /// <summary>Called after each probe, so the site can store it and update its list.</summary>
    public Action<Robot, ProbeReport>? Probed { get; init; }

    /// <summary>Called after each backup, so the site can refresh its history.</summary>
    public Action<Robot, BackupResult>? BackedUp { get; init; }

    /// <summary>Called before a robot is started, after it is done - the list can show which one is in hand.</summary>
    public Action<Robot, string?>? Activity { get; init; }

    /// <summary>The robot's event log as CSV, for its capture. Null leaves it out.</summary>
    public Func<Robot, string?>? EventLog { get; init; }

    /// <summary>Pause between robots. Each robot already gets one request at a time.</summary>
    public TimeSpan Pause { get; init; } = TimeSpan.FromSeconds(1);

    /// <summary>Probe, back up, capture. Tests switch steps off.</summary>
    public bool Backup { get; init; } = true;

    public bool Capture { get; init; } = true;

    public bool Zip { get; init; } = true;
}
