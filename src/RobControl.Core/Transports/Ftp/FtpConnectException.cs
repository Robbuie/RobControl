namespace RobControl.Core.Transports.Ftp;

/// <summary>Could not open an FTP session with the controller at all.</summary>
public sealed class FtpConnectException : RobControlException
{
    public FtpConnectException(string message, string remediation, FtpConnectFailure failure = FtpConnectFailure.NoAnswer)
        : base(message)
    {
        Remediation = remediation;
        Failure = failure;
    }

    public FtpConnectFailure Failure { get; }

    /// <summary>
    /// What the robot's web port did when FTP did not answer, once <see cref="FtpReachability"/> has
    /// looked. Null when nobody looked.
    /// </summary>
    public Net.PortState? WebPort { get; init; }
}
