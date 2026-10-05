namespace RobControl.Core.History;

/// <summary>One line of a diff. Line numbers are 1-based; the side a line is not on is null.</summary>
public sealed record DiffLine(DiffLineKind Kind, int? OldNumber, int? NewNumber, string Text);
