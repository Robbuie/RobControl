namespace RobControl.App.Composition;

/// <summary>
/// What to open: a site, and a system in it. A null system means the site's first one - made, as
/// "Main" holding every robot, if the site has none yet.
/// </summary>
public sealed record SystemChoice(string SiteKey, string? SystemKey);

/// <summary>One site in the picker, with its systems.</summary>
public sealed record SitePick(string Key, string Name, string? Notes, IReadOnlyList<SystemPick> Systems);

/// <summary>One system in the picker. <see cref="RobotCount"/> is what its file lists.</summary>
public sealed record SystemPick(string SiteKey, string Key, string Name, int RobotCount, string? Notes, string Folder);
