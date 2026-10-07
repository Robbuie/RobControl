using System.Globalization;
// UseWPF drops System.IO from the implicit usings; Path is used below.
using System.IO;
using RobControl.Core.Sites;
using RobControl.Core.Transports.Ftp;

namespace RobControl.App.ViewModels;

/// <summary>
/// What the site dialog edits: text, exactly as typed. It becomes <see cref="SiteSettings"/> only
/// through <see cref="TryBuild"/>, which says what is wrong - so the dialog and the view model cannot
/// disagree about what a valid site is.
/// </summary>
public sealed record SiteDraft
{
    public string Name { get; init; } = string.Empty;

    /// <summary>Blank means the default: a folder named after the site under Documents\RobControl Backups.</summary>
    public string ArchiveRoot { get; init; } = string.Empty;

    public string Concurrency { get; init; } = "2";

    /// <summary>Days before a robot's last complete backup counts as stale.</summary>
    public string StaleAfterDays { get; init; } = "7";

    /// <summary>Times a scheduled backup retries robots that did not complete.</summary>
    public string ScheduleRetries { get; init; } = "1";

    /// <summary>Complete backups per robot that Prune old backups keeps; 0 turns pruning off.</summary>
    public string KeepBackups { get; init; } = "0";

    public string DefaultFtpUser { get; init; } = FtpCredentials.Default.User;

    public string DefaultFtpPassword { get; init; } = string.Empty;

    public string Notes { get; init; } = string.Empty;

    public static SiteDraft From(SiteSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return new SiteDraft
        {
            Name = settings.Name,
            ArchiveRoot = settings.ArchiveRoot,
            Concurrency = settings.Concurrency.ToString(CultureInfo.InvariantCulture),
            StaleAfterDays = settings.StaleAfterDays.ToString(CultureInfo.InvariantCulture),
            ScheduleRetries = settings.ScheduleRetries.ToString(CultureInfo.InvariantCulture),
            KeepBackups = settings.KeepBackups.ToString(CultureInfo.InvariantCulture),
            DefaultFtpUser = settings.DefaultFtpUser,
            DefaultFtpPassword = settings.DefaultFtpPassword,
            Notes = settings.Notes ?? string.Empty,
        };
    }

    /// <summary>
    /// <paramref name="current"/> with this draft's values over it. What the dialog does not show -
    /// the schedule, FR:, trend retention - is kept from <paramref name="current"/>.
    /// </summary>
    public bool TryBuild(SiteSettings current, out SiteSettings? settings, out string? problem)
    {
        ArgumentNullException.ThrowIfNull(current);
        settings = null;

        if (!SiteCatalog.IsValidName(Name, out problem))
        {
            return false;
        }

        string archive = ArchiveRoot.Trim();
        if (archive.Length > 0 && !Path.IsPathFullyQualified(archive))
        {
            problem = $"'{archive}' is not a full folder path. Use Browse, or type one like D:\\Robot backups or \\\\server\\share\\robots.";
            return false;
        }

        if (!int.TryParse(Concurrency.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out int concurrency) || concurrency is < 1 or > 8)
        {
            problem = "Robots at once is a whole number from 1 to 8. Controller FTP servers are small; 2 is gentle.";
            return false;
        }

        if (!int.TryParse(StaleAfterDays.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out int stale) || stale is < 1 or > 365)
        {
            problem = "Stale after is a number of days from 1 to 365 - how old a robot's last good backup may be before it is flagged.";
            return false;
        }

        if (!int.TryParse(ScheduleRetries.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out int retries) || retries is < 0 or > 3)
        {
            problem = "Retries is a whole number from 0 to 3 - how often a scheduled backup tries again a robot that did not complete.";
            return false;
        }

        if (!int.TryParse(KeepBackups.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out int keep) || keep > 1000)
        {
            problem = "Keep is a whole number from 0 to 1000 complete backups per robot. 0 turns Prune old backups off.";
            return false;
        }

        settings = current with
        {
            Name = Name.Trim(),
            ArchiveRoot = archive,
            Concurrency = concurrency,
            StaleAfterDays = stale,
            ScheduleRetries = retries,
            KeepBackups = keep,
            DefaultFtpUser = string.IsNullOrWhiteSpace(DefaultFtpUser) ? FtpCredentials.Default.User : DefaultFtpUser.Trim(),
            DefaultFtpPassword = DefaultFtpPassword,
            Notes = string.IsNullOrWhiteSpace(Notes) ? null : Notes.Trim(),
        };
        return true;
    }
}
