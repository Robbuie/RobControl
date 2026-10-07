namespace RobControl.Core.Insight;

/// <summary>The hits, how much was read, and whether the hit limit cut the list short.</summary>
public sealed record SearchResult(IReadOnlyList<SearchHit> Hits, int FilesSearched, int RobotsSearched, bool Truncated);
