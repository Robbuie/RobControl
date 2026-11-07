namespace RobControl.App.ViewModels;

/// <summary>
/// One axis on the Live tab's Position view. Joints are one value each; a Cartesian axis has its
/// value in the current user frame and in world side by side.
/// </summary>
public sealed record LivePositionRow(string Group, string Axis, string Value, string Unit);
