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

        settings = current with
        {
            Name = Name.Trim(),
            ArchiveRoot = archive,
            Concurrency = concurrency,
            DefaultFtpUser = string.IsNullOrWhiteSpace(DefaultFtpUser) ? FtpCredentials.Default.User : DefaultFtpUser.Trim(),
            DefaultFtpPassword = DefaultFtpPassword,
            Notes = string.IsNullOrWhiteSpace(Notes) ? null : Notes.Trim(),
        };
        return true;
    }
}
