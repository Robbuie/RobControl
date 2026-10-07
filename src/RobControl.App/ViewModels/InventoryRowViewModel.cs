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

    public int BackupCount => Row.BackupCount;
}
