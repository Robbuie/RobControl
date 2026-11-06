using RobControl.Core.Insight;

namespace RobControl.Core.Live;

/// <summary>Everything one Live read brought back from one robot.</summary>
public sealed record LiveSnapshot(
    DateTimeOffset Utc,
    IReadOnlyList<LiveFile> Files,
    IReadOnlyList<AlarmEntry> ActiveAlarms,
    IReadOnlyList<AlarmEntry> RecentAlarms,
    IReadOnlyDictionary<string, double> Io)
{
    /// <summary>"4 of 5 pages read" - for the status line and the event log.</summary>
    public string Summary
    {
        get
        {
            int ok = Files.Count(f => f.IsOk);
            string locked = Files.Any(f => f.Status == LiveFileStatus.Locked) ? " Diagnostic files are locked." : string.Empty;
            return $"{ok} of {Files.Count} pages read; {ActiveAlarms.Count} active alarm(s), {Io.Count} I/O point(s).{locked}";
        }
    }

    public LiveFile? File(string fileName) =>
        Files.FirstOrDefault(f => string.Equals(f.FileName, fileName, StringComparison.OrdinalIgnoreCase));
}
