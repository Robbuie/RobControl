namespace RobControl.App.ViewModels;

/// <summary>The periods the Alarms and Fleet tabs offer, and the start of each.</summary>
public static class InsightPeriods
{
    public static IReadOnlyList<string> All { get; } = ["7 days", "30 days", "90 days", "1 year", "All time"];

    public const string Default = "30 days";

    /// <summary>Null for "All time".</summary>
    public static TimeSpan? Length(string? period) => period switch
    {
        "7 days" => TimeSpan.FromDays(7),
        "30 days" => TimeSpan.FromDays(30),
        "90 days" => TimeSpan.FromDays(90),
        "1 year" => TimeSpan.FromDays(365),
        _ => null,
    };
}
