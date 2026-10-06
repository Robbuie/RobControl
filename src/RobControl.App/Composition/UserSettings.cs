// UseWPF drops System.IO from the implicit usings; this file is all paths.
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using RobControl.Core.Diagnostics;
using RobControl.Core.Sites;

namespace RobControl.App.Composition;

/// <summary>
/// What this PC remembers between runs that belongs to no one site: which site was open last.
///
/// <para>Its own file, <c>%LOCALAPPDATA%\RobControl\robcontrol.json</c>, and not
/// <c>settings.json</c>: that one is machine configuration (the update source) that the app must
/// never rewrite, exactly as in NetControl. This one the app writes whenever the site changes.</para>
///
/// <para><b>Before 0.3.0 the archive folder, schedule and the rest lived here too.</b> They belong to
/// a site now (<see cref="SiteSettings"/>). The old properties are still read, once, to make the
/// first site out of what the person had already chosen, and are left out of the file after that.</para>
/// </summary>
public sealed record UserSettings
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    /// <summary>The key (folder name) of the site open last time. Null on a first run.</summary>
    public string? Site { get; init; }

    // ---------------------------------------------------------------- 0.2.0 and earlier, read once

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ArchiveRoot { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? Concurrency { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? ScheduleHours { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? IncludeFrom { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? TrendRetentionDays { get; init; }

    public static string FilePath => Path.Combine(AppPaths.Data, "robcontrol.json");

    /// <summary>
    /// The site made from a 0.2.0 robot list, carrying over whatever 0.2.0 had been told.
    ///
    /// <para>The archive folder is the one that matters: it has to be <b>exactly</b> where 0.2.0 put
    /// the backups, or the robots' history would look empty. A folder somebody chose is kept; an
    /// unset one is 0.2.0's own default, <paramref name="oldDefaultArchive"/> - not the new per-site
    /// subfolder, which nothing has been written to yet.</para>
    /// </summary>
    public SiteSettings ToFirstSite(string name, string oldDefaultArchive)
    {
        var defaults = new SiteSettings();
        return defaults with
        {
            Name = name,
            ArchiveRoot = string.IsNullOrWhiteSpace(ArchiveRoot) ? oldDefaultArchive : ArchiveRoot,
            Concurrency = Concurrency ?? defaults.Concurrency,
            ScheduleHours = ScheduleHours ?? defaults.ScheduleHours,
            IncludeFrom = IncludeFrom ?? defaults.IncludeFrom,
            TrendRetentionDays = TrendRetentionDays ?? defaults.TrendRetentionDays,
        };
    }

    /// <summary>The same settings with the pre-site properties dropped, once they have been carried over.</summary>
    public UserSettings WithoutLegacy() => new() { Site = Site };

    /// <summary>Never throws: a file nobody can read gives the defaults, and the log says why.</summary>
    public static UserSettings Load(ITraceLog? trace = null)
    {
        try
        {
            if (File.Exists(FilePath))
            {
                return JsonSerializer.Deserialize<UserSettings>(File.ReadAllText(FilePath), Json) ?? new UserSettings();
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
