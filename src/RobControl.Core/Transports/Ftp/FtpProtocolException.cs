namespace RobControl.Core.Transports.Ftp;

/// <summary>
/// The server sent something that is not FTP. Usually the wrong port, or something in the way.
/// </summary>
public sealed class FtpProtocolException : RobControlException
{
    public FtpProtocolException(string message) : base(message) { }
}
