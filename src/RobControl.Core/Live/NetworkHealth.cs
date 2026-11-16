using System.Globalization;
using System.Text.RegularExpressions;
using RobControl.Core.Transports.Http;

namespace RobControl.Core.Live;

/// <summary>One counter or setting from the Ethernet page: section, name, value, and whether it looks like trouble.</summary>
public sealed record NetworkCounter(string Section, string Name, string Value, bool Concern);

/// <summary>One line of the EtherNet/IP log, and whether it mentions a fault.</summary>
public sealed record NetworkLogLine(int Number, string Text, bool Concern);

/// <summary>What <c>ETHERNET.DG</c> and <c>EIPLOG.DG</c> say: counters and the connection log.</summary>
public sealed record NetworkHealthReport(IReadOnlyList<NetworkCounter> Counters, IReadOnlyList<NetworkLogLine> Log)
{
    public static NetworkHealthReport Empty { get; } = new([], []);

    public int Concerns => Counters.Count(c => c.Concern) + Log.Count(l => l.Concern);

    /// <summary>"3 things worth a look: CRC errors 12, ..." - or that nothing was flagged.</summary>
    public string Summary
    {
        get
        {
            if (Counters.Count == 0 && Log.Count == 0)
            {
                return "Nothing read from the Ethernet or EtherNet/IP pages.";
            }

            List<string> flagged = [.. Counters.Where(c => c.Concern).Select(c => $"{c.Name} {c.Value}").Concat(Log.Where(l => l.Concern).Select(_ => "log").Distinct())];
            return flagged.Count == 0
                ? string.Create(CultureInfo.InvariantCulture, $"{Counters.Count} values, {Log.Count} log lines - nothing flagged.")
                : string.Create(CultureInfo.InvariantCulture, $"{Concerns} thing(s) worth a look: {string.Join(", ", flagged.Take(6))}.");
        }
    }
}

/// <summary>
/// Reads the controller's Ethernet diagnostic page (<c>ETHERNET.DG</c>) and EtherNet/IP log
/// (<c>EIPLOG.DG</c>) into tables, flagging error counters above zero, links that are down and log lines
/// that mention a fault.
///
/// <para><b>Neither layout has been seen yet</b> (both are listed in the V8.30 robot's index; neither was
/// opened). So this searches rather than parses, like the alarm reader: any <c>name: value</c> or
/// <c>name   value</c> line is a value; a line on its own in capitals or ending in a colon is a section.
/// The Pages tab shows both files whole for whatever this misses - the first capture says what to fix.</para>
/// </summary>
public static partial class NetworkHealth
{
    public const string EthernetFile = "ETHERNET.DG";
    public const string EipLogFile = "EIPLOG.DG";

    /// <summary>Log lines kept, newest last as the page lists them.</summary>
    public const int MaxLogLines = 300;

    public static NetworkHealthReport Parse(string? ethernet, string? eipLog)
    {
        var counters = new List<NetworkCounter>();
        string section = string.Empty;
        foreach (string raw in DiagnosticPage.Text(ethernet).Split('\n'))
        {
            string line = raw.TrimEnd('\r').TrimEnd();
            if (line.Trim().Length == 0)
            {
                continue;
            }

            Match pair = Pair().Match(line);
            if (pair.Success && pair.Groups["v"].Value.Trim().Length > 0)
            {
                string name = pair.Groups["n"].Value.Trim();
                string value = pair.Groups["v"].Value.Trim();
                counters.Add(new NetworkCounter(section, name, value, IsConcern(name, value)));
            }
            else if (Section().IsMatch(line))
            {
                section = line.Trim().TrimEnd(':').Trim('*', '=', '-', ' ');
            }
        }

        var log = new List<NetworkLogLine>();
        int number = 0;
        foreach (string raw in DiagnosticPage.Text(eipLog).Split('\n'))
        {
            string line = raw.TrimEnd('\r').Trim();
            if (line.Length == 0)
            {
                continue;
            }

            number++;
            log.Add(new NetworkLogLine(number, line, Bad().IsMatch(line)));
        }

        if (log.Count > MaxLogLines)
        {
            log = log[^MaxLogLines..];
        }

        return new NetworkHealthReport(counters, log);
    }

    /// <summary>An error-like counter above zero, or a state that says down / disconnected / fault.</summary>
    internal static bool IsConcern(string name, string value)
    {
        if (Bad().IsMatch(value) || (LinkLike().IsMatch(name) && Down().IsMatch(value)))
        {
            return true;
        }

        if (!ErrorLike().IsMatch(name))
        {
            return false;
        }

        Match number = FirstNumber().Match(value);
        return number.Success && long.TryParse(number.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out long n) && n > 0;
    }

    // "CRC errors:   12", "Link Status = UP", "Rx Discards     0"
    [GeneratedRegex(@"^\s*(?<n>[A-Za-z][A-Za-z0-9 _/().#%-]{1,48}?)\s*(?::|=|\s{2,})\s*(?<v>\S.*)$", RegexOptions.CultureInvariant)]
    private static partial Regex Pair();

    [GeneratedRegex(@"^\s*(?:[*=\-]+\s*)?[A-Z0-9][A-Z0-9 /_#().-]{2,}:?\s*(?:[*=\-]+)?\s*$|^\s*[^:=]{3,40}:\s*$", RegexOptions.CultureInvariant)]
    private static partial Regex Section();

    [GeneratedRegex(@"err|collision|crc|drop|discard|overrun|underrun|fail|timeout|time-out|lost|retr(y|ies)|fault|alignment|late|jabber|reject", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ErrorLike();

    [GeneratedRegex(@"\blink\b|status|state|connect", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex LinkLike();

    [GeneratedRegex(@"\b(down|no link|not connected|disconnected|offline|idle)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Down();

    [GeneratedRegex(@"\b(error|fault|faulted|fail(ed|ure)?|timed? ?out|time-out|lost|refused|disconnect(ed)?|duplicate ip|ip conflict)\b|[A-Z]{3,5}-\d{3}", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Bad();

    [GeneratedRegex(@"-?\d+", RegexOptions.CultureInvariant)]
    private static partial Regex FirstNumber();
}
