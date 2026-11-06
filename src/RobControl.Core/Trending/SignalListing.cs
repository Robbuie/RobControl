namespace RobControl.Core.Trending;

/// <summary>
/// What the signal browser found in one place - a backup or one read of a robot - and anything
/// worth telling the person about where it looked ("no NUMREG.VA in this backup").
/// </summary>
public sealed record SignalListing(IReadOnlyList<SignalEntry> Entries, IReadOnlyList<string> Notes)
{
    public static SignalListing Empty(string note) => new([], [note]);
}
