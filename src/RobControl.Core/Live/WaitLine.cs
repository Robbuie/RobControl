namespace RobControl.Core.Live;

/// <summary>Where one task is, the line it is at (from the newest backup's listing) and the signals on it.</summary>
/// <param name="Text">The line from the listing, or null when no listing was found.</param>
/// <param name="Note">Why the line may not be right, or why there is none.</param>
public sealed record WaitLine(string Task, string Status, string Program, int? Line, string? Text, IReadOnlyList<WaitSignal> Signals, string? Note)
{
    public bool IsWaitingOnASignal => Signals.Any(s => s.Satisfied == false);

    public string SignalsText => Signals.Count == 0
        ? string.Empty
        : string.Join("; ", Signals.Select(s => $"{s.Point}{(s.Comment.Length > 0 ? " " + s.Comment : string.Empty)} = {s.Now ?? "?"}{(s.Expected is null ? string.Empty : $" (wants {s.Expected})")}"));
}
