namespace RobControl.Core.Insight;

/// <summary>A numbered line of a program's /MN section, without the number and the trailing semicolon.</summary>
public sealed record ProgramLine(int Number, string Text);
