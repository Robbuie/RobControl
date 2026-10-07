using System.Text.RegularExpressions;

namespace RobControl.Core.Insight;

/// <summary>A category of setting worth watching, and the variable names that belong to it.</summary>
public sealed record SettingRule(string Category, Regex Variable);
