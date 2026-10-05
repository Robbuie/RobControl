using System.Globalization;
using System.Text;

namespace RobControl.Core.Transports.Ftp;

/// <summary>
/// Every line sent and received on the control connection, in order.
///
/// <para>This is the Phase 0 deliverable as much as a debugging aid: the probe tool saves it, and a
/// transcript from a real controller is what turns "FANUC's FTP server does X" from folklore into a
/// fixture. <b>The password is never recorded</b> - <see cref="FtpClient"/> writes <c>PASS ****</c>.</para>
/// </summary>
public sealed class FtpTranscript
{
    private readonly object _gate = new();
    private readonly StringBuilder _text = new();
    private readonly TimeProvider _time;
    private readonly DateTimeOffset _start;

    public FtpTranscript(TimeProvider? time = null)
    {
        _time = time ?? TimeProvider.System;
        _start = _time.GetUtcNow();
    }

    public void Sent(string line) => Add(">", line);

    public void Received(string line) => Add("<", line);

    public void Note(string line) => Add("#", line);

    public override string ToString()
    {
        lock (_gate)
        {
            return _text.ToString();
        }
    }

    private void Add(string direction, string line)
    {
        double ms = (_time.GetUtcNow() - _start).TotalMilliseconds;
        lock (_gate)
        {
            _text.Append(CultureInfo.InvariantCulture, $"{ms,8:0} {direction} {line}\n");
        }
    }
}
