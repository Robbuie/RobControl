namespace RobControl.Core.History;

/// <summary>One file that differs between a robot's two newest complete backups.</summary>
/// <param name="Path">Inside the backup: <c>MD/WELD_A.LS</c>.</param>
/// <param name="LinesAdded">For a text file present in both: lines added. Null when not counted (binary, too large, added or removed whole).</param>
/// <param name="IsSafety">A DCS file - the safety configuration. Always worth a look, whatever else changed.</param>
public sealed record FileChangeSummary(string Path, FileChange Change, ChangeKind Kind, int? LinesAdded, int? LinesRemoved, bool IsSafety)
{
    /// <summary>"changed (+3 -1)", "added", "removed".</summary>
    public string Describe()
    {
        string what = Change switch
        {
            FileChange.Added => "added",
            FileChange.Removed => "removed",
            _ => "changed",
        };
        return LinesAdded is { } plus && LinesRemoved is { } minus
            ? $"{what} (+{plus} -{minus})"
            : what;
    }
}
