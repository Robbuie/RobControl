namespace RobControl.Core.Trending;

/// <summary>A value to store, for one signal.</summary>
public readonly record struct TrendSample(long SignalId, DateTimeOffset Utc, double Value);
