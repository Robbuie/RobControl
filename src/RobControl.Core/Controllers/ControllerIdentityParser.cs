using System.Globalization;
using System.Text.RegularExpressions;

namespace RobControl.Core.Controllers;

/// <summary>
/// Finds a controller's identity in whatever text it gave us: the FTP banner, a summary or version
/// diagnostic file, a <c>SHOW VAR $VERSION</c> reply.
///
/// <para><b>Deliberately a searcher, not a parser of one format.</b> The layout of these files
/// differs between generations and we have not yet seen ours (Phase 0). So this looks for the
/// things that are distinctive wherever they appear - a version string shaped like <c>V9.40P/23</c>,
/// a cabinet name, an application name - and ignores the layout around them. When a real file
/// arrives that this misreads, it becomes a test fixture and this gets a case for it.</para>
/// </summary>
public static partial class ControllerIdentityParser
{
    // Longest first, so "SpotTool+" wins over "SpotTool" and "LR HandlingTool" over "HandlingTool".
    private static readonly string[] Applications =
    [
        "SpotTool+", "LR HandlingTool", "HandlingTool", "PaintTool", "SpotTool", "ArcTool",
        "DispenseTool", "PalletTool", "LR Tool", "SealTool",
    ];

    public static ControllerIdentity Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return ControllerIdentity.Unknown;
        }

        var identity = new ControllerIdentity();

        Match version = Version().Match(text);
        if (version.Success)
        {
            int major = int.Parse(version.Groups["major"].Value, NumberStyles.None, CultureInfo.InvariantCulture);
            int minor = int.Parse(version.Groups["minor"].Value, NumberStyles.None, CultureInfo.InvariantCulture);
            identity = identity with
            {
                SoftwareVersion = version.Value.Replace(" ", string.Empty, StringComparison.Ordinal),
                VersionMajor = major,
                VersionMinor = minor,
            };
        }

        Match cabinet = Cabinet().Match(text);
        if (cabinet.Success)
        {
            identity = identity with
            {
                Generation = FromCabinet(cabinet.Groups["family"].Value, cabinet.Groups["plus"].Success),
                GenerationInferred = false,
            };
        }
        else if (identity.VersionMajor is int major)
        {
            ControllerGeneration inferred = FromMajorVersion(major);
            if (inferred != ControllerGeneration.Unknown)
            {
                identity = identity with { Generation = inferred, GenerationInferred = true };
            }
        }

        foreach (string application in Applications)
        {
            if (text.Contains(application, StringComparison.OrdinalIgnoreCase))
            {
                identity = identity with { Application = application };
                break;
            }
        }

        Match model = Model().Match(text);
        if (model.Success)
        {
            identity = identity with { RobotModel = model.Value.Trim() };
        }

        Match fnumber = FNumber().Match(text);
        if (fnumber.Success)
        {
            identity = identity with { FNumber = fnumber.Groups["f"].Value.ToUpperInvariant() };
        }

        return identity;
    }

    /// <summary>V7 is iA, V8 is iB, V9 is iB Plus, V10 is R-50iA. Used only when the text names no cabinet.</summary>
    public static ControllerGeneration FromMajorVersion(int major) => major switch
    {
        7 => ControllerGeneration.R30iA,
        8 => ControllerGeneration.R30iB,
        9 => ControllerGeneration.R30iBPlus,
        >= 10 => ControllerGeneration.R50iA,
        _ => ControllerGeneration.Unknown,
    };

    private static ControllerGeneration FromCabinet(string family, bool plus) =>
        family.ToUpperInvariant().Replace("-", string.Empty, StringComparison.Ordinal) switch
    {
        "R30IA" => ControllerGeneration.R30iA,
        "R30IB" => plus ? ControllerGeneration.R30iBPlus : ControllerGeneration.R30iB,
        "R50IA" => ControllerGeneration.R50iA,
        _ => ControllerGeneration.Unknown,
    };

    // V9.40P/23, V8.30P/16, V7.70P/41, V9.40, V9.40P - the build number is optional.
    [GeneratedRegex(@"\bV(?<major>\d{1,2})\.(?<minor>\d{2})(?:P)?(?:\s*/\s*\d{1,3})?", RegexOptions.IgnoreCase)]
    private static partial Regex Version();

    // R-30iB Plus, R-30iB Mate Plus, R-30iB Compact Plus, R-30iA Mate, R30iB.
    [GeneratedRegex(@"\b(?<family>R-?30i[AB]|R-?50iA)(?:\s+(?:Mate|Compact))?(?<plus>\s+Plus)?\b", RegexOptions.IgnoreCase)]
    private static partial Regex Cabinet();

    // R-2000iC/210F, M-20iA/35M, LR Mate 200iD/7L, M-710iC/50, P-250iB/15, ARC Mate 120iC.
    [GeneratedRegex(@"\b(?:R-2000i[ABCD]|R-1000i[AB]|M-\d{1,4}i[ABCD]|LR\s+Mate\s+200i[BCD]|ARC\s+Mate\s+\d{2,3}i[BCD]|P-\d{2,3}i[ABC])(?:/[\w.]+)?", RegexOptions.IgnoreCase)]
    private static partial Regex Model();

    // "F Number: F123456", "F-Number : 123456", "F#: F12345".
    [GeneratedRegex(@"\bF(?:\s*-\s*|\s+)?(?:Number|No\.?|#)\s*[:=]?\s*(?<f>F?\d{5,7})\b", RegexOptions.IgnoreCase)]
    private static partial Regex FNumber();
}
