using System.Globalization;
using System.Net;
using RobControl.Core;
using RobControl.Core.Net;
using RobControl.Core.Robots;
using RobControl.Core.Transports.Ftp;

namespace RobControl.App.ViewModels;

/// <summary>
/// What the robot dialog edits: text, exactly as typed, so a half-typed address is not lost. It
/// becomes a <see cref="Robot"/> only through <see cref="TryBuild"/>, which says what is wrong.
/// </summary>
public sealed record RobotDraft
{
    public long Id { get; init; }

    public string Name { get; init; } = string.Empty;

    public string Address { get; init; } = string.Empty;

    public string Line { get; init; } = string.Empty;

    public string Notes { get; init; } = string.Empty;

    public string FtpUser { get; init; } = FtpCredentials.Default.User;

    public string FtpPassword { get; init; } = string.Empty;

    public string FtpPort { get; init; } = "21";

    public string HttpPort { get; init; } = "80";

    public static RobotDraft From(Robot robot)
    {
        ArgumentNullException.ThrowIfNull(robot);
        return new RobotDraft
        {
            Id = robot.Id,
            Name = robot.Name,
            Address = robot.Address.ToString(),
            Line = robot.Line ?? string.Empty,
            Notes = robot.Notes ?? string.Empty,
            FtpUser = robot.Ftp.User,
            FtpPassword = robot.Ftp.Password,
            FtpPort = robot.FtpPort.ToString(CultureInfo.InvariantCulture),
            HttpPort = robot.HttpPort.ToString(CultureInfo.InvariantCulture),
        };
    }

    public bool TryBuild(out Robot? robot, out string? problem)
    {
        robot = null;
        problem = null;

        if (string.IsNullOrWhiteSpace(Name))
        {
            problem = "Give the robot a name - it is also its folder in the archive.";
            return false;
        }

        if (!IPAddress.TryParse(Address.Trim(), out IPAddress? address) || address.GetAddressBytes().Length != 4
            || Address.Trim().Count(c => c == '.') != 3)
        {
            problem = $"'{Address}' is not an IPv4 address. Type all four numbers, e.g. 10.20.1.54.";
            return false;
        }

        if (!UnicastTarget.TryCheck(address, null, out string? notOneHost))
        {
            problem = notOneHost;
            return false;
        }

        if (!TryPort(FtpPort, out int ftpPort) || !TryPort(HttpPort, out int httpPort))
        {
            problem = "Ports are whole numbers from 1 to 65535.";
            return false;
        }

        try
        {
            robot = new Robot(Name, address)
            {
                Id = Id,
                Line = Blank(Line),
                Notes = Blank(Notes),
                Ftp = new FtpCredentials(string.IsNullOrWhiteSpace(FtpUser) ? FtpCredentials.Default.User : FtpUser.Trim(), FtpPassword),
                FtpPort = ftpPort,
                HttpPort = httpPort,
            };
            return true;
        }
        catch (RobControlException ex)
        {
            problem = ex.Message;
            return false;
        }
    }

    private static bool TryPort(string text, out int port) =>
        int.TryParse(text.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out port) && port is >= 1 and <= 65535;

    private static string? Blank(string text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();
}
