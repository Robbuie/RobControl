namespace RobControl.Core.Sites;

/// <summary>
/// One site on this PC: its folder, and the settings read from it.
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

    public string SettingsPath => Path.Combine(Folder, SettingsFileName);

    /// <summary>The robot list, last probes, event log and trends of this site - and of no other.</summary>
    public string DatabasePath => Path.Combine(Folder, DatabaseFileName);

    public override string ToString() => Name;
}
