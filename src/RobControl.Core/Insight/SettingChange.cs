namespace RobControl.Core.Insight;

/// <summary>
/// A watched setting that differs between two consecutive backups of a robot - a tool frame, the
/// payload, mastering data. <see cref="OldText"/> and <see cref="NewText"/> are the first changed
/// lines, enough to see what moved; the Changes tab has the full diff.
/// </summary>
public sealed record SettingChange(string Robot, string Category, string Variable, string File, string OlderStamp, string NewerStamp, DateTimeOffset NewerUtc, string? OldText, string? NewText);
