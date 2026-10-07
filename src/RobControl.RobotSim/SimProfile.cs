using System.Text.Json;

namespace RobControl.RobotSim;

/// <summary>
/// What the simulated controller is: a folder holding <c>profile.json</c>, one sub-folder per
/// device (<c>MD</c>, <c>FR</c>...) holding its files, and <c>kcl.json</c> mapping KCL commands to
/// the page the controller answers with.
///
/// <para>The probe tool writes exactly this shape from a real controller, so a capture from site
/// can be dropped into <c>tests/Fixtures</c> and replayed here unchanged.</para>
/// </summary>
public sealed record SimProfile
{
    public const string FileName = "profile.json";
    public const string KclFileName = "kcl.json";

    /// <summary>The 220 greeting, without the code.</summary>
    public string Banner { get; init; } = "FTP server ready.";

    /// <summary>Null accepts any user and any password, which is what a fresh controller does.</summary>
    public string? FtpUser { get; init; }

    public string? FtpPassword { get; init; }

    /// <summary>Reply to SYST, without the code.</summary>
    public string System { get; init; } = "UNIX Type: L8";

    /// <summary>The directory a session starts in, as PWD reports it.</summary>
    public string HomeDevice { get; init; } = "md:";

    /// <summary>Whether NLST names carry the device prefix (<c>md:SUMMARY.DG</c>).</summary>
    public bool NlstWithDevicePrefix { get; init; }

    public bool DiagnosticFilesLocked { get; init; }

    public bool KclLocked { get; init; }

    /// <summary>
    /// Send an HTTP <c>Date</c> header this many seconds off the PC's clock. Null sends none - whether
    /// a FANUC web server sends one at all is a Phase 0 question.
    /// </summary>
    public int? ClockOffsetSeconds { get; init; }

    /// <summary>
    /// The <c>Date</c> header carries the controller's local wall-clock time labelled GMT - what a
    /// controller with no idea of time zones would plausibly do.
    /// </summary>
    public bool ClockIsLocalTime { get; init; }

    /// <summary>Where <see cref="FileName"/> was read from. Device folders are beside it.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public string Folder { get; init; } = string.Empty;

    /// <summary>KCL command (normalised, any case) to the HTML page the controller sends back.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public IReadOnlyDictionary<string, string> Kcl { get; init; } = new Dictionary<string, string>();

    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true };

    public static SimProfile Load(string folder)
    {
        string full = Path.GetFullPath(folder);
        string path = Path.Combine(full, FileName);
        SimProfile profile = File.Exists(path)
            ? JsonSerializer.Deserialize<SimProfile>(File.ReadAllText(path), Json) ?? new SimProfile()
            : new SimProfile();

        string kclPath = Path.Combine(full, KclFileName);
        Dictionary<string, string> kcl = File.Exists(kclPath)
            ? JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(kclPath)) ?? []
            : [];

        return profile with
        {
            Folder = full,
            Kcl = new Dictionary<string, string>(kcl, StringComparer.OrdinalIgnoreCase),
        };
    }

    public void Save(string folder)
    {
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, FileName), JsonSerializer.Serialize(this, Json));
        File.WriteAllText(Path.Combine(folder, KclFileName), JsonSerializer.Serialize(Kcl, Json));
    }

    /// <summary>The folder for a device such as <c>md:</c>, or null if this controller has no such device.</summary>
    public string? DeviceFolder(string device)
    {
        string name = device.Trim().TrimEnd(':').TrimEnd('/', '\\').ToUpperInvariant();
        if (name.Length == 0 || name.AsSpan().IndexOfAny("/\\.") >= 0)
        {
            return null;
        }

        string path = Path.Combine(Folder, name);
        return Directory.Exists(path) ? path : null;
    }
}
