namespace RobControl.Core.Insight;

/// <summary>
/// One reference from a program line. <see cref="Target"/> is canonical - <c>R[45]</c>, <c>DO[120]</c>,
/// <c>WELD_A</c> - whatever comment the line carried (<c>R[45:Weld count]</c>) is left out, so two
/// lines naming the same register compare equal.
/// </summary>
public sealed record ProgramReference(ReferenceKind Kind, string Target, int Line);
