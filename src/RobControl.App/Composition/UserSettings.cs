// UseWPF drops System.IO from the implicit usings; this file is all paths.
using System.IO;
using System.Text.Json;
using RobControl.Core.Diagnostics;

namespace RobControl.App.Composition;

/// <summary>
/// What the person chose in the app: where backups go, how many robots at once, how often.
///
/// <para>Its own file, <c>%LOCALAPPDATA%\RobControl\robcontrol.json</c>, and not
/// <c>settings.json</c>: that one is site configuration (the update source) that the app must never
/// rewrite, exactly as in NetControl. This one the app writes every time something changes.</para>
/// </summary>
public sealed record UserSettings
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    /// <summary>The archive root. Defaults to Documents\RobControl Backups.</summary>
    public string ArchiveRoot { get; init; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments, Environment.SpecialFolderOption.DoNotVerify),
        "RobControl Backups");

    /// <summary>Robots backed up at the same time. Never more than one session per robot regardless.</summary>
    public int Concurrency { get; init; } = 2;

    /// <summary>Hours between automatic fleet backups while the app is open. Zero is off.</summary>
    public int ScheduleHours { get; init; }

    /// <summary>Also copy <c>fr:</c> (FROM). Off by default: it can be large, and md: is what matters.</summary>
    public bool IncludeFrom { get; init; }

    /// <summary>Trend samples older than this are pruned. Recording sessions stay in the event log regardless.</summary>
    public int TrendRetentionDays { get; init; } = 30;

    public static string FilePath => Path.Combine(AppPaths.Data, "robcontrol.json");

    /// <summary>Never throws: a file nobody can read gives the defaults, and the log says why.</summary>
    public static UserSettings Load(ITraceLog? trace = null)
    {
        try
        {
            if (File.Exists(FilePath))
            {
                UserSettings? loaded = JsonSerializer.Deserialize<UserSettings>(File.ReadAllText(FilePath), Json);
                if (loaded is not null)
                {
                    return loaded with
                    {
                        Concurrency = Math.Clamp(loaded.Concurrency, 1, 8),
                        ScheduleHours = Math.Clamp(loaded.ScheduleHours, 0, 168),
                        TrendRetentionDays = Math.Clamp(loaded.TrendRetentionDays, 1, 3650),
                        ArchiveRoot = string.IsNullOrWhiteSpace(loaded.ArchiveRoot) ? new UserSettings().ArchiveRoot : loaded.ArchiveRoot,
                    };
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            trace?.Warn($"{FilePath} could not be read; using defaults.", ex);
        }

        return new UserSettings();
    }

    /// <summary>Never throws either: a setting that did not save is a nuisance, not a reason to stop.</summary>
    public void Save(ITraceLog? trace = null)
    {
        try
        {
            Directory.CreateDirectory(AppPaths.Data);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, Json));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            trace?.Warn($"{FilePath} could not be written.", ex);
        }
    }
}
