namespace RobControl.Core.Transports.Ftp;

/// <summary>
/// A user name and password for a controller's FTP server.
///
/// <para>The password is kept out of <see cref="ToString"/> and out of every transcript - see
/// <see cref="FtpTranscript"/>. Transcripts get pasted into emails and committed as test fixtures.</para>
/// </summary>
public sealed record FtpCredentials(string User, string Password)
{
    /// <summary>
    /// What a controller with no FTP users configured accepts. FANUC's own manual describes
    /// anonymous access as the default; whether a given controller has been locked down is exactly
    /// what the capability probe finds out.
    /// </summary>
    public static FtpCredentials Default { get; } = new("anonymous", string.Empty);

    public override string ToString() => Password.Length == 0 ? User : $"{User} (password set)";
}
