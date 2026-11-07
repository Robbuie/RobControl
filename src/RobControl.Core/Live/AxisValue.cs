namespace RobControl.Core.Live;

/// <summary>One axis of a position: <c>J1</c>, <c>X</c>, <c>W</c>, <c>E1</c> and its value (degrees or millimetres).</summary>
public sealed record AxisValue(string Axis, double Value);
