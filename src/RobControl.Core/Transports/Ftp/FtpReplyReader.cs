using System.Globalization;

namespace RobControl.Core.Transports.Ftp;

/// <summary>
/// Assembles <see cref="FtpReply"/> values out of lines, per RFC 959 section 4.2.
///
/// <para>A reply is either one line, <c>"230 User logged in"</c>, or several: the first line is the
/// code followed by a hyphen, and the reply ends at the first later line that starts with the same
/// code followed by a space. Lines in between can say anything, including lines that start with
/// some other number - which is why the rule is "same code and a space", not "any code".</para>
///
/// <para>Separate from the client and fed one line at a time so it can be tested against captured
/// transcripts without a socket.</para>
/// </summary>
internal sealed class FtpReplyReader
{
    private readonly List<string> _lines = [];
    private string? _multiLineCode;

    /// <summary>Adds a line. Returns the finished reply when this line completed one, else null.</summary>
    /// <exception cref="FtpProtocolException">The line cannot start a reply.</exception>
    public FtpReply? Push(string line)
    {
        ArgumentNullException.ThrowIfNull(line);

        if (_multiLineCode is null)
        {
            if (line.Length < 3 || !IsCode(line))
            {
                throw new FtpProtocolException(
                    $"The server sent '{Truncate(line)}' where a reply code was expected.");
            }

            _lines.Add(line);

            if (line.Length > 3 && line[3] == '-')
            {
                _multiLineCode = line[..3];
                return null;
            }

            return Finish(line[..3]);
        }

        _lines.Add(line);

        if (line.Length >= 4 && line.StartsWith(_multiLineCode, StringComparison.Ordinal) && line[3] == ' ')
        {
            return Finish(_multiLineCode);
        }

        // Some servers end a multi-line reply with the bare code and nothing after it.
        if (line.Length == 3 && line == _multiLineCode)
        {
            return Finish(_multiLineCode);
        }

        return null;
    }

    private FtpReply Finish(string code)
    {
        var reply = new FtpReply(int.Parse(code, NumberStyles.None, CultureInfo.InvariantCulture), [.. _lines]);
        _lines.Clear();
        _multiLineCode = null;
        return reply;
    }

    private static bool IsCode(string line) =>
        char.IsAsciiDigit(line[0]) && char.IsAsciiDigit(line[1]) && char.IsAsciiDigit(line[2])
            && (line.Length == 3 || line[3] is ' ' or '-');

    private static string Truncate(string line) => line.Length <= 60 ? line : line[..60] + "...";
}
