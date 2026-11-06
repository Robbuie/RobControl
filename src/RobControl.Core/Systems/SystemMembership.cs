using System.Net;
using RobControl.Core.Net;
using RobControl.Core.Robots;
using RobControl.Core.Transports.Ftp;

namespace RobControl.Core.Systems;

/// <summary>
/// Which of a site's robots a system shows, and which robots a <c>system.json</c> names that the
/// site's database does not have yet.
/// </summary>
public static class SystemMembership
{
    /// <summary>
    /// The robots <paramref name="system"/> lists, in the file's order - or, for the "Not in a system"
    /// entry, every robot no system lists.
    /// </summary>
    public static IReadOnlyList<Robot> Members(RobotSystem system, IReadOnlyList<Robot> all, IReadOnlyList<RobotSystem> systems)
    {
        ArgumentNullException.ThrowIfNull(system);
        ArgumentNullException.ThrowIfNull(all);
        ArgumentNullException.ThrowIfNull(systems);

        if (system.IsUnassigned)
        {
            return [.. all.Where(r => !systems.Any(s => !s.IsUnassigned && s.Settings.Contains(r)))];
        }

        var members = new List<Robot>();
        foreach (SystemRobot entry in system.Settings.Robots)
        {
            Robot? robot = all.FirstOrDefault(entry.Matches);
            if (robot is not null && !members.Contains(robot))
            {
                members.Add(robot);
            }
        }

        return members;
    }

    /// <summary>
    /// Robots that <paramref name="system"/>'s file lists by address and the database does not have:
    /// typed into <c>system.json</c> by hand, or a system folder copied from another laptop. Each comes
    /// back as a robot ready to save - its name from the file (the address when there is none), the
    /// site's default FTP login - or as a sentence saying why it cannot be one.
    ///
    /// <para>The same rules as the robot dialog: a unicast address, a name nobody else has, one row per
    /// controller. A file is just another way of typing the list.</para>
    /// </summary>
    public static IReadOnlyList<Robot> Adoptable(
        RobotSystem system, IReadOnlyList<Robot> all, FtpCredentials login, out IReadOnlyList<string> problems)
    {
        ArgumentNullException.ThrowIfNull(system);
        ArgumentNullException.ThrowIfNull(all);
        ArgumentNullException.ThrowIfNull(login);

        var found = new List<Robot>();
        var why = new List<string>();
        problems = why;
        if (system.IsUnassigned)
        {
            return found;
        }

        foreach (SystemRobot entry in system.Settings.Robots)
        {
            if (all.Any(entry.Matches) || found.Any(entry.Matches))
            {
                continue;
            }

            string label = string.IsNullOrWhiteSpace(entry.Name) ? entry.Address : $"'{entry.Name}' ({entry.Address})";
            if (!IPAddress.TryParse(entry.Address, out IPAddress? address))
            {
                why.Add($"{label}: '{entry.Address}' is not an IPv4 address.");
                continue;
            }

            if (!UnicastTarget.TryCheck(address, null, out string? unicast))
            {
                why.Add($"{label}: {unicast}");
                continue;
            }

            string name = string.IsNullOrWhiteSpace(entry.Name) ? entry.Address : entry.Name.Trim();
            if (all.Concat(found).Any(r => string.Equals(r.Name, name, StringComparison.OrdinalIgnoreCase)))
            {
                why.Add($"{label}: another robot at this site is already called '{name}'.");
                continue;
            }

            if (all.Concat(found).Any(r => r.Address.Equals(address) && r.FtpPort == entry.FtpPort))
            {
                continue;
            }

            found.Add(new Robot(name, address) { FtpPort = entry.FtpPort, Ftp = login });
        }

        return found;
    }
}
