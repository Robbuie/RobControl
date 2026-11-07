using System.Text.RegularExpressions;
using RobControl.Core.Transports.Http;

namespace RobControl.Core.Live;

/// <summary>
/// The stop signals out of <c>SFTYSIG.DG</c> ("Current Stop Signals" on the controller's home page):
/// a name, two or more spaces, TRUE or FALSE. Seen on an R-30iB, V8.30, November 2026:
/// <c>External ESTOP   FALSE</c>, <c>Hand Broken      TRUE</c>, <c>Fence Open       FALSE</c>.
/// </summary>
public static partial class SafetySignalParser
{
    public static IReadOnlyList<SafetySignal> Parse(string? text)
    {
        var signals = new List<SafetySignal>();
        foreach (string line in DiagnosticPage.Text(text).Split('\n'))
        {
            Match match = Line().Match(line);
            if (match.Success)
            {
                signals.Add(new SafetySignal(match.Groups["name"].Value.Trim(),
                    match.Groups["v"].Value.Equals("TRUE", StringComparison.OrdinalIgnoreCase)
                    || match.Groups["v"].Value.Equals("ON", StringComparison.OrdinalIgnoreCase)));
            }
        }

        return signals;
    }

    [GeneratedRegex(@"^\s*(?<name>[A-Za-z*][^:\[\]]*?)\s{2,}(?<v>TRUE|FALSE|ON|OFF)\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Line();
}
