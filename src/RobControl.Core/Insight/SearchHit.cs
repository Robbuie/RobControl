namespace RobControl.Core.Insight;

/// <summary>One matching line. <see cref="Program"/> is the TP program name when the file is a listing.</summary>
public sealed record SearchHit(string Robot, string Stamp, string File, string? Program, int Line, string Text, string FullPath);
