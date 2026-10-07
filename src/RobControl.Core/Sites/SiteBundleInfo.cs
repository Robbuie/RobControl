using System.Text.Json.Serialization;

namespace RobControl.Core.Sites;

/// <summary>
/// <c>bundle.json</c>, first in every site bundle: what is inside, so an import can say so before it
/// unpacks anything.
/// </summary>
public sealed record SiteBundleInfo
{
    public const string FormatName = "robcontrol-site-bundle";

    public const int CurrentVersion = 1;

    [JsonPropertyName("format")]
    public string? Format { get; init; }

    [JsonPropertyName("version")]
    public int Version { get; init; } = CurrentVersion;

    [JsonPropertyName("exportedUtc")]
    public string? ExportedUtc { get; init; }

    [JsonPropertyName("exportedBy")]
    public string? ExportedBy { get; init; }

    [JsonPropertyName("siteName")]
    public string SiteName { get; init; } = string.Empty;

    [JsonPropertyName("robots")]
    public int Robots { get; init; }

    /// <summary>The site's database is inside: event log, last probes, trend history.</summary>
    [JsonPropertyName("database")]
    public bool Database { get; init; }

    /// <summary>Backup folders inside, across every robot.</summary>
    [JsonPropertyName("backupFolders")]
    public int BackupFolders { get; init; }

    [JsonPropertyName("backupBytes")]
    public long BackupBytes { get; init; }
}
