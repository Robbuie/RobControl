namespace RobControl.Core.Live;

/// <summary>One level of a task's call stack from <c>PRGSTATE.DG</c>: routine, program, line, program type.</summary>
public sealed record TaskFrame(int Depth, string Routine, string Program, int? Line, string? Type);
