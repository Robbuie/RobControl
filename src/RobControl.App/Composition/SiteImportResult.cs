using RobControl.Core.Sites;

namespace RobControl.App.Composition;

/// <summary>The site an import made, how many robots went in, and a sentence for each that did not.</summary>
public sealed record SiteImportResult(Site Site, int Added, IReadOnlyList<string> Skipped);
