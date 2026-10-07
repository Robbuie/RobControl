using System.Text.Json.Serialization;
using RobControl.Core.Transports.Ftp;

namespace RobControl.Core.Sites;

/// <summary>
/// How RobControl behaves at one plant: where its backups go, how hard it leans on the network,
/// and what a new robot's FTP login starts as. Stored as <c>site.json</c> in the site's folder.
///
/// <para><b>Plain JSON with readable names, on purpose.</b> An engineer can open it in Notepad,
/// change the archive folder, and the next time the site is opened it takes effect. Every value has
/// a working default and is clamped on load, so a hand-edit that goes wrong gives a site that works
/// rather than one that will not open.</para>
/// </summary>
public sealed record SiteSettings
{
    /// <summary>What the person calls the plant: "Lansing Delta", "Plant 3 body shop".</summary>
    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    /// <summary>
    /// Where this site's backups go. Defaults to <c>Documents\RobControl Backups\&lt;site&gt;</c>, so
    /// two plants with a robot called R1-01 never share a folder.
    /// </summary>
    [JsonPropertyName("archiveRoot")]
    public string ArchiveRoot { get; init; } = string.Empty;

    /// <summary>Robots backed up or probed at the same time. Never more than one session per robot regardless.</summary>
    [JsonPropertyName("concurrency")]
    public int Concurrency { get; init; } = 2;

    /// <summary>Hours between automatic fleet backups while the app is open on this site. Zero is off.</summary>
    [JsonPropertyName("scheduleHours")]
    public int ScheduleHours { get; init; }

    /// <summary>Also copy <c>fr:</c> (FROM). Off by default: it can be large, and md: is what matters.</summary>
    [JsonPropertyName("includeFrom")]
    public bool IncludeFrom { get; init; }

    /// <summary>Trend samples older than this are pruned. Recording sessions stay in the event log regardless.</summary>
    [JsonPropertyName("trendRetentionDays")]
    public int TrendRetentionDays { get; init; } = 30;

    /// <summary>
    /// The FTP login a robot added at this site starts with. A plant that has set up FTP users on
    /// every controller usually uses one login everywhere; this saves typing it per robot. Robots
    /// already in the list keep their own.
    /// </summary>
    [JsonPropertyName("defaultFtpUser")]
    public string DefaultFtpUser { get; init; } = FtpCredentials.Default.User;

    /// <summary>Plain text, like the per-robot password - see the note on the fleet schema.</summary>
    [JsonPropertyName("defaultFtpPassword")]
    public string DefaultFtpPassword { get; init; } = string.Empty;

    /// <summary>
    /// A robot whose last complete backup is older than this many days is flagged stale - in the
    /// Fleet tab and the site report. A week suits a weekly visit or a daily schedule.
    /// </summary>
    [JsonPropertyName("staleAfterDays")]
    public int StaleAfterDays { get; init; } = 7;

    /// <summary>
    /// How many times a scheduled fleet backup tries again, a few minutes later, the robots whose
    /// backup failed or came back partial. Backups started by hand are never retried - the person
    /// is there to see the result.
    /// </summary>
    [JsonPropertyName("scheduleRetries")]
    public int ScheduleRetries { get; init; } = 1;

    /// <summary>Minutes between a scheduled backup and its retry.</summary>
    [JsonPropertyName("retryDelayMinutes")]
    public int RetryDelayMinutes { get; init; } = 5;

    /// <summary>
    /// Complete backups per robot that <b>Prune old backups</b> keeps. Zero means the command is off.
    /// Nothing is ever pruned automatically: the command shows what would go and asks first, and
    /// what it removes goes to the Recycle Bin.
    /// </summary>
    [JsonPropertyName("keepBackups")]
    public int KeepBackups { get; init; }

    /// <summary>Anything worth knowing next visit: who to call, which VLAN, where the laptop plugs in.</summary>
    [JsonPropertyName("notes")]
    public string? Notes { get; init; }

    /// <summary>
    /// The same settings with every value in range. <paramref name="defaultArchiveRoot"/> fills an
    /// empty archive folder.
    /// </summary>
    public SiteSettings Normalised(string defaultArchiveRoot) => this with
    {
        Name = (Name ?? string.Empty).Trim(),
        ArchiveRoot = string.IsNullOrWhiteSpace(ArchiveRoot) ? defaultArchiveRoot : ArchiveRoot.Trim(),
        Concurrency = Math.Clamp(Concurrency, 1, 8),
        ScheduleHours = Math.Clamp(ScheduleHours, 0, 168),
        TrendRetentionDays = Math.Clamp(TrendRetentionDays, 1, 3650),
        StaleAfterDays = Math.Clamp(StaleAfterDays, 1, 365),
        ScheduleRetries = Math.Clamp(ScheduleRetries, 0, 3),
        RetryDelayMinutes = Math.Clamp(RetryDelayMinutes, 1, 60),
        KeepBackups = Math.Clamp(KeepBackups, 0, 1000),
        DefaultFtpUser = string.IsNullOrWhiteSpace(DefaultFtpUser) ? FtpCredentials.Default.User : DefaultFtpUser.Trim(),
        DefaultFtpPassword = DefaultFtpPassword ?? string.Empty,
        Notes = string.IsNullOrWhiteSpace(Notes) ? null : Notes.Trim(),
    };
}
