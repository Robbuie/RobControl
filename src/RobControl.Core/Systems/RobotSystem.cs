namespace RobControl.Core.Systems;

/// <summary>
/// One system on this PC: the site it belongs to, its folder, and its settings.
///
/// <para>As with sites, <see cref="Key"/> is the folder name and never changes; the name can. The
/// folder is under the site's config folder, so the whole tree reads in Explorer as
/// <c>Systems\Plant 3\Line 4 framer\system.json</c>.</para>
/// </summary>
public sealed record RobotSystem(string SiteKey, string Key, string Folder, SystemSettings Settings)
{
    public const string SettingsFileName = "system.json";

    /// <summary>The key of the pseudo-system that holds a site's robots no system lists.</summary>
    public const string UnassignedKey = "*unassigned";

    public string Name => Settings.Name;

    public string SettingsPath => Path.Combine(Folder, SettingsFileName);

    /// <summary>
    /// True for the "Not in a system" entry: robots in the site's database that no system.json lists -
    /// usually because somebody edited a file by hand. It has no folder and no settings of its own.
    /// </summary>
    public bool IsUnassigned => Key == UnassignedKey;

    public static RobotSystem Unassigned(string siteKey) =>
        new(siteKey, UnassignedKey, string.Empty, new SystemSettings { Name = "Not in a system" });

    public override string ToString() => Name;
}
