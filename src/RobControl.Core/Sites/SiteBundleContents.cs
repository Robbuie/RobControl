namespace RobControl.Core.Sites;

/// <summary>A bundle's description and its site file, read without unpacking anything else.</summary>
public sealed record SiteBundleContents(SiteBundleInfo Info, SiteFile Site);
