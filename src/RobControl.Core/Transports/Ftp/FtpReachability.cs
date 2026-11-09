using System.Globalization;
using RobControl.Core.Net;
using RobControl.Core.Robots;

namespace RobControl.Core.Transports.Ftp;

/// <summary>
/// Turns "FTP did not answer" into which of the likely causes it is, by looking at the robot's web
/// port once - a TCP connect and close, nothing sent on it.
///
/// <para>Seen on an R-30iB V8.30 (6 Nov 2026): ping and the web pages answered, FTP on 21 timed out.
/// "Nothing answered - check the robot is powered" was the wrong advice there. If the web port opens,
/// the robot is on the network and the problem is the FTP server or a filter in front of port 21,
/// and the message says so.</para>
/// </summary>
public static class FtpReachability
{
    /// <summary>How long the web port gets. Short: this is a second opinion, not a session.</summary>
    public static TimeSpan WebCheckTimeout { get; } = TimeSpan.FromSeconds(3);

    /// <summary>
    /// For a <see cref="FtpConnectFailure.NoAnswer"/>, checks the web port and returns a better
    /// exception. Anything else comes back unchanged, with nothing sent.
    /// </summary>
    public static async Task<FtpConnectException> ExplainAsync(
        Robot robot,
        FtpConnectException failure,
        FtpTranscript? transcript = null,
        Func<CancellationToken, Task<PortState>>? checkWeb = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(robot);
        ArgumentNullException.ThrowIfNull(failure);
        if (failure.Failure != FtpConnectFailure.NoAnswer || failure.WebPort is not null)
        {
            return failure;
        }

        checkWeb ??= ct => PortCheck.CheckAsync(robot.Address, robot.HttpPort, WebCheckTimeout, ct);
        PortState web = await checkWeb(cancellationToken).ConfigureAwait(false);
        transcript?.Note(string.Create(CultureInfo.InvariantCulture, $"web port {robot.HttpPort}: {web}"));
        return Explain(robot, failure, web);
    }

    /// <summary>The message and next step for FTP silence, given what the web port did.</summary>
    public static FtpConnectException Explain(Robot robot, FtpConnectException failure, PortState web)
    {
        ArgumentNullException.ThrowIfNull(robot);
        ArgumentNullException.ThrowIfNull(failure);
        string who = robot.Describe();
        int ftp = robot.FtpPort;
        int http = robot.HttpPort;

        (string message, string remediation) = web switch
        {
            PortState.Open => (
                string.Create(CultureInfo.InvariantCulture,
                    $"{who} is on the network - its web server (port {http}) answers - but FTP on port {ftp} sent nothing back, not even a refusal."),
                "Either the controller's FTP server is not running, or something between this PC and the robot "
                    + "(a firewall, a managed switch, a cell router) drops port " + ftp.ToString(CultureInfo.InvariantCulture) + ". "
                    + "On the pendant, look at Setup > Host Comm > [SHOW] > Servers: the FTP server tags should be STARTED. "
                    + "If they are, ask whoever runs the plant network whether port " + ftp.ToString(CultureInfo.InvariantCulture)
                    + " is open to this robot. RobControl will not change the controller's settings itself."),
            PortState.Refused => (
                string.Create(CultureInfo.InvariantCulture,
                    $"{who} answers (its web port {http} refused at once), but FTP on port {ftp} sent nothing back."),
                "Something answers at this address, but neither FTP nor the web server is listening, which is unusual "
                    + "for a FANUC controller. Check the address is the robot's, and its FTP and web servers under "
                    + "Setup > Host Comm on the pendant."),
            _ => (
                string.Create(CultureInfo.InvariantCulture,
                    $"Nothing at {who} answered - not FTP on port {ftp}, not the web server on port {http}."),
                "The robot is off, on another network, or unplugged, or a cable or switch on the way is down. "
                    + "Check it is powered and that this PC is on its network; Diagnose network opens NetControl on it."),
        };

        return new FtpConnectException(message, remediation, failure.Failure) { WebPort = web };
    }
}
