// UseWPF drops System.IO from the implicit usings; the resource is read through a StreamReader.
using System.IO;
using RobControl.App.Diagnostics;

namespace RobControl.App.Composition;

/// <summary>
/// The README and the changelog, compiled into the executable (see the EmbeddedResource items in
/// the project file).
///
/// <para><b>Embedded rather than installed beside the exe</b> because the product is very often a
/// single portable exe copied onto a plant laptop. Help that only works when an installer put a
/// second file next to it is help that is missing exactly where it is needed - and an embedded copy
/// is always the one that matches the build that is running.</para>
/// </summary>
public static class HelpDocuments
{
    /// <summary>Where a relative link in either document points: the repository on GitHub.</summary>
    public static string RepositoryBase { get; } =
        $"https://github.com/{BuildInfo.RepositoryOwner}/{BuildInfo.RepositoryName}/blob/main/";

    public static string ReadmeOnline { get; } = RepositoryBase + "README.md";

    public static string ChangelogOnline { get; } = RepositoryBase + "CHANGELOG.md";

    public static string Readme => Read("RobControl.README.md");

    public static string Changelog => Read("RobControl.CHANGELOG.md");

    /// <summary>Never throws: a build somehow missing the resource says so in the window instead.</summary>
    private static string Read(string name)
    {
        using Stream? stream = typeof(HelpDocuments).Assembly.GetManifestResourceStream(name);
        if (stream is null)
        {
            return $"# Not in this build\n\n{name} was not compiled into this copy of RobControl. "
                + $"The current version is at [{RepositoryBase}]({RepositoryBase}).";
        }

        using var reader = new StreamReader(stream, System.Text.Encoding.UTF8);
        return reader.ReadToEnd();
    }
}
