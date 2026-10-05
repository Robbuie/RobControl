using RobControl.RobotSim;

namespace RobControl.Tests;

/// <summary>Finds profile folders copied beside the test assembly, and copies them for a test to change.</summary>
internal static class Fixtures
{
    public const string Synthetic = "synthetic-r30ibplus-v940-spottool";

    public static string Folder(string name) => Path.Combine(AppContext.BaseDirectory, "Fixtures", name);

    public static string ReadText(string name, string relative) =>
        File.ReadAllText(Path.Combine(Folder(name), relative), System.Text.Encoding.Latin1);

    /// <summary>A private copy of a profile, so a test can edit files or flags without touching others.</summary>
    public static string CopyOf(string name, TempFolder temp)
    {
        string target = Path.Combine(temp.Path, "profile-" + name);
        CopyDirectory(Folder(name), target);
        return target;
    }

    public static SimProfile Profile(string folder, Func<SimProfile, SimProfile>? change = null)
    {
        SimProfile profile = SimProfile.Load(folder);
        return change is null ? profile : change(profile);
    }

    private static void CopyDirectory(string from, string to)
    {
        Directory.CreateDirectory(to);
        foreach (string file in Directory.GetFiles(from))
        {
            File.Copy(file, Path.Combine(to, Path.GetFileName(file)));
        }

        foreach (string dir in Directory.GetDirectories(from))
        {
            CopyDirectory(dir, Path.Combine(to, Path.GetFileName(dir)));
        }
    }
}
