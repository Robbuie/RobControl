using System.Globalization;
using System.Text.Json;
using RobControl.Core.Backup;
using RobControl.Core.Diagnostics;
using RobControl.Core.Robots;

namespace RobControl.Core.Sites;

/// <summary>
/// The sites on this PC: one folder each under <see cref="Root"/>, holding <c>site.json</c> and that
/// site's own fleet database.
///
/// <para><b>Fully separate, on purpose.</b> Two plants can both have an R1-01 at 10.10.1.11. With a
/// database and an archive folder per site, switching plants can never put one plant's robot in the
/// other's list, append one plant's backups to the other's history, or overlay their trends.</para>
///
/// <para>Sites live on this PC, not on a share: SQLite on a network share is a known way to corrupt
/// a database. A site travels as an export file (<see cref="Export"/>), and its backups travel as
/// the plain folders they already are.</para>
/// </summary>
public sealed class SiteCatalog
{
    public const int MaxNameLength = 64;

    private static readonly JsonSerializerOptions WriteJson = new() { WriteIndented = true };

    private static readonly JsonSerializerOptions ReadJson = new()
    {
        PropertyNameCaseInsensitive = true,
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
    };

    private readonly ITraceLog _trace;

    /// <param name="root">The folder the site folders are in: <c>%LOCALAPPDATA%\RobControl\sites</c>.</param>
    /// <param name="archiveBase">Where a new site's archive folder goes by default: <c>Documents\RobControl Backups</c>.</param>
    public SiteCatalog(string root, string archiveBase, ITraceLog? trace = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        ArgumentException.ThrowIfNullOrWhiteSpace(archiveBase);
        Root = Path.GetFullPath(root);
        ArchiveBase = archiveBase;
        _trace = trace ?? NullTraceLog.Instance;
    }

    public string Root { get; }

    public string ArchiveBase { get; }

    /// <summary>Where a site called <paramref name="name"/> keeps its backups unless told otherwise.</summary>
    public string DefaultArchiveRoot(string name) => Path.Combine(ArchiveBase, ArchiveNames.RobotFolder(name));

    /// <summary>
    /// Every site, sorted by name. A folder with a database but no readable <c>site.json</c> is still
    /// listed - under its folder name, with defaults - because hiding a site whose settings file
    /// somebody broke would look like losing its robots.
    /// </summary>
    public IReadOnlyList<Site> List()
    {
        if (!Directory.Exists(Root))
        {
            return [];
        }

        var sites = new List<Site>();
        foreach (string folder in Directory.EnumerateDirectories(Root))
        {
            if (File.Exists(Path.Combine(folder, Site.SettingsFileName)) || File.Exists(Path.Combine(folder, Site.DatabaseFileName)))
            {
                sites.Add(Load(folder));
            }
        }

        sites.Sort((a, b) => StringComparer.CurrentCultureIgnoreCase.Compare(a.Name, b.Name));
        return sites;
    }

    /// <summary>The site whose key (folder name) or name is <paramref name="keyOrName"/>, ignoring case.</summary>
    public Site? Find(string? keyOrName)
    {
        if (string.IsNullOrWhiteSpace(keyOrName))
        {
            return null;
        }

        string wanted = keyOrName.Trim();
        IReadOnlyList<Site> sites = List();
        return sites.FirstOrDefault(s => string.Equals(s.Key, wanted, StringComparison.OrdinalIgnoreCase))
            ?? sites.FirstOrDefault(s => string.Equals(s.Name, wanted, StringComparison.CurrentCultureIgnoreCase));
    }

    /// <summary>Reads one site folder. Never throws for a bad <c>site.json</c>: the log says why and defaults are used.</summary>
    public Site Load(string folder)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folder);
        string key = Path.GetFileName(Path.TrimEndingDirectorySeparator(folder));
        string path = Path.Combine(folder, Site.SettingsFileName);
        SiteSettings settings = new();

        try
        {
            if (File.Exists(path))
            {
                settings = JsonSerializer.Deserialize<SiteSettings>(File.ReadAllText(path), ReadJson) ?? new SiteSettings();
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or NotSupportedException)
        {
            _trace.Warn($"{path} could not be read; using defaults for this site.", ex);
        }

        if (string.IsNullOrWhiteSpace(settings.Name))
        {
            settings = settings with { Name = key };
        }

        settings = settings.Normalised(DefaultArchiveRoot(settings.Name));
        return new Site(key, folder, settings);
    }

    /// <summary>Makes a new, empty site. Its folder name is derived from the name and never changes after.</summary>
    public Site Create(SiteSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        string name = (settings.Name ?? string.Empty).Trim();
        EnsureNameUsable(name, except: null);

        string key = FreeKey(ArchiveNames.RobotFolder(name));
        string folder = Path.Combine(Root, key);
        try
        {
            Directory.CreateDirectory(folder);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new SiteException($"The folder for site '{name}' could not be created: {ex.Message}", ex)
            {
                Remediation = $"Check that {Root} is writable.",
            };
        }

        var site = new Site(key, folder, settings.Normalised(DefaultArchiveRoot(name)) with { Name = name });
        Write(site.SettingsPath, site.Settings);
        _trace.Info($"Site '{name}' created in {folder}.");
        return site;
    }

    /// <summary>Writes new settings for an existing site. The folder - and so the database - stays where it is.</summary>
    public Site Save(Site site, SiteSettings settings)
    {
        ArgumentNullException.ThrowIfNull(site);
        ArgumentNullException.ThrowIfNull(settings);
        string name = (settings.Name ?? string.Empty).Trim();
        EnsureNameUsable(name, except: site.Key);

        Site saved = site with { Settings = settings.Normalised(DefaultArchiveRoot(name)) with { Name = name } };
        Write(saved.SettingsPath, saved.Settings);
        return saved;
    }

    /// <summary>
    /// Makes the first site out of the database RobControl kept before there were sites (0.2.0 and
    /// earlier: <c>%LOCALAPPDATA%\RobControl\robcontrol.db</c>). The database file - and its WAL and
    /// shared-memory files, if a crash left them - is moved, not copied, so there is exactly one copy
    /// of the event log and nothing to drift.
    /// </summary>
    public Site Adopt(string legacyDatabase, SiteSettings settings)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(legacyDatabase);
        Site site = Create(settings);

        try
        {
            foreach (string suffix in new[] { string.Empty, "-wal", "-shm" })
            {
                string from = legacyDatabase + suffix;
                if (File.Exists(from))
                {
                    File.Move(from, site.DatabasePath + suffix);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new SiteException($"The robot list could not be moved into site '{site.Name}': {ex.Message}", ex)
            {
                Remediation = $"Close any other copy of RobControl and start it again. Nothing has been lost: the list is still at {legacyDatabase}.",
            };
        }

        _trace.Info($"Robot list from {legacyDatabase} moved into site '{site.Name}'.");
        return site;
    }

    /// <summary>Writes <paramref name="site"/> and <paramref name="robots"/> into one file someone else can import.</summary>
    public static void Export(Site site, IEnumerable<Robot> robots, string path, string? tool = null)
    {
        ArgumentNullException.ThrowIfNull(site);
        ArgumentNullException.ThrowIfNull(robots);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var file = new SiteFile
        {
            Format = SiteFile.FormatName,
            Version = SiteFile.CurrentVersion,
            ExportedUtc = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture),
            ExportedBy = tool,
            Settings = site.Settings,
            Robots = [.. robots.Select(SiteRobot.From)],
        };

        Write(path, file);
    }

    /// <summary>Reads an export, or says in one sentence why it is not one.</summary>
    public static SiteFile ReadExport(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        SiteFile? file;
        try
        {
            file = JsonSerializer.Deserialize<SiteFile>(File.ReadAllText(path), ReadJson);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new SiteException($"{path} could not be read: {ex.Message}", ex);
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException)
        {
            throw new SiteException($"{path} is not a RobControl site file: {ex.Message}", ex)
            {
                Remediation = "Pick a file written by Site > Export site.",
            };
        }

        if (file is null || !string.Equals(file.Format, SiteFile.FormatName, StringComparison.Ordinal))
        {
            throw new SiteException($"{path} is not a RobControl site file.")
            {
                Remediation = "Pick a file written by Site > Export site.",
            };
        }

        if (file.Version > SiteFile.CurrentVersion)
        {
            throw new SiteException($"{path} was written by a newer RobControl (site file version {file.Version}).")
            {
                Remediation = "Update RobControl on this PC, then import it again.",
            };
        }

        return file with { Robots = file.Robots ?? [], Settings = file.Settings ?? new SiteSettings() };
    }

    /// <summary>
    /// Makes a new site from an export's settings. The robots are the caller's to add, because they
    /// go into the site's database, which this class does not open.
    ///
    /// <para>A name already used here gets " (2)", " (3)"... rather than merging into the existing
    /// site: merging two robot lists by guesswork is how a robot ends up listed twice. An archive
    /// folder that does not exist on this PC (another laptop's D: drive) falls back to the default.</para>
    /// </summary>
    public Site Import(SiteFile file)
    {
        ArgumentNullException.ThrowIfNull(file);
        string baseName = string.IsNullOrWhiteSpace(file.Settings.Name) ? "Imported site" : file.Settings.Name.Trim();
        if (baseName.Length > MaxNameLength)
        {
            baseName = baseName[..MaxNameLength].TrimEnd();
        }

        string name = baseName;
        for (int n = 2; NameTaken(name, except: null); n++)
        {
            string suffix = string.Create(CultureInfo.InvariantCulture, $" ({n})");
            name = baseName[..Math.Min(baseName.Length, MaxNameLength - suffix.Length)] + suffix;
        }

        string archive = file.Settings.ArchiveRoot;
        if (string.IsNullOrWhiteSpace(archive) || !Directory.Exists(archive))
        {
            archive = DefaultArchiveRoot(name);
        }

        return Create(file.Settings with { Name = name, ArchiveRoot = archive });
    }

    /// <summary>Whether <paramref name="name"/> can name a site, and if not, why.</summary>
    public static bool IsValidName(string? name, out string? problem)
    {
        problem = null;
        string trimmed = (name ?? string.Empty).Trim();
        if (trimmed.Length == 0)
        {
            problem = "Give the site a name - the plant, or the plant and the shop.";
        }
        else if (trimmed.Length > MaxNameLength)
        {
            problem = string.Create(CultureInfo.InvariantCulture, $"Keep the site name to {MaxNameLength} characters.");
        }
        else if (trimmed.Any(char.IsControl))
        {
            problem = "The site name contains a control character.";
        }

        return problem is null;
    }

    private void EnsureNameUsable(string name, string? except)
    {
        if (!IsValidName(name, out string? problem))
        {
            throw new SiteException(problem!);
        }

        if (NameTaken(name, except))
        {
            throw new SiteException($"There is already a site called '{name}'.")
            {
                Remediation = "Pick another name, or switch to that site.",
            };
        }
    }

    private bool NameTaken(string name, string? except) =>
        List().Any(s => !string.Equals(s.Key, except, StringComparison.OrdinalIgnoreCase)
            && string.Equals(s.Name, name, StringComparison.CurrentCultureIgnoreCase));

    private string FreeKey(string wanted)
    {
        string key = wanted;
        for (int n = 2; Directory.Exists(Path.Combine(Root, key)) || File.Exists(Path.Combine(Root, key)); n++)
        {
            key = string.Create(CultureInfo.InvariantCulture, $"{wanted}-{n}");
        }

        return key;
    }

    /// <summary>Through a temporary file, so a crash mid-write leaves the old file rather than half a new one.</summary>
    private static void Write<T>(string path, T value)
    {
        string temp = path + ".tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            File.WriteAllText(temp, JsonSerializer.Serialize(value, WriteJson));
            File.Move(temp, path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new SiteException($"{path} could not be written: {ex.Message}", ex)
            {
                Remediation = "Check the folder is writable and the file is not open in another program.",
            };
        }
    }
}
