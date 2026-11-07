namespace RobControl.App.ViewModels;

/// <summary>One Cartesian axis: its value in the current user frame and in world, side by side.</summary>
public sealed record LiveCartesianRow(string Group, string Axis, string User, string World, string Unit);
