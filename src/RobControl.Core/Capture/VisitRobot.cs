using RobControl.Core.Backup;
using RobControl.Core.Controllers;
using RobControl.Core.History;
using RobControl.Core.Robots;

namespace RobControl.Core.Capture;

/// <summary>What a site visit did with one robot: each step's result, or why it was not done.</summary>
public sealed record VisitRobot(Robot Robot)
{
    public ProbeReport? Probe { get; init; }

    public BackupResult? Backup { get; init; }

    public CaptureResult? Capture { get; init; }

    /// <summary>The new backup against the complete one before it.</summary>
    public RobotChanges? Changes { get; init; }

    /// <summary>Steps not done and why: "backup: FTP did not answer", "skipped: recording trends".</summary>
    public IReadOnlyList<string> Notes { get; init; } = [];

    /// <summary>Probe answered, backup complete, capture read pages.</summary>
    public bool AllGood =>
        Probe is not null && Backup?.Outcome == BackupOutcome.Complete && Capture is not null && Capture.Pages.Any(p => p.IsOk);
}
