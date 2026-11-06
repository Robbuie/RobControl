using System.Net;
using System.Text.Json.Serialization;
using RobControl.Core.Robots;

namespace RobControl.Core.Systems;

/// <summary>
/// One robot's line in <c>system.json</c>: which controller it is, and - only if it differs from the
/// system - how it is backed up.
///
/// <para><b>The address is the key.</b> The robot itself (name, login, ports) lives in the site's
/// database, where the probes, events and trends refer to it; this entry says it belongs to this
/// system. Address and FTP port together are what the robot dialog already treats as "the same
/// controller", so they are what matches an entry to a robot. The name is written for whoever opens
/// the file, and refreshed whenever RobControl saves it.</para>
/// </summary>
public sealed record SystemRobot
{
    [JsonPropertyName("address")]
    public string Address { get; init; } = string.Empty;

    [JsonPropertyName("ftpPort")]
    public int FtpPort { get; init; } = 21;

    /// <summary>For the reader of the file. Not used to match.</summary>
    [JsonPropertyName("name")]
    public string? Name { get; init; }

    /// <summary>What differs from the system's backup settings. Null when nothing does.</summary>
    [JsonPropertyName("backup")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public BackupProfile? Backup { get; init; }

    public static SystemRobot From(Robot robot, BackupProfile? backup = null)
    {
        ArgumentNullException.ThrowIfNull(robot);
        return new SystemRobot
        {
            Address = robot.Address.ToString(),
            FtpPort = robot.FtpPort,
            Name = robot.Name,
            Backup = backup is { IsEmpty: false } ? backup : null,
        };
    }

    /// <summary>Whether this entry is <paramref name="robot"/>: same address, same FTP port.</summary>
    public bool Matches(Robot robot)
    {
        ArgumentNullException.ThrowIfNull(robot);
        return IPAddress.TryParse(Address.Trim(), out IPAddress? ip) && ip.Equals(robot.Address) && FtpPort == robot.FtpPort;
    }
}
