namespace RobControl.Core.Transports.Ftp;

/// <summary>
/// Timeouts for one FTP session. Generous by default: a controller generating a large ASCII
/// listing on the fly (an <c>.LS</c> of a long program, or the <c>.VA</c> of every variable) can
/// sit for seconds before the first byte, and a timeout that fires there reads as a broken robot.
/// </summary>
public sealed record FtpClientOptions
{
    public TimeSpan ConnectTimeout { get; init; } = TimeSpan.FromSeconds(5);

    /// <summary>How long to wait for any one reply on the control connection.</summary>
    public TimeSpan ReplyTimeout { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// How long a data transfer may go without receiving a byte. Not a limit on the whole transfer:
    /// a big file over a slow link is fine as long as it keeps moving.
    /// </summary>
    public TimeSpan DataIdleTimeout { get; init; } = TimeSpan.FromSeconds(60);

    public static FtpClientOptions Default { get; } = new();
}
