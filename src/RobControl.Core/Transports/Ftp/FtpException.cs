namespace RobControl.Core.Transports.Ftp;

/// <summary>
/// The controller answered, and the answer was no. Carries the reply so a message can quote it
/// exactly - the wording a controller uses is often the only clue to which setting is wrong.
/// </summary>
public sealed class FtpException : RobControlException
{
    public FtpException(string message, FtpReply? reply = null, Exception? inner = null)
        : base(message, inner)
    {
        Reply = reply;
    }

    public FtpReply? Reply { get; }
}
