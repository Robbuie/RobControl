namespace RobControl.Core.Live;

/// <summary>
/// One block of <c>CURPOS.DG</c>: a motion group's position in one representation - joints, the
/// current user frame, or world - with the frame and tool numbers and the configuration string the
/// controller printed above it.
/// </summary>
/// <param name="Group">Motion group number.</param>
/// <param name="Kind">"Joint", "User frame", "World", or the controller's own words for anything else.</param>
public sealed record PositionFrame(int Group, string Kind, int? UserFrame, int? Tool, string? Config, IReadOnlyList<AxisValue> Axes)
{
    public bool IsJoint => Kind == PositionParser.Joint;

    public double? this[string axis] => Axes.FirstOrDefault(a => a.Axis.Equals(axis, StringComparison.OrdinalIgnoreCase))?.Value;
}
