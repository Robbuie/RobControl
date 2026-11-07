using System.Globalization;
using System.Text.RegularExpressions;
using RobControl.Core.Transports.Http;

namespace RobControl.Core.Trending;

/// <summary>
/// I/O states out of an <c>IOSTATE.DG</c> page, keyed as <c>DI[1]</c>.
///
/// <para><b>What a real page looks like</b> (R-30iB, V8.30 RepairTool+, seen November 2026): the file
/// sits inside a <c>&lt;pre&gt;</c> of an HTML page; the types are spelled <c>DIN</c>, <c>DOUT</c>,
/// <c>GIN</c>, <c>GOUT</c>, <c>FLG</c> (and <c>UI</c>, <c>UO</c>, <c>SI</c>, <c>SO</c>, <c>RI</c>,
/// <c>RO</c> as on the pendant); a digital value is <c>ON</c>/<c>OFF</c> right-aligned, a group value
/// a number; the I/O comment follows; and flags come two to a line:
/// <c>FLG[   1] OFF  Continue                  FLG[ 513] OFF</c>.</para>
///
/// <para>Still searches rather than parses: any <c>TYPE[n] value</c> on a line is taken, whatever the
/// type is called, so I/O this controller does not have - safe I/O on a DCS or CIP Safety robot
/// (<c>CSI</c>, <c>CSO</c>, <c>SPI</c>, <c>SPO</c>...), weld I/O, analog - is listed as it appears
/// rather than dropped. Register-like names (<c>R</c>, <c>PR</c>, <c>SR</c>) are not I/O and are
/// skipped.</para>
/// </summary>
public static partial class IoStateParser
{
    /// <summary>The page's spelling to the pendant's. Anything not here is kept as written.</summary>
    private static readonly Dictionary<string, string> Aliases = new(StringComparer.Ordinal)
    {
        ["DIN"] = "DI",
        ["DOUT"] = "DO",
        ["GIN"] = "GI",
        ["GOUT"] = "GO",
        ["AIN"] = "AI",
        ["AOUT"] = "AO",
        ["RDI"] = "RI",
        ["RDO"] = "RO",
        ["SDI"] = "SI",
        ["SDO"] = "SO",
        ["UOPI"] = "UI",
        ["UOPO"] = "UO",
        ["FLG"] = "F",
        ["FLAG"] = "F",
        ["MKR"] = "M",
    };

    /// <summary>Bracketed names that are registers or positions, not I/O.</summary>
    private static readonly HashSet<string> NotIo = new(StringComparer.Ordinal)
    {
        "R", "PR", "SR", "AR", "VR", "P", "PL", "J", "POS", "JPOS", "NUMREG", "POSREG", "STRREG", "TIMER", "TMR",
    };

    /// <summary>
    /// DCS safe I/O. CSI/CSO are CIP Safety; SPI/SPO safe peripheral; SSI/SSO safe system; SIR safe
    /// internal relay; NSI/NSO non-safe; RPI/RPO robot peripheral (EE); SFI/SFO safe functions;
    /// LDI/LDO line diagnostics. Names from FANUC's DCS manual, not yet seen on a page here.
    /// </summary>
    public static IReadOnlySet<string> SafeTypes { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        "CSI", "CSO", "SPI", "SPO", "SSI", "SSO", "SIR", "NSI", "NSO", "RPI", "RPO", "SFI", "SFO", "LDI", "LDO",
    };

    /// <summary>The category order the Live tab lists them in - the pendant's I/O menu, then safe I/O.</summary>
    public static IReadOnlyList<string> Categories { get; } =
    [
        "Digital", "Group", "Analog", "Robot (EE)", "UOP", "SOP", "Weld", "Flags", "Markers", "Safe I/O (DCS / CIP Safety)", "Other",
    ];

    /// <summary>Values by key (<c>DI[1]</c>), for the trend recorder. Only types it can trend.</summary>
    public static IReadOnlyDictionary<string, double> Parse(string? text)
    {
        var values = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (IoPoint point in Points(text))
        {
            if (SignalAddress.IoTypes.Contains(point.Type))
            {
                values.TryAdd(point.Key, point.Value);
            }
        }

        return values;
    }

    /// <summary>Every I/O point on the page with its comment, in the order listed, first listing of a key wins.</summary>
    public static IReadOnlyList<IoPoint> Points(string? text)
    {
        var points = new List<IoPoint>();
        string body = DiagnosticPage.Text(text);
        if (body.Length == 0)
        {
            return points;
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (string line in body.Split('\n'))
        {
            MatchCollection matches = Entry().Matches(line);
            for (int i = 0; i < matches.Count; i++)
            {
                Match match = matches[i];
                string listed = match.Groups["type"].Value.ToUpperInvariant();
                if (NotIo.Contains(listed)
                    || !int.TryParse(match.Groups["n"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out int n)
                    || !ScalarParser.TryParse(match.Groups["v"].Value, out double value))
                {
                    continue;
                }

                string type = Aliases.TryGetValue(listed, out string? alias) ? alias : listed;

                // The comment is what follows the value, up to the next point on the same line
                // (flags are printed two to a line), minus any table markup.
                int from = match.Index + match.Length;
                int to = i + 1 < matches.Count ? matches[i + 1].Index : line.Length;
                string comment = Spaces().Replace(Markup().Replace(line[from..to], " "), " ").Trim();

                var point = new IoPoint(type, n, value, comment, listed);
                if (seen.Add(point.Key))
                {
                    points.Add(point);
                }
            }
        }

        return points;
    }

    /// <summary>On/off - everything but group and analog values.</summary>
    public static bool IsDigital(string type) => type is not ("GI" or "GO" or "AI" or "AO");

    public static string CategoryOf(string type) => type switch
    {
        "DI" or "DO" => "Digital",
        "GI" or "GO" => "Group",
        "AI" or "AO" => "Analog",
        "RI" or "RO" => "Robot (EE)",
        "UI" or "UO" => "UOP",
        "SI" or "SO" => "SOP",
        "WI" or "WO" or "WSI" or "WSO" => "Weld",
        "F" => "Flags",
        "M" => "Markers",
        _ when SafeTypes.Contains(type) => "Safe I/O (DCS / CIP Safety)",
        _ => "Other",
    };

    public static string DirectionOf(string type) => type switch
    {
        "F" or "M" or "SIR" => "Internal",
        _ when type.EndsWith('I') => "Input",
        _ when type.EndsWith('O') => "Output",
        _ => "Internal",
    };

    [GeneratedRegex(@"\b(?<type>[A-Za-z][A-Za-z0-9]{0,5})\[\s*(?<n>\d{1,6})\s*\]\s*(?:<[^>]{0,80}>\s*){0,6}(?:[=:]\s*)?(?<v>ON|OFF|TRUE|FALSE|-?\d+(?:\.\d+)?|-?\.\d+)(?![\w.])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Entry();

    [GeneratedRegex(@"<[^>]{0,200}>", RegexOptions.CultureInvariant)]
    private static partial Regex Markup();

    [GeneratedRegex(@"\s+", RegexOptions.CultureInvariant)]
    private static partial Regex Spaces();
}
