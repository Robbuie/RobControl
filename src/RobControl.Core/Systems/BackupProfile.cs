using System.Globalization;
using System.Text.Json.Serialization;
using RobControl.Core.Backup;

namespace RobControl.Core.Systems;

/// <summary>
/// What a backup takes, as written in <c>system.json</c> - once for the whole system and, optionally,
/// again per robot. <b>Every value is optional</b>: a robot's profile only says what differs from its
/// system's, and the system's only what differs from the built-in defaults. <see cref="Resolve"/>
/// lays them over each other.
///
/// <para>Plain names on purpose, so the file reads in Notepad:</para>
/// <code>
/// "backup": { "devices": [ "MD:", "FR:" ], "includeSubfolders": true, "exclude": [ "*.DG" ] }
/// </code>
/// </summary>
public sealed record BackupProfile
{
    /// <summary>At most this many devices in one backup. A typo'd list should not become fifty CWDs.</summary>
    public const int MaxDevices = 12;

    /// <summary>At most this many exclude patterns.</summary>
    public const int MaxExcludes = 50;

    /// <summary>Controller devices, in order: <c>MD:</c>, <c>FR:</c>, <c>FRA:</c>, <c>MC:</c>...</summary>
    [JsonPropertyName("devices")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<string>? Devices { get; init; }

    /// <summary>Also copy the folders inside a device (FR: and FRA: have them; MD: does not).</summary>
    [JsonPropertyName("includeSubfolders")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? IncludeSubfolders { get; init; }

    /// <summary>File-name patterns not to copy: <c>*.DG</c>, <c>LOGBOOK/*</c>. <c>*</c> and <c>?</c> only.</summary>
    [JsonPropertyName("exclude")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<string>? Exclude { get; init; }

    /// <summary>Milliseconds between files, for a controller that struggles. 0 by default.</summary>
    [JsonPropertyName("pauseBetweenFilesMs")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? PauseBetweenFilesMs { get; init; }

    /// <summary>Tries per file before it is recorded as a failure. 2 by default.</summary>
    [JsonPropertyName("attemptsPerFile")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? AttemptsPerFile { get; init; }

    /// <summary>True when nothing is set - a robot entry with this profile can be written without one.</summary>
    [JsonIgnore]
    public bool IsEmpty => Devices is null && IncludeSubfolders is null && Exclude is null
        && PauseBetweenFilesMs is null && AttemptsPerFile is null;

    /// <summary>
    /// <paramref name="over"/>'s values where it has them, this profile's where it does not. Lists are
    /// replaced, never merged: a robot that says <c>["MD:"]</c> means MD: only.
    /// </summary>
    public BackupProfile Overlay(BackupProfile? over) => over is null ? this : new BackupProfile
    {
        Devices = over.Devices ?? Devices,
        IncludeSubfolders = over.IncludeSubfolders ?? IncludeSubfolders,
        Exclude = over.Exclude ?? Exclude,
        PauseBetweenFilesMs = over.PauseBetweenFilesMs ?? PauseBetweenFilesMs,
        AttemptsPerFile = over.AttemptsPerFile ?? AttemptsPerFile,
    };

    /// <summary>
    /// The backup options for one robot: built-in defaults, then the site's FR: switch (for a system
    /// that has not chosen devices), then the system, then the robot. Every value clamped, every device
    /// checked, so a hand-edit that goes wrong gives a backup that still runs.
    /// </summary>
    public static BackupOptions Resolve(BackupProfile? system, BackupProfile? robot, bool siteIncludesFrom = false)
    {
        BackupProfile merged = new BackupProfile().Overlay(system).Overlay(robot);
        IReadOnlyList<string> devices = NormaliseDevices(merged.Devices);
        if (devices.Count == 0)
        {
            devices = siteIncludesFrom ? ["MD:", "FR:"] : ["MD:"];
        }

        return new BackupOptions
        {
            Devices = devices,
            IncludeSubfolders = merged.IncludeSubfolders ?? true,
            Exclude = NormaliseExcludes(merged.Exclude),
            PauseBetweenFiles = TimeSpan.FromMilliseconds(Math.Clamp(merged.PauseBetweenFilesMs ?? 0, 0, 10_000)),
            AttemptsPerFile = Math.Clamp(merged.AttemptsPerFile ?? 2, 1, 5),
        };
    }

    /// <summary>The profile that, resolved, gives <paramref name="options"/> - for writing a choice back.</summary>
    public static BackupProfile From(BackupOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return new BackupProfile
        {
            Devices = [.. options.Devices],
            IncludeSubfolders = options.IncludeSubfolders,
            Exclude = [.. options.Exclude],
            PauseBetweenFilesMs = (int)options.PauseBetweenFiles.TotalMilliseconds,
            AttemptsPerFile = options.AttemptsPerFile,
        };
    }

    /// <summary>
    /// Devices as the controller wants them - upper case, one colon - with anything that is not a
    /// plain device name dropped, and duplicates removed. Order kept.
    /// </summary>
    public static IReadOnlyList<string> NormaliseDevices(IEnumerable<string>? devices)
    {
        var result = new List<string>();
        foreach (string raw in devices ?? [])
        {
            if (TryNormaliseDevice(raw, out string? device) && !result.Contains(device!, StringComparer.Ordinal))
            {
                result.Add(device!);
            }

            if (result.Count == MaxDevices)
            {
                break;
            }
        }

        return result;
    }

    /// <summary>
    /// "fr", "FR:", " fr: " all become <c>FR:</c>. A device name is letters then optional digits, at
    /// most eight characters: <c>MD</c>, <c>FRA</c>, <c>UD1</c>, <c>UT1</c>. Anything else - a path, a
    /// wildcard, a space - is not a device, and would end up as a folder name in the archive.
    /// </summary>
    public static bool TryNormaliseDevice(string? raw, out string? device)
    {
        device = null;
        string name = (raw ?? string.Empty).Trim().TrimEnd(':').Trim().ToUpperInvariant();
        if (name.Length is 0 or > 8 || !char.IsAsciiLetterUpper(name[0]))
        {
            return false;
        }

        int i = 0;
        while (i < name.Length && char.IsAsciiLetterUpper(name[i]))
        {
            i++;
        }

        while (i < name.Length && char.IsAsciiDigit(name[i]))
        {
            i++;
        }

        if (i != name.Length)
        {
            return false;
        }

        device = name + ":";
        return true;
    }

    /// <summary>Trimmed, non-empty, de-duplicated patterns, forward slashes.</summary>
    public static IReadOnlyList<string> NormaliseExcludes(IEnumerable<string>? patterns) =>
        [.. (patterns ?? [])
            .Select(p => (p ?? string.Empty).Trim().Replace('\\', '/'))
            .Where(p => p.Length > 0 && p.Length <= 128)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(MaxExcludes)];

    /// <summary>"MD:, FR:" - how a device list reads in a sentence.</summary>
    public static string Describe(IEnumerable<string> devices) =>
        string.Join(", ", devices.Select(d => d.ToUpper(CultureInfo.InvariantCulture)));
}
