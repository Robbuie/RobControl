using System.Net;
using System.Text.Json.Serialization;
using RobControl.Core.Robots;
using RobControl.Core.Transports.Ftp;

namespace RobControl.Core.Sites;

/// <summary>
/// One robot as it travels in a site file: what the person typed, nothing the controller said.
/// Probe results, backups and trends stay behind - the archive carries the backups on its own, and
/// a probe is re-run at the next contact anyway.
/// </summary>
public sealed record SiteRobot
{
    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    [JsonPropertyName("address")]
    public string Address { get; init; } = string.Empty;

    [JsonPropertyName("line")]
    public string? Line { get; init; }

    [JsonPropertyName("notes")]
    public string? Notes { get; init; }

    [JsonPropertyName("ftpUser")]
    public string FtpUser { get; init; } = FtpCredentials.Default.User;

    [JsonPropertyName("ftpPassword")]
    public string FtpPassword { get; init; } = string.Empty;

    [JsonPropertyName("ftpPort")]
    public int FtpPort { get; init; } = 21;

    [JsonPropertyName("httpPort")]
    public int HttpPort { get; init; } = 80;

    public static SiteRobot From(Robot robot)
    {
        ArgumentNullException.ThrowIfNull(robot);
        return new SiteRobot
        {
            Name = robot.Name,
            Address = robot.Address.ToString(),
            Line = robot.Line,
            Notes = robot.Notes,
            FtpUser = robot.Ftp.User,
            FtpPassword = robot.Ftp.Password,
            FtpPort = robot.FtpPort,
            HttpPort = robot.HttpPort,
        };
    }

    /// <summary>
    /// The robot this entry describes, or why it cannot be one. The same unicast check as a robot
    /// typed into the dialog: a site file is just another way of typing the list, and it must not
    /// be a way to aim RobControl at a broadcast address.
    /// </summary>
    public bool TryBuild(out Robot? robot, out string? problem)
    {
        robot = null;
        problem = null;

        string label = string.IsNullOrWhiteSpace(Name) ? $"'{Address}'" : $"'{Name}'";
        if (string.IsNullOrWhiteSpace(Name))
        {
            problem = $"{label}: no name.";
            return false;
        }

        if (!IPAddress.TryParse(Address?.Trim(), out IPAddress? address) || address.GetAddressBytes().Length != 4
            || Address!.Trim().Count(c => c == '.') != 3)
        {
            problem = $"{label}: '{Address}' is not an IPv4 address.";
            return false;
        }

        if (FtpPort is < 1 or > 65535 || HttpPort is < 1 or > 65535)
        {
            problem = $"{label}: a port is outside 1-65535.";
            return false;
        }

        try
        {
            robot = new Robot(Name, address)
            {
                Line = string.IsNullOrWhiteSpace(Line) ? null : Line.Trim(),
                Notes = string.IsNullOrWhiteSpace(Notes) ? null : Notes.Trim(),
                Ftp = new FtpCredentials(string.IsNullOrWhiteSpace(FtpUser) ? FtpCredentials.Default.User : FtpUser.Trim(), FtpPassword ?? string.Empty),
                FtpPort = FtpPort,
                HttpPort = HttpPort,
            };
            return true;
        }
        catch (RobControlException ex)
        {
            problem = $"{label}: {ex.Message}";
            return false;
        }
    }
}
