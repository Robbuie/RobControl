namespace RobControl.App.ViewModels;

/// <summary>One program loaded on the controller, from PRGSTATE.DG.</summary>
/// <param name="Status">The task's status when the program is running as one (RUNNING, PAUSED, ABORTED), else empty.</param>
/// <param name="Details">Every field the controller listed, one per line - the tooltip.</param>
public sealed record LiveProgramRow(
    string Name,
    string Type,
    string Comment,
    string Status,
    int? Lines,
    int? Size,
    DateTime? Modified,
    string ModifiedText,
    string Source,
    string Protection,
    string Details);
