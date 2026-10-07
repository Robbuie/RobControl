namespace RobControl.Core.Insight;

/// <summary>One robot in the fleet inventory: what it is, and how its backups stand.</summary>
public sealed record InventoryRow(
    string Robot,
    string Address,
    string? Line,
    string Controller,
    string? Model,
    string? Software,
    string? Application,
    string? FNumber,
    DateTimeOffset? LastCompleteUtc,
    DateTimeOffset? LastAttemptUtc,
    string LastOutcome,
    int BackupCount)
{
    /// <summary>Days since the last complete backup, as of <paramref name="now"/>; null when there is none.</summary>
    public double? BackupAgeDays(DateTimeOffset now) => LastCompleteUtc is { } last ? (now - last).TotalDays : null;
}
