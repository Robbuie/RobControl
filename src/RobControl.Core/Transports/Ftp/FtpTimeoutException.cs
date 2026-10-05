namespace RobControl.Core.Transports.Ftp;

/// <summary>The controller stopped answering part way through a session.</summary>
public sealed class FtpTimeoutException : RobControlException
{
    public FtpTimeoutException(string message) : base(message)
    {
        Remediation = "The controller may be busy generating a large listing, or the link dropped. Try again; "
            + "if one file always times out, note which and on what software version.";
    }
}
