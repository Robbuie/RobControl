namespace RobControl.Core.Insight;

/// <summary>A program on a robot that uses something, and on which lines.</summary>
public sealed record ProgramUse(string Robot, string Program, string? Comment, IReadOnlyList<int> Lines)
{
    public string LineList => string.Join(", ", Lines);
}
