using System.Globalization;
using System.Text.RegularExpressions;

namespace RobControl.Core.Trending;

/// <summary>
/// I/O states out of an <c>IOSTATE.DG</c> page, keyed as <c>DI[1]</c>.
///
/// <para>Searches rather than parses, for the same reason as the identity parser: the layout is
/// unconfirmed (Phase 0). Anything shaped like <c>DI[ 1] ON</c>, <c>DO[12] = OFF</c> or
/// <c>GI[1] 37</c> anywhere in the text is taken - including with table markup between the name and
/// the value, in case the page is HTML; nothing else is.</para>
/// </summary>
public static partial class IoStateParser
{
    public static IReadOnlyDictionary<string, double> Parse(string? text)
    {
        var values = new Dictionary<string, double>(StringComparer.Ordinal);
        if (string.IsNullOrEmpty(text))
        {
            return values;
        }

        foreach (Match match in Entry().Matches(text))
        {
            string type = match.Groups["type"].Value.ToUpperInvariant();
            if (!SignalAddress.IoTypes.Contains(type)
                || !int.TryParse(match.Groups["n"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out int n)
                || !ScalarParser.TryParse(match.Groups["v"].Value, out double value))
            {
                continue;
            }

            values[$"{type}[{n}]"] = value;
        }

        return values;
    }

    [GeneratedRegex(@"\b(?<type>[A-Za-z]{1,2})\[\s*(?<n>\d{1,6})\s*\]\s*(?:<[^>]{0,80}>\s*){0,6}(?:=\s*)?(?<v>ON|OFF|-?\d+(?:\.\d+)?)\b", RegexOptions.IgnoreCase)]
    private static partial Regex Entry();
}
