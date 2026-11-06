using System.Globalization;
using System.Text.Json;
using RobControl.Core.Backup;
using RobControl.Core.Diagnostics;
using RobControl.Core.Robots;

namespace RobControl.Core.Systems;

/// <summary>
/// The systems of each site, as folders under the site's config folder:
/// <c>&lt;config root&gt;\&lt;site key&gt;\&lt;system key&gt;\system.json</c>.
///
/// <para><b>Only files here.</b> A system is a name, a few backup settings and a list of robot
/// addresses. The robots themselves - and their probes, events and trends - stay in the site's
/// database, which never moves into a folder that might be synced or shared (SQLite on a share is
/// how a database gets corrupted). So copying a system folder to another laptop brings its settings;
/// a site bundle brings the rest.</para>
///
/// <para><b>Every robot of a site belongs to a system.</b> A site that has robots and no systems
/// (anything from before 0.6.0, or a fresh import) gets one called <see cref="DefaultName"/> holding
/// all of them - <see cref="EnsureAny"/>. Robots no system lists after that (a hand-edited file) are
/// shown under <see cref="RobotSystem.Unassigned"/>, never hidden.</para>
/// </summary>
public sealed class SystemCatalog
{
    public const int MaxNameLength = 64;

    /// <summary>What a site's first system is called. Renamable.</summary>
    public const string DefaultName = "Main";

    private static readonly JsonSerializerOptions WriteJson = new() { WriteIndented = true };

    private static readonly JsonSerializerOptions ReadJson = new()
    {
        PropertyNameCaseInsensitive = true,
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
    };

    private readonly ITraceLog _trace;

    /// <param name="root">The config root: <c>Documents\RobControl\Systems</c>. One folder per site under it.</param>
    public SystemCatalog(string root, ITraceLog? trace = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        Root = Path.GetFullPath(root);
        _trace = trace ?? NullTraceLog.Instance;
    }

    public string Root { get; }

    /// <summary>The folder that holds a site's <c>site.json</c> and its system folders.</summary>
    public string SiteFolder(string siteKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(siteKey);
        return Path.Combine(Root, siteKey);
    }

    /// <summary>The site's systems, by name. Never throws for a broken <c>system.json</c>: it is listed with defaults.</summary>
    public IReadOnlyList<RobotSystem> List(string siteKey)
    {
        string folder = SiteFolder(siteKey);
        if (!Directory.Exists(folder))
        {
            return [];
        }

        var systems = new List<RobotSystem>();
        foreach (string sub in Directory.EnumerateDirectories(folder))
        {
            if (File.Exists(Path.Combine(sub, RobotSystem.SettingsFileName)))
            {
                systems.Add(Load(siteKey, sub));
            }
        }

        systems.Sort((a, b) => StringComparer.CurrentCultureIgnoreCase.Compare(a.Name, b.Name));
        return systems;
    }

    /// <summary>The system whose key or name is <paramref name="keyOrName"/>, ignoring case.</summary>
    public RobotSystem? Find(string siteKey, string? keyOrName)
    {
        if (string.IsNullOrWhiteSpace(keyOrName))
        {
            return null;
        }

        string wanted = keyOrName.Trim();
        if (wanted == RobotSystem.UnassignedKey)
        {
            return RobotSystem.Unassigned(siteKey);
        }

        IReadOnlyList<RobotSystem> systems = List(siteKey);
        return systems.FirstOrDefault(s => string.Equals(s.Key, wanted, StringComparison.OrdinalIgnoreCase))
            ?? systems.FirstOrDefault(s => string.Equals(s.Name, wanted, StringComparison.CurrentCultureIgnoreCase));
    }

    /// <summary>Reads one system folder. A bad file gives defaults and a line in the log, never an exception.</summary>
    public RobotSystem Load(string siteKey, string folder)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folder);
        string key = Path.GetFileName(Path.TrimEndingDirectorySeparator(folder));
        string path = Path.Combine(folder, RobotSystem.SettingsFileName);
        SystemSettings settings = new();

        try
        {
            if (File.Exists(path))
            {
                settings = JsonSerializer.Deserialize<SystemSettings>(File.ReadAllText(path), ReadJson) ?? new SystemSettings();
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or NotSupportedException)
        {
            _trace.Warn($"{path} could not be read; using defaults for this system.", ex);
        }

        settings = settings.Normalised();
        if (settings.Name.Length == 0)
        {
            settings = settings with { Name = key };
        }

        return new RobotSystem(siteKey, key, folder, settings);
    }

    /// <summary>Makes a new system in a site. Its folder name comes from the name and never changes.</summary>
    public RobotSystem Create(string siteKey, SystemSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        string name = (settings.Name ?? string.Empty).Trim();
        EnsureNameUsable(siteKey, name, except: null);

        string siteFolder = SiteFolder(siteKey);
        string key = FreeKey(siteFolder, ArchiveNames.RobotFolder(name));
        string folder = Path.Combine(siteFolder, key);
        try
        {
            Directory.CreateDirectory(folder);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new RobotSystemException($"The folder for system '{name}' could not be created: {ex.Message}", ex)
            {
                Remediation = $"Check that {siteFolder} is writable.",
            };
        }

        var system = new RobotSystem(siteKey, key, folder, settings.Normalised() with { Name = name });
        Write(system.SettingsPath, system.Settings);
        _trace.Info($"System '{name}' created in {folder}.");
        return system;
    }

    /// <summary>Writes new settings for an existing system. Its folder stays where it is.</summary>
    public RobotSystem Save(RobotSystem system, SystemSettings settings)
    {
        ArgumentNullException.ThrowIfNull(system);
        ArgumentNullException.ThrowIfNull(settings);
        if (system.IsUnassigned)
        {
            throw new RobotSystemException("Robots that are not in a system have no settings of their own.")
            {
                Remediation = "Move the robot into a system first: Robots > Move to system.",
            };
        }

        string name = (settings.Name ?? string.Empty).Trim();
        EnsureNameUsable(system.SiteKey, name, except: system.Key);
        RobotSystem saved = system with { Settings = settings.Normalised() with { Name = name } };
        Write(saved.SettingsPath, saved.Settings);
        return saved;
    }

    /// <summary>The system that lists <paramref name="robot"/> - the first by name if, by hand-editing, more than one does.</summary>
    public RobotSystem? SystemOf(string siteKey, Robot robot) => List(siteKey).FirstOrDefault(s => s.Settings.Contains(robot));

    /// <summary>
    /// <paramref name="robots"/> that no system of the site lists. Shown under "Not in a system" so a
    /// robot can never disappear because somebody edited a file.
    /// </summary>
    public IReadOnlyList<Robot> Unlisted(string siteKey, IEnumerable<Robot> robots)
    {
        ArgumentNullException.ThrowIfNull(robots);
        IReadOnlyList<RobotSystem> systems = List(siteKey);
        return [.. robots.Where(r => !systems.Any(s => s.Settings.Contains(r)))];
    }

    /// <summary>
    /// Gives a site its first system when it has none: <see cref="DefaultName"/>, listing every robot
    /// it already has. Returns the systems after, never empty.
    /// </summary>
    public IReadOnlyList<RobotSystem> EnsureAny(string siteKey, IEnumerable<Robot> robots)
    {
        ArgumentNullException.ThrowIfNull(robots);
        IReadOnlyList<RobotSystem> systems = List(siteKey);
        if (systems.Count > 0)
        {
            return systems;
        }

        var settings = new SystemSettings
        {
            Name = DefaultName,
            Robots = [.. robots.Select(r => SystemRobot.From(r))],
        };
        RobotSystem made = Create(siteKey, settings);
        _trace.Info($"Site '{siteKey}' had no systems; '{made.Name}' now holds its {made.Settings.Robots.Count} robot(s).");
        return [made];
    }

    /// <summary>
    /// Moves <paramref name="robot"/> into <paramref name="to"/>, taking its own backup settings with
    /// it, and out of every other system of the site that lists it. Returns the destination as saved.
    /// </summary>
    public RobotSystem Move(Robot robot, RobotSystem to)
    {
        ArgumentNullException.ThrowIfNull(robot);
        ArgumentNullException.ThrowIfNull(to);
        if (to.IsUnassigned)
        {
            throw new RobotSystemException("Pick a system to move the robot into.");
        }

        BackupProfile? carried = null;
        foreach (RobotSystem other in List(to.SiteKey).Where(s => s.Key != to.Key && s.Settings.Contains(robot)))
        {
            carried ??= other.Settings.EntryFor(robot)?.Backup;
            Save(other, other.Settings.Without(robot));
        }

        RobotSystem fresh = Find(to.SiteKey, to.Key) ?? to;
        return Save(fresh, fresh.Settings.With(robot, carried));
    }

    /// <summary>Whether <paramref name="name"/> can name a system, and if not, why.</summary>
    public static bool IsValidName(string? name, out string? problem)
    {
        problem = null;
        string trimmed = (name ?? string.Empty).Trim();
        if (trimmed.Length == 0)
        {
            problem = "Give the system a name - the line, the cell, the job number.";
        }
        else if (trimmed.Length > MaxNameLength)
        {
            problem = string.Create(CultureInfo.InvariantCulture, $"Keep the system name to {MaxNameLength} characters.");
        }
        else if (trimmed.Any(char.IsControl))
        {
            problem = "The system name contains a control character.";
        }

        return problem is null;
    }

    private void EnsureNameUsable(string siteKey, string name, string? except)
    {
        if (!IsValidName(name, out string? problem))
        {
            throw new RobotSystemException(problem!);
        }

        if (List(siteKey).Any(s => !string.Equals(s.Key, except, StringComparison.OrdinalIgnoreCase)
            && string.Equals(s.Name, name, StringComparison.CurrentCultureIgnoreCase)))
        {
            throw new RobotSystemException($"This site already has a system called '{name}'.")
            {
                Remediation = "Pick another name, or open that system.",
            };
        }
    }

    private static string FreeKey(string parent, string wanted)
    {
        string key = wanted;
        for (int n = 2; Directory.Exists(Path.Combine(parent, key)) || File.Exists(Path.Combine(parent, key)); n++)
        {
            key = string.Create(CultureInfo.InvariantCulture, $"{wanted}-{n}");
        }

        return key;
    }

    /// <summary>Through a temporary file, so a crash mid-write leaves the old file rather than half a new one.</summary>
    private static void Write(string path, SystemSettings value)
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
            throw new RobotSystemException($"{path} could not be written: {ex.Message}", ex)
            {
                Remediation = "Check the folder is writable and the file is not open in another program.",
            };
        }
    }
}
