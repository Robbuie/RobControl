using System.Text.Json.Serialization;

namespace RobControl.Core.Sites;

/// <summary>
/// A whole site in one file, to carry to another laptop or hand to a colleague: the settings and the
/// robot list. Written by <see cref="SiteCatalog.Export"/>, read by <see cref="SiteCatalog.ReadExport"/>.
///
/// <para>It holds FTP passwords in plain text when robots have them, exactly as the fleet database
/// does. Treat the file like the list of controller logins it is.</para>
/// </summary>
public sealed record SiteFile
{
    /// <summary>What this file is, so a stray JSON file is refused with a sentence rather than half-read.</summary>
    public const string FormatName = "robcontrol-site";

    public const int CurrentVersion = 1;

    /// <summary>
    /// <see cref="FormatName"/> in every file RobControl writes. No default on purpose: a JSON file
    /// without it - somebody's settings.json picked by mistake - must read as "not a site file".
    /// </summary>
    [JsonPropertyName("format")]
    public string? Format { get; init; }

    [JsonPropertyName("version")]
    public int Version { get; init; } = CurrentVersion;

    /// <summary>Round-trip UTC. For the person reading the file; nothing depends on it.</summary>
    [JsonPropertyName("exportedUtc")]
    public string? ExportedUtc { get; init; }

    /// <summary>The build that wrote it.</summary>
    [JsonPropertyName("exportedBy")]
    public string? ExportedBy { get; init; }

    [JsonPropertyName("settings")]
    public SiteSettings Settings { get; init; } = new();

    [JsonPropertyName("robots")]
    public IReadOnlyList<SiteRobot> Robots { get; init; } = [];
}
