namespace RobControl.Core.Transports.Ftp;

/// <summary>Could not open a TCP connection to the controller at all.</summary>
public sealed class FtpConnectException : RobControlException
{
    public FtpConnectException(string message, string remediation) : base(message)
    {
        Remediation = remediation;
    }
}
