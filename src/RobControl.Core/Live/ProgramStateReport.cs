namespace RobControl.Core.Live;

/// <summary>What <c>PRGSTATE.DG</c> says: the tasks and where each is, and every program loaded.</summary>
public sealed record ProgramStateReport(IReadOnlyList<TaskState> Tasks, IReadOnlyList<ProgramInfo> Programs)
{
    public static ProgramStateReport Empty { get; } = new([], []);

    public bool IsEmpty => Tasks.Count == 0 && Programs.Count == 0;
}
