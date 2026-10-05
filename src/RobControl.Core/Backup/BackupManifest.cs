using System.Text.Json;
using System.Text.Json.Serialization;
using RobControl.Core.Controllers;

namespace RobControl.Core.Backup;

/// <summary>
/// <c>manifest.json</c>, at the top of every backup folder. The source of truth for that backup:
/// the app's history list is built by reading these, so a folder copied to another laptop - or a
/// network share - brings its history with it, and nothing needs a database to make sense of it.
/// </summary>
public sealed record BackupManifest
{
    public const string FileName = "manifest.json";

    /// <summary>Bumped if the shape ever changes incompatibly. Readers refuse what they do not know.</summary>
    public int Format { get; init; } = 1;

    /// <summary>The build that took it: "RobControl 0.1.0+a1b2c3d".</summary>
    public required string Tool { get; init; }

    public required string RobotName { get; init; }

    public required string Address { get; init; }

    public string? Line { get; init; }

    public ControllerIdentity Identity { get; init; } = ControllerIdentity.Unknown;

    public DateTimeOffset StartedUtc { get; init; }

    public DateTimeOffset FinishedUtc { get; init; }

    [JsonConverter(typeof(JsonStringEnumConverter<BackupOutcome>))]
    public BackupOutcome Outcome { get; init; }

    /// <summary>One sentence: why it ended the way it did.</summary>
    public string? Summary { get; init; }

    public IReadOnlyList<string> Devices { get; init; } = [];

    public IReadOnlyList<BackupFileRecord> Files { get; init; } = [];

    /// <summary>Listed but did not arrive.</summary>
    public IReadOnlyList<BackupFileProblem> Failures { get; init; } = [];

    /// <summary>Listed but deliberately not fetched - a name that is not safe to write to disk.</summary>
    public IReadOnlyList<BackupFileProblem> Skipped { get; init; } = [];

    [JsonIgnore]
    public long TotalBytes => Files.Sum(f => f.Bytes);

    internal static JsonSerializerOptions Json { get; } = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter<ControllerGeneration>() },
    };

    public string ToJson() => JsonSerializer.Serialize(this, Json);

    public static BackupManifest FromJson(string json)
    {
        BackupManifest manifest = JsonSerializer.Deserialize<BackupManifest>(json, Json)
            ?? throw new BackupArchiveException("The manifest is empty.");

        if (manifest.Format != 1)
        {
            throw new BackupArchiveException($"The manifest is format {manifest.Format}; this build reads format 1.")
            {
                Remediation = "The backup was written by a newer RobControl. Update this copy.",
            };
        }

        return manifest;
    }
}
