// UseWPF drops System.IO from the implicit usings.
using System.IO;
using System.Net;

namespace RobControl.App.Composition;

/// <summary>
/// Opens NetControl on an address - CLAUDE.md, "Relationship to NetControl".
///
/// <para>Finds NetControl where its installer puts it, per-user and without admin. A portable copy
/// somewhere else is not hunted for: guessing at executables on somebody's disk and running them is
/// the wrong habit for a tool that sits on plant laptops.</para>
///
/// <para>Passes <c>--diagnose &lt;address&gt;</c>. A NetControl build that does not understand it yet
/// simply opens, which is still the right window.</para>
/// </summary>
public static class NetControlHandoff
{
    public const string ReleasesPage = "https://github.com/Robbuie/NetControl/releases/latest";

    public static string InstalledPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData, Environment.SpecialFolderOption.DoNotVerify),
        "Programs", "NetControl", "NetControl.exe");

    public static bool IsInstalled => File.Exists(InstalledPath);

    /// <summary>Starts NetControl. Returns null on success, or a sentence saying why not.</summary>
    public static string? Open(IPAddress address)
    {
        ArgumentNullException.ThrowIfNull(address);

        if (!IsInstalled)
        {
            return $"NetControl is not installed for this user (looked in {InstalledPath}). Get it from {ReleasesPage}";
        }

        try
        {
            var start = new System.Diagnostics.ProcessStartInfo(InstalledPath) { UseShellExecute = false };
            start.ArgumentList.Add("--diagnose");
            start.ArgumentList.Add(address.ToString());
            using System.Diagnostics.Process? process = System.Diagnostics.Process.Start(start);
            return null;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return $"NetControl could not be started: {ex.Message}";
        }
    }
}
