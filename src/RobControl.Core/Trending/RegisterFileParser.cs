using System.Globalization;
using System.Text.RegularExpressions;

namespace RobControl.Core.Trending;

/// <summary>
/// Numeric register values out of a <c>NUMREG.VA</c> listing, keyed by register number.
///
/// <para>Expected lines look like <c>[2] = 1287  'Weld count'</c>. <b>Unconfirmed</b> against our
/// controllers (Phase 0); the pattern is tolerant of spacing, a missing comment, and floats in
/// exponent form, and ignores every line it does not recognise rather than failing.</para>
/// </summary>
public static partial class RegisterFileParser
{
    public static IReadOnlyDictionary<int, double> Parse(string? text)
    {
        var values = new Dictionary<int, double>();
        if (string.IsNullOrEmpty(text))
        {
            return values;
        }

        foreach (Match match in Line().Matches(text))
        {
            if (int.TryParse(match.Groups["n"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out int n)
                && ScalarParser.TryParse(match.Groups["v"].Value, out double value))
            {
                values[n] = value;
            }
        }

        return values;
    }

    [GeneratedRegex(@"(?m)^\s*\[\s*(?<n>\d{1,6})\s*\]\s*=\s*(?<v>\S+)")]
    private static partial Regex Line();
}
