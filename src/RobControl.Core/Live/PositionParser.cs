using System.Globalization;
using System.Text.RegularExpressions;
using RobControl.Core.Transports.Http;

namespace RobControl.Core.Live;

/// <summary>
/// Reads <c>CURPOS.DG</c>. As seen on an R-30iB, V8.30 RepairTool+ (November 2026), per group:
/// <code>
/// Group #:  1
/// CURRENT JOINT POSITION:
/// Joint   1:     69.05
/// EXTAXS: 1:  -3500.02
///
/// Frame #:  0  Tool #:  1
/// CURRENT USER FRAME POSITION:
/// CFG: N U T, 0, 0, 0
/// X:    199.99
/// ...
/// P:       .00
///
/// Tool #:  1
/// CURRENT WORLD POSITION:
/// </code>
/// </summary>
public static partial class PositionParser
{
    public const string Joint = "Joint";
    public const string UserFrame = "User frame";
    public const string World = "World";

    public static IReadOnlyList<PositionFrame> Parse(string? text)
    {
        var frames = new List<PositionFrame>();
        string body = DiagnosticPage.Text(text);
        int group = 1;
        int? userFrame = null;
        int? tool = null;

        string? kind = null;
        string? config = null;
        var axes = new List<AxisValue>();

        foreach (string raw in body.Split('\n'))
        {
            string line = raw.Trim();
            Match m;
            if ((m = GroupLine().Match(line)).Success)
            {
                Flush();
                group = Int(m.Groups["g"].Value) ?? group;
                userFrame = null;
                tool = null;
            }
            else if ((m = FrameToolLine().Match(line)).Success)
            {
                Flush();
                userFrame = m.Groups["f"].Success ? Int(m.Groups["f"].Value) : null;
                tool = Int(m.Groups["t"].Value);
            }
            else if ((m = Heading().Match(line)).Success)
            {
                Flush();
                string what = m.Groups["what"].Value.Trim().ToUpperInvariant();
                kind = what switch
                {
                    "JOINT" => Joint,
                    "USER FRAME" or "USER" => UserFrame,
                    "WORLD" => World,
                    "ROBOT" => null, // "CURRENT ROBOT POSITION::" - the page title, not a block
                    _ => CultureInfo.InvariantCulture.TextInfo.ToTitleCase(what.ToLowerInvariant()),
                };
            }
            else if (kind is null)
            {
                continue;
            }
            else if ((m = JointLine().Match(line)).Success && Number(m.Groups["v"].Value) is { } j)
            {
                axes.Add(new AxisValue("J" + m.Groups["n"].Value, j));
            }
            else if ((m = ExtLine().Match(line)).Success && Number(m.Groups["v"].Value) is { } e)
            {
                axes.Add(new AxisValue("E" + m.Groups["n"].Value, e));
            }
            else if ((m = ConfigLine().Match(line)).Success)
            {
                config = m.Groups["c"].Value.Trim();
            }
            else if ((m = CartesianLine().Match(line)).Success && Number(m.Groups["v"].Value) is { } c)
            {
                axes.Add(new AxisValue(m.Groups["a"].Value.ToUpperInvariant(), c));
            }
        }

        Flush();
        return frames;

        void Flush()
        {
            if (kind is not null && axes.Count > 0)
            {
                bool joint = kind == Joint;
                frames.Add(new PositionFrame(group, kind, joint ? null : userFrame, joint ? null : tool, joint ? null : config, [.. axes]));
            }

            kind = null;
            config = null;
            axes.Clear();
        }
    }

    private static int? Int(string text) =>
        int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out int n) ? n : null;

    private static double? Number(string text) =>
        double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double v) && double.IsFinite(v) ? v : null;

    [GeneratedRegex(@"^Group\s*#\s*:\s*(?<g>\d+)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex GroupLine();

    [GeneratedRegex(@"^(?:Frame\s*#\s*:\s*(?<f>\d+)\s+)?Tool\s*#\s*:\s*(?<t>\d+)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex FrameToolLine();

    [GeneratedRegex(@"^CURRENT\s+(?<what>.+?)\s+POSITION\s*:+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Heading();

    [GeneratedRegex(@"^Joint\s+(?<n>\d+)\s*:\s*(?<v>[-+]?\d*\.?\d+(?:[eE][-+]?\d+)?)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex JointLine();

    [GeneratedRegex(@"^EXTAXS\s*:\s*(?<n>\d+)\s*:\s*(?<v>[-+]?\d*\.?\d+(?:[eE][-+]?\d+)?)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ExtLine();

    [GeneratedRegex(@"^CFG\s*:\s*(?<c>.+)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ConfigLine();

    [GeneratedRegex(@"^(?<a>[XYZWPR])\s*:\s*(?<v>[-+]?\d*\.?\d+(?:[eE][-+]?\d+)?)\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex CartesianLine();
}
