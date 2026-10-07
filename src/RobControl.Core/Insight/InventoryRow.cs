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
    int BackupCount,
    int FailedSinceLastComplete = 0)
{
    /// <summary>Days since the last complete backup, as of <paramref name="now"/>; null when there is none.</summary>
    public double? BackupAgeDays(DateTimeOffset now) => LastCompleteUtc is { } last ? (now - last).TotalDays : null;

    /// <summary>Where this robot's backups stand, against the site's limit in days.</summary>
    public BackupHealth Health(DateTimeOffset now, int staleAfterDays) => BackupAgeDays(now) switch
    {
        null => BackupHealth.Never,
        { } days when days > staleAfterDays => BackupHealth.Stale,
        _ when FailedSinceLastComplete > 0 => BackupHealth.Failing,
        _ => BackupHealth.Ok,
    };

    /// <summary>The health in words, with the count that makes it actionable.</summary>
    public string HealthText(DateTimeOffset now, int staleAfterDays) => Health(now, staleAfterDays) switch
    {
        BackupHealth.Never => BackupCount == 0 ? "Never backed up" : $"No complete backup ({BackupCount} attempts)",
        BackupHealth.Stale => $"Stale - over {staleAfterDays} d",
        BackupHealth.Failing => FailedSinceLastComplete == 1 ? "Last attempt failed" : $"Last {FailedSinceLastComplete} attempts failed",
        _ => "OK",
    };
}
