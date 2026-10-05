namespace RobControl.Core.Trending;

/// <summary>A signal being watched on one robot. Stored; survives restarts.</summary>
public sealed record TrendSignal(long Id, long RobotId, SignalAddress Address, string? Label)
{
    public string Describe() => Label is null ? Address.Text : $"{Address.Text} {Label}";
}
