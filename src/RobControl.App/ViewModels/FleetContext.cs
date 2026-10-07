using RobControl.Core.Backup;
using RobControl.Core.Controllers;
using RobControl.Core.Robots;

namespace RobControl.App.ViewModels;

/// <summary>
/// The open site as the fleet-wide tabs need it, taken as a snapshot when a search or report starts:
/// the robot list can change while one runs, and the run should finish on the list it started with.
/// </summary>
public sealed record FleetContext(
    string SiteName,
    string? SiteNotes,
    BackupArchive Archive,
    IReadOnlyList<(Robot Robot, ControllerIdentity? Identity)> Robots,
    string Tool)
{
    public IEnumerable<Robot> RobotList => Robots.Select(r => r.Robot);
}
