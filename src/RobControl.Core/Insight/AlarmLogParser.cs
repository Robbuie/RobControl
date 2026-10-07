using System.Globalization;
using System.Text.RegularExpressions;

namespace RobControl.Core.Insight;

/// <summary>
/// Reads a controller alarm log (<c>ERRALL.LS</c>, and the like).
///
/// <para><b>Searches rather than parses:</b> any line with an alarm code (<c>SRVO-062</c>) is an
/// alarm; a date and time on the same line (<c>04-OCT-26 21:55</c>, seconds optional) is when it
/// happened; the text after the code is the message. That copes with the column layout changing
/// between software versions - the Phase 0 capture is what confirms the real one.</para>
/// </summary>
public static partial class AlarmLogParser
{
    /// <summary>Log files an alarm history is read from, as listed in a backup.</summary>
    public static bool IsAlarmLog(string fileName) =>
        string.Equals(Path.GetFileName(fileName), "ERRALL.LS", StringComparison.OrdinalIgnoreCase);

    public static IReadOnlyList<AlarmEntry> Parse(string robot, IReadOnlyList<string> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);
        var entries = new List<AlarmEntry>();
        foreach (string raw in lines)
        {
            Match code = Code().Match(raw);
            if (!code.Success)
            {
                continue;
            }

            DateTime? when = null;
            Match date = Date().Match(raw);
            if (date.Success && DateTime.TryParseExact(
                    $"{date.Groups[1].Value}-{date.Groups[2].Value}-{date.Groups[3].Value} {date.Groups[4].Value}:{date.Groups[5].Value}:{(date.Groups[6].Success ? date.Groups[6].Value : "00")}",
                    "dd-MMM-yy HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime parsed))
            {
                when = parsed;
            }

            string message = raw[(code.Index + code.Length)..].Trim().Trim('"').Trim();
            // Some layouts put a status column after the message; keep the message readable.
            message = TrailingColumns().Replace(message, string.Empty).Trim();
            entries.Add(new AlarmEntry(robot, when, code.Value.ToUpperInvariant(), message, raw.Trim()));
        }

        return entries;
    }

    [GeneratedRegex(@"\b[A-Z]{3,5}-\d{3}\b", RegexOptions.CultureInvariant)]
    private static partial Regex Code();

    [GeneratedRegex(@"\b(\d{1,2})-([A-Za-z]{3})-(\d{2})\s+(\d{1,2}):(\d{2})(?::(\d{2}))?", RegexOptions.CultureInvariant)]
    private static partial Regex Date();

    [GeneratedRegex(@"\s{3,}.*$", RegexOptions.CultureInvariant)]
    private static partial Regex TrailingColumns();
}
