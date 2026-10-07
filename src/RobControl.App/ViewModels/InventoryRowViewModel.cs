using System.Globalization;
using RobControl.Core.Insight;

namespace RobControl.App.ViewModels;

/// <summary>An inventory row with its dates and backup age written out for the grid.</summary>
public sealed class InventoryRowViewModel(InventoryRow row, DateTimeOffset now, int staleAfterDays)
{
    public InventoryRow Row { get; } = row;

    public string Robot => Row.Robot;

    public string Address => Row.Address;

    public string? Line => Row.Line;

    public string Controller => Row.Controller;

    public string? Model => Row.Model;

    public string? Software => Row.Software;

    public string? Application => Row.Application;

    public string? FNumber => Row.FNumber;

    public string LastComplete => Row.LastCompleteUtc?.ToLocalTime().ToString("g", CultureInfo.CurrentCulture) ?? "never";

    public string Age => Row.BackupAgeDays(now) switch
    {
        null => "no backup",
        < 1 => "today",
        { } days => string.Create(CultureInfo.CurrentCulture, $"{days:0} d"),
    };

    /// <summary>No complete backup within the limit - shown in the fault colour.</summary>
    public bool IsStale => Row.BackupAgeDays(now) is not { } d || d > staleAfterDays;

    public string LastOutcome => Row.LastOutcome;

    public BackupHealth Health => Row.Health(now, staleAfterDays);

    /// <summary>"OK", "Last 2 attempts failed", "Stale - over 7 d", "Never backed up".</summary>
    public string HealthText => Row.HealthText(now, staleAfterDays);

    /// <summary>Stale or never: shown in the fault colour.</summary>
    public bool IsBad => Health is BackupHealth.Stale or BackupHealth.Never;

    /// <summary>Recent attempts failing while the last good backup is still in date: shown in the warning colour.</summary>
    public bool IsFailing => Health == BackupHealth.Failing;

    public int BackupCount => Row.BackupCount;
}
