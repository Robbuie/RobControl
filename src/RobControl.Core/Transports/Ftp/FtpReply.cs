namespace RobControl.Core.Transports.Ftp;

/// <summary>
/// One reply from the server: the three-digit code and every line of text that came with it.
/// </summary>
public sealed record FtpReply(int Code, IReadOnlyList<string> Lines)
{
    /// <summary>1yz - positive preliminary: the data connection is about to open.</summary>
    public bool IsPreliminary => Code is >= 100 and < 200;

    /// <summary>2yz - done.</summary>
    public bool IsCompletion => Code is >= 200 and < 300;

    /// <summary>3yz - more is needed, for example a password after a user name.</summary>
    public bool IsIntermediate => Code is >= 300 and < 400;

    /// <summary>4yz or 5yz.</summary>
    public bool IsFailure => Code >= 400;

    /// <summary>The text with the code stripped, lines joined - for messages.</summary>
    public string Text => string.Join(" ", Lines.Select(StripCode)).Trim();

    public override string ToString() => $"{Code} {Text}";

    private static string StripCode(string line) =>
        line.Length >= 4 && char.IsDigit(line[0]) && char.IsDigit(line[1]) && char.IsDigit(line[2])
            && (line[3] == ' ' || line[3] == '-')
            ? line[4..]
            : line;
}
