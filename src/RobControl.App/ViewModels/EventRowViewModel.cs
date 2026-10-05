using System.Globalization;
using RobControl.App.Diagnostics;
using RobControl.Core.Events;
using RobControl.Core.Persistence;

namespace RobControl.App.ViewModels;

/// <summary>One row of the event log, as shown. Immutable: a log row is a record of a moment.</summary>
public sealed class EventRowViewModel(RobotEvent entry)
{
    public RobotEvent Entry { get; } = entry ?? throw new ArgumentNullException(nameof(entry));

    public string When => Entry.Utc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.CurrentCulture);

    public string Category => Entry.Category.ToString();

    public string Robot => Entry.Robot ?? string.Empty;

    public string Message => Entry.Message;

    public string? Detail => Entry.Detail;

    public ReadinessState State => Entry.Severity switch
    {
        EventSeverity.Error => ReadinessState.Blocked,
        EventSeverity.Warn => ReadinessState.Warning,
        _ => ReadinessState.Ready,
    };
}
