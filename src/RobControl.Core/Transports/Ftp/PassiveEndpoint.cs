using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;

namespace RobControl.Core.Transports.Ftp;

/// <summary>
/// Reads the address out of a <c>227 Entering Passive Mode (h1,h2,h3,h4,p1,p2)</c> reply.
/// </summary>
internal static partial class PassiveEndpoint
{
    /// <summary>
    /// The endpoint to connect the data channel to.
    ///
    /// <para><b>The address in the reply is ignored when it disagrees with the controller's own.</b>
    /// A controller with two ports, or behind NAT, can advertise an address the laptop cannot reach,
    /// and a data connection to the wrong host is a hang rather than an error. The port is what
    /// matters; the host is the one we are already talking to. This is what every mainstream FTP
    /// client does by default for the same reason.</para>
    /// </summary>
    public static IPEndPoint Parse(FtpReply reply, IPAddress controlHost)
    {
        ArgumentNullException.ThrowIfNull(reply);

        Match match = Numbers().Match(reply.Text);
        if (reply.Code != 227 || !match.Success)
        {
            throw new FtpException(
                $"The controller did not accept passive mode ({reply}).",
                reply)
            {
                Remediation = "Older controllers may only support active FTP. Note the controller "
                    + "generation and software version and report it - active mode is not built yet.",
            };
        }

        int Group(int i) => int.Parse(match.Groups[i].Value, NumberStyles.None, CultureInfo.InvariantCulture);

        for (int i = 1; i <= 6; i++)
        {
            if (Group(i) > 255)
            {
                throw new FtpProtocolException($"The passive mode reply '{reply.Text}' is not a valid address.");
            }
        }

        int port = (Group(5) << 8) | Group(6);
        return new IPEndPoint(controlHost, port);
    }

    [GeneratedRegex(@"(\d{1,3}),\s*(\d{1,3}),\s*(\d{1,3}),\s*(\d{1,3}),\s*(\d{1,3}),\s*(\d{1,3})")]
    private static partial Regex Numbers();
}
