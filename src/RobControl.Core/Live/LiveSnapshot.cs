using RobControl.Core.Insight;
using RobControl.Core.Trending;

namespace RobControl.Core.Live;

/// <summary>Everything one Live read brought back from one robot.</summary>
public sealed record LiveSnapshot(
    DateTimeOffset Utc,
    IReadOnlyList<LiveFile> Files,
    IReadOnlyList<AlarmEntry> ActiveAlarms,
    IReadOnlyList<AlarmEntry> RecentAlarms,
    IReadOnlyDictionary<string, double> Io)
{
    /// <summary>Every I/O point on the page with its comment, safe I/O included - not only the ones the recorder can trend.</summary>
    public IReadOnlyList<IoPoint> IoPoints { get; init; } = [];

    /// <summary>The stop signals from <c>SFTYSIG.DG</c>.</summary>
    public IReadOnlyList<SafetySignal> SafetySignals { get; init; } = [];

    public ProgramStateReport Programs { get; init; } = ProgramStateReport.Empty;

    public IReadOnlyList<PositionFrame> Position { get; init; } = [];

    /// <summary>"4 of 6 pages read" - for the status line and the event log.</summary>
    public string Summary
    {
        get
        {
            int ok = Files.Count(f => f.IsOk);
            int points = Math.Max(Io.Count, IoPoints.Count);
            string locked = Files.Any(f => f.Status == LiveFileStatus.Locked) ? " Diagnostic files are locked." : string.Empty;
            string route = Route is { } r ? " " + r : string.Empty;
            return $"{ok} of {Files.Count} pages read; {ActiveAlarms.Count} active alarm(s), {points} I/O point(s).{locked}{route}";
        }
    }

    /// <summary>
    /// How the web server had to be asked, when it was not simply the first try: "Web server answered
    /// only as plain HTTP/1.0 (6 pages)." Null when every page answered first time or nothing did.
    /// </summary>
    public string? Route
    {
        get
        {
            var odd = Files.Where(f => f.Via is Transports.Http.HttpAttempt.AfterRetry or Transports.Http.HttpAttempt.PlainHttp10)
                .GroupBy(f => f.Via!.Value).OrderBy(g => g.Key).ToList();
            if (odd.Count == 0)
            {
                return null;
            }

            return "Web server answered " + string.Join(", ", odd.Select(g =>
                $"{Transports.Http.HttpExchange.Describe(g.Key)} ({g.Count()} page{(g.Count() == 1 ? string.Empty : "s")})")) + ".";
        }
    }

    public LiveFile? File(string fileName) =>
        Files.FirstOrDefault(f => string.Equals(f.FileName, fileName, StringComparison.OrdinalIgnoreCase));
}
