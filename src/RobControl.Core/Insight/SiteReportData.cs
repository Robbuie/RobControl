namespace RobControl.Core.Insight;

/// <summary>Everything a site report shows, gathered by the caller so the renderer does no file reading.</summary>
public sealed record SiteReportData
{
    public required string SiteName { get; init; }

    public string? SiteNotes { get; init; }

    public required DateTimeOffset GeneratedUtc { get; init; }

    public required string Tool { get; init; }

    public required string ArchiveRoot { get; init; }

    /// <summary>A last complete backup older than this many days is reported as stale.</summary>
    public int StaleAfterDays { get; init; } = 7;

    /// <summary>The start of the period alarms and setting changes are reported for.</summary>
    public required DateTimeOffset Since { get; init; }

    public IReadOnlyList<InventoryRow> Inventory { get; init; } = [];

    public IReadOnlyList<AlarmCodeSummary> AlarmCodes { get; init; } = [];

    public IReadOnlyList<RobotAlarmSummary> RobotAlarms { get; init; } = [];

    public IReadOnlyList<AlarmEntry> ConcernAlarms { get; init; } = [];

    public IReadOnlyList<SettingChange> SettingChanges { get; init; } = [];
}
