using RobControl.Core.Backup;
using RobControl.Core.Controllers;
using RobControl.Core.Events;
using RobControl.Core.Robots;

namespace RobControl.App.ViewModels;

/// <summary>
/// The open site as the fleet-wide tabs need it, taken as a snapshot when a search or report starts:
/// the robot list can change while one runs, and the run should finish on the list it started with.
///
/// <para><see cref="StaleAfterDays"/> and <see cref="KeepBackups"/> are the site's settings;
/// <see cref="Events"/> is the site's event log, for the checks and prunes the tab runs.</para>
/// </summary>
public sealed record FleetContext(
    string SiteName,
    string? SiteNotes,
    BackupArchive Archive,
    IReadOnlyList<(Robot Robot, ControllerIdentity? Identity)> Robots,
    string Tool,
    int StaleAfterDays = 7,
    int KeepBackups = 0,
    IEventSink? Events = null)
{
    public IEnumerable<Robot> RobotList => Robots.Select(r => r.Robot);
}
