using System.Text.Json.Serialization;

namespace RobControl.Core.Controllers;

/// <summary>
/// What a controller says it is. Every field is optional because every source is partial: the FTP
/// banner may name the version and nothing else, a summary file may be locked, KCL may be locked.
///
/// <para><see cref="GenerationInferred"/> is the honest bit. When the text named the cabinet
/// ("R-30iB Plus") it is false; when the generation was worked out from the software version
/// (V9.x means iB Plus) it is true, and the UI says "probably".</para>
/// </summary>
public sealed record ControllerIdentity
{
    public static ControllerIdentity Unknown { get; } = new();

    public ControllerGeneration Generation { get; init; }

    public bool GenerationInferred { get; init; }

    /// <summary>As the controller wrote it, e.g. <c>V9.40P/23</c>.</summary>
    public string? SoftwareVersion { get; init; }

    /// <summary>9 for V9.40P/23.</summary>
    public int? VersionMajor { get; init; }

    /// <summary>40 for V9.40P/23.</summary>
    public int? VersionMinor { get; init; }

    /// <summary>SpotTool+, PaintTool, HandlingTool...</summary>
    public string? Application { get; init; }

    /// <summary>The arm, e.g. <c>R-2000iC/210F</c>, when the text names one.</summary>
    public string? RobotModel { get; init; }

    /// <summary>FANUC's F-number - the serial that support asks for first.</summary>
    public string? FNumber { get; init; }

    [JsonIgnore]
    public bool IsEmpty =>
        Generation == ControllerGeneration.Unknown && SoftwareVersion is null && Application is null
            && RobotModel is null && FNumber is null;

    /// <summary>"R-30iB Plus, SpotTool+ V9.40P/23" - or as much of it as is known.</summary>
    public string Describe()
    {
        if (IsEmpty)
        {
            return "Not identified yet";
        }

        var parts = new List<string>();
        if (Generation != ControllerGeneration.Unknown)
        {
            parts.Add((GenerationInferred ? "probably " : string.Empty) + Name(Generation));
        }

        string app = string.Join(' ', new[] { Application, SoftwareVersion }.Where(s => s is not null));
        if (app.Length > 0)
        {
            parts.Add(app);
        }

        if (RobotModel is not null)
        {
            parts.Add(RobotModel);
        }

        return string.Join(", ", parts);
    }

    /// <summary>
    /// Fills gaps in this identity from <paramref name="other"/>. Never overwrites a stated value
    /// with an inferred one.
    /// </summary>
    public ControllerIdentity Merge(ControllerIdentity other)
    {
        ArgumentNullException.ThrowIfNull(other);

        bool takeGeneration = Generation == ControllerGeneration.Unknown
            || (GenerationInferred && !other.GenerationInferred && other.Generation != ControllerGeneration.Unknown);

        return this with
        {
            Generation = takeGeneration ? other.Generation : Generation,
            GenerationInferred = takeGeneration ? other.GenerationInferred : GenerationInferred,
            SoftwareVersion = SoftwareVersion ?? other.SoftwareVersion,
            VersionMajor = VersionMajor ?? other.VersionMajor,
            VersionMinor = VersionMinor ?? other.VersionMinor,
            Application = Application ?? other.Application,
            RobotModel = RobotModel ?? other.RobotModel,
            FNumber = FNumber ?? other.FNumber,
        };
    }

    public static string Name(ControllerGeneration generation) => generation switch
    {
        ControllerGeneration.R30iA => "R-30iA",
        ControllerGeneration.R30iB => "R-30iB",
        ControllerGeneration.R30iBPlus => "R-30iB Plus",
        ControllerGeneration.R50iA => "R-50iA",
        _ => "unknown controller",
    };
}
