namespace RobControl.Core.Trending;

/// <summary>What one robot's recorder is doing, for the UI. Raised after every poll.</summary>
public sealed record RecorderStatus(
    long RobotId,
    bool Running,
    DateTimeOffset? LastPollUtc,
    IReadOnlyDictionary<string, double> LastValues,
    IReadOnlyDictionary<string, string> Problems,
    long SamplesStored,
    TimeSpan CurrentInterval,
    string Message);
