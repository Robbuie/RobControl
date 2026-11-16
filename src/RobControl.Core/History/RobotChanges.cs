using RobControl.Core.Backup;

namespace RobControl.Core.History;

/// <summary>
/// One robot's answer to "what changed?": its newest complete backup against the complete one before it.
/// </summary>
/// <param name="Problem">Why there is nothing to compare ("only one complete backup"), or null.</param>
public sealed record RobotChanges(string Robot, BackupSet? Newer, BackupSet? Older, IReadOnlyList<FileChangeSummary> Files, string? Problem)
{
    /// <summary>Programs and settings: what a person changed. What the headline counts.</summary>
    public IReadOnlyList<FileChangeSummary> Real => [.. Files.Where(f => f.Kind is ChangeKind.Program or ChangeKind.Setting || f.IsSafety)];

    public bool HasRealChanges => Real.Count > 0;

    public bool HasSafetyChanges => Files.Any(f => f.IsSafety);

    /// <summary>"2 programs, 1 setting, DCS" - or "no program or setting changes".</summary>
    public string Short
    {
        get
        {
            if (Problem is not null)
            {
                return Problem;
            }

            int programs = Files.Count(f => f.Kind == ChangeKind.Program);
            int settings = Files.Count(f => f.Kind == ChangeKind.Setting);
            var parts = new List<string>();
            if (programs > 0)
            {
                parts.Add(programs == 1 ? "1 program" : $"{programs} programs");
            }

            if (settings > 0)
            {
                parts.Add(settings == 1 ? "1 setting file" : $"{settings} setting files");
            }

            if (HasSafetyChanges)
            {
                parts.Add("SAFETY (DCS)");
            }

            return parts.Count == 0 ? "no program or setting changes" : string.Join(", ", parts);
        }
    }
}
