namespace RobControl.Core.Insight;

/// <summary>
/// One network value found in a backup's variable listings: what kind it looks like, the variable
/// it was under, the value, and the file - so a person can check RobControl's reading against the
/// listing itself.
/// </summary>
public sealed record NetworkSetting(string Robot, string Kind, string Variable, string Value, string File);
