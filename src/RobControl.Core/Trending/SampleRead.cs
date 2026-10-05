namespace RobControl.Core.Trending;

/// <summary>
/// One read of a set of signals from one robot: the values that came back, and for each signal that
/// did not, why. A partial read is normal - a locked KCL resource still lets registers through.
/// </summary>
public sealed record SampleRead(
    DateTimeOffset Utc,
    IReadOnlyDictionary<string, double> Values,
    IReadOnlyDictionary<string, string> Problems)
{
    public bool AnyValue => Values.Count > 0;
}
