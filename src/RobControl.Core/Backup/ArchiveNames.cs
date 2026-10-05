using System.Globalization;
using System.Text;

namespace RobControl.Core.Backup;

/// <summary>
/// The rules for turning names that came off a controller, or out of a person's keyboard, into
/// paths on this PC.
///
/// <para><b>A name from the controller is untrusted input headed for the file system.</b> If
/// NLST ever returned <c>..\..\Windows\x</c>, a naive <c>Path.Combine</c> would write outside the
/// archive. So a listed name is either a plain file name that stays inside its folder, or it is
/// skipped and recorded as skipped - never "cleaned up" into something else, because a cleaned-up
/// name is a file that does not match what is on the robot.</para>
/// </summary>
public static class ArchiveNames
{
    public const string IncompleteSuffix = "_INCOMPLETE";
    public const string InProgressSuffix = ".partial";

    private static readonly char[] Forbidden = ['/', '\\', ':', '*', '?', '"', '<', '>', '|'];

    private static readonly HashSet<string> ReservedDeviceNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    };

    /// <summary>
    /// The listed name with a leading device prefix (<c>md:</c>) removed, if it had one. Some
    /// servers list <c>md:SUMMARY.DG</c> rather than <c>SUMMARY.DG</c>.
    /// </summary>
    public static string StripDevice(string listed, string device)
    {
        ArgumentNullException.ThrowIfNull(listed);
        ArgumentNullException.ThrowIfNull(device);
        string trimmed = listed.Trim();
        return trimmed.StartsWith(device, StringComparison.OrdinalIgnoreCase) ? trimmed[device.Length..] : trimmed;
    }

    /// <summary>Whether <paramref name="name"/> can be written as-is, and if not, why.</summary>
    public static bool IsSafeFileName(string name, out string? problem)
    {
        problem = null;

        if (string.IsNullOrWhiteSpace(name))
        {
            problem = "empty name";
        }
        else if (name.Length > 128)
        {
            problem = "name longer than 128 characters";
        }
        else if (name.IndexOfAny(Forbidden) >= 0)
        {
            problem = "name contains a path separator or a character Windows does not allow";
        }
        else if (name.Any(char.IsControl))
        {
            problem = "name contains a control character";
        }
        else if (name is "." or ".." || name.Contains("..", StringComparison.Ordinal))
        {
            problem = "name contains '..'";
        }
        else if (name.EndsWith('.') || name.EndsWith(' '))
        {
            problem = "name ends with a dot or a space, which Windows silently drops";
        }
        else if (ReservedDeviceNames.Contains(Path.GetFileNameWithoutExtension(name)))
        {
            problem = "name is a reserved Windows device name";
        }

        return problem is null;
    }

    /// <summary>
    /// A robot's folder name. The robot's name is the person's, so it is made safe rather than
    /// refused: anything Windows will not take becomes <c>_</c>.
    /// </summary>
    public static string RobotFolder(string robotName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(robotName);
        var builder = new StringBuilder(robotName.Length);
        foreach (char c in robotName.Trim())
        {
            builder.Append(c < 32 || Array.IndexOf(Forbidden, c) >= 0 ? '_' : c);
        }

        string folder = builder.ToString().TrimEnd('.', ' ');
        if (folder.Length == 0 || ReservedDeviceNames.Contains(folder) || folder.All(c => c == '.'))
        {
            folder = "_" + folder;
        }

        return folder;
    }

    /// <summary><c>MD:</c> becomes the folder <c>MD</c>.</summary>
    public static string DeviceFolder(string device)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(device);
        string folder = device.Trim().TrimEnd(':').ToUpperInvariant();
        return IsSafeFileName(folder, out _) ? folder : throw new ArgumentException($"'{device}' is not a device name.", nameof(device));
    }

    /// <summary>The device as written in a manifest: <c>MD:</c>.</summary>
    public static string DeviceLabel(string device) => DeviceFolder(device) + ":";

    /// <summary>
    /// The timestamp part of a backup folder: local time, because a person reads it, sortable, and
    /// legal on every file system.
    /// </summary>
    public static string Stamp(DateTimeOffset utc) =>
        utc.ToLocalTime().ToString("yyyy-MM-dd_HHmmss", CultureInfo.InvariantCulture);
}
