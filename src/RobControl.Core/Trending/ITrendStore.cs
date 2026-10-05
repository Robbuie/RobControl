namespace RobControl.Core.Trending;

/// <summary>
/// Where the recorder puts samples. The fleet database implements it; tests use a list.
/// Like <see cref="Events.IEventSink"/>, implementations must not throw - a full disk must not
/// stop a recorder from telling the person it has a problem.
/// </summary>
public interface ITrendStore
{
    /// <summary>Stores a batch. Returns false if it could not, so the recorder can say so.</summary>
    bool Append(IReadOnlyList<TrendSample> samples);
}
