namespace RobControl.Core.Live;

/// <summary>
/// One task from <c>PRGSTATE.DG</c>'s TASK STATES: <c>9  SWAXTSK1 RUNNING @ 874 in PROCESSAMR of SWAXTCMN</c>
/// or <c>2  DGMONFRC status = ABORTED</c>, with the call stack under it (deepest first, as listed).
/// </summary>
public sealed record TaskState(int Number, string Name, string Status, int? Line, string? Routine, string? Program, IReadOnlyList<TaskFrame> Stack)
{
    public bool IsRunning => Status.Equals("RUNNING", StringComparison.OrdinalIgnoreCase);

    /// <summary>"PROCESSAMR of SWAXTCMN, line 874", or the top of the stack when the status line does not say.</summary>
    public string Where
    {
        get
        {
            if (Routine is not null)
            {
                string of = Program is null || Program == Routine ? string.Empty : $" of {Program}";
                return Line is { } at ? $"{Routine}{of}, line {at}" : $"{Routine}{of}";
            }

            return Stack.Count == 0 ? string.Empty : Describe(Stack[0]);
        }
    }

    /// <summary>"SWAXTSK1:172 > PROCESSAMR:874" - outermost first.</summary>
    public string CallStack => string.Join(" > ", Stack.OrderBy(f => f.Depth).Select(f => f.Line is { } l ? $"{f.Routine}:{l}" : f.Routine));

    private static string Describe(TaskFrame frame)
    {
        string of = frame.Program == frame.Routine ? string.Empty : $" of {frame.Program}";
        return frame.Line is { } at ? $"{frame.Routine}{of}, line {at}" : $"{frame.Routine}{of}";
    }
}
