namespace RobControl.Core.Sites;

/// <summary>
/// One site on this PC: its folders, and the settings read from them.
///
/// <para><see cref="Key"/> is the folder name and never changes; <see cref="SiteSettings.Name"/> is
/// what the person sees and can be renamed. That split is what lets a site be renamed without
/// moving its database out from under an open connection.</para>
/// </summary>
public sealed record Site(string Key, string Folder, SiteSettings Settings)
{
    public const string SettingsFileName = "site.json";
    public const string DatabaseFileName = "robcontrol.db";

    public string Name => Settings.Name;

    /// <summary>
    /// Where <c>site.json</c> and the site's system folders are: <c>Documents\RobControl\Systems\&lt;key&gt;</c>
    /// since 0.6.0. <see cref="Folder"/> - in the profile - keeps the database. The same folder when a
    /// catalog is built without a separate config root (the tests, and anything before 0.6.0).
    /// </summary>
    public string ConfigFolder { get; init; } = Folder;

    public string SettingsPath => Path.Combine(ConfigFolder, SettingsFileName);

    /// <summary>The robot list, last probes, event log and trends of this site - and of no other.</summary>
    public string DatabasePath => Path.Combine(Folder, DatabaseFileName);

    public override string ToString() => Name;
}
