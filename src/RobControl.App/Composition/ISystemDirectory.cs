using RobControl.Core.Sites;
using RobControl.Core.Systems;

namespace RobControl.App.Composition;

/// <summary>
/// What the system picker needs: every site and its systems, and a way to make new ones. Nothing in
/// here opens a database - the picker runs before anything is open.
/// </summary>
public interface ISystemDirectory
{
    /// <summary>Every site on this PC with its systems, by name. A site with no systems yet lists none.</summary>
    IReadOnlyList<SitePick> Directory();

    /// <summary>Where the config folders are: <c>Documents\RobControl\Systems</c>.</summary>
    string SystemsFolder { get; }

    /// <summary>Makes an empty site without opening it. Throws <see cref="SiteException"/> with the reason.</summary>
    Site Create(SiteSettings settings);

    /// <summary>Makes an empty system in a site without opening it. Throws <see cref="RobotSystemException"/> with the reason.</summary>
    RobotSystem CreateSystem(string siteKey, SystemSettings settings);
}
