namespace RobControl.Core.Insight;

/// <summary>A teach pendant program as its ASCII listing (<c>.LS</c>) describes it.</summary>
public sealed record ProgramListing(string Name, string? Comment, IReadOnlyList<ProgramLine> Lines, IReadOnlyList<ProgramReference> References)
{
    /// <summary>Programs this one CALLs or RUNs, each once.</summary>
    public IEnumerable<string> Calls =>
        References.Where(r => r.Kind == ReferenceKind.Program).Select(r => r.Target).Distinct(StringComparer.OrdinalIgnoreCase);
}
