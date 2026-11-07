namespace RobControl.App.ViewModels;

/// <summary>One task from PRGSTATE.DG on the Live tab's Program view.</summary>
public sealed record LiveTaskRow(int Number, string Name, string Status, string Where, string CallStack, bool IsRunning);
