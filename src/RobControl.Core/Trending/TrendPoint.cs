namespace RobControl.Core.Trending;

/// <summary>One stored value of one signal.</summary>
public readonly record struct TrendPoint(DateTimeOffset Utc, double Value);
