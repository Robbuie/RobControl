namespace RobControl.Core.Transports.Ftp;

/// <summary>
/// Which way the FTP connection failed. They have different causes on a FANUC controller, so they
/// get different messages: a refusal is a closed port on a live stack, silence is a server that is
/// off behind a stack that drops, or a filter on the way, and a connection with no greeting is a
/// server that is there but busy.
/// </summary>
public enum FtpConnectFailure
{
    /// <summary>Nothing came back within the connect timeout - not even a refusal.</summary>
    NoAnswer,

    /// <summary>The address answered with a reset: nothing is listening on the port.</summary>
    Refused,

    /// <summary>The TCP connection opened, but the FTP server never sent its 220 greeting.</summary>
    NoGreeting,
}
