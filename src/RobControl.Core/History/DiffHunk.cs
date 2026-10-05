namespace RobControl.Core.History;

/// <summary>A run of changes with a little context either side, like a unified diff's @@ block.</summary>
public sealed record DiffHunk(int OldStart, int OldCount, int NewStart, int NewCount, IReadOnlyList<DiffLine> Lines)
{
    public string Header => $"@@ -{OldStart},{OldCount} +{NewStart},{NewCount} @@";
}
