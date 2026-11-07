using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace RobControl.Core.Transports.Http;

/// <summary>
/// One GET written by hand as HTTP/1.0 over a plain socket, read until the server closes it - the
/// oldest and simplest request a web server can be sent. <see cref="ControllerWebClient"/> uses it
/// only after <see cref="HttpClient"/> has had a connection dropped twice: a small embedded server
/// that resets a modern client can still answer this.
///
/// <para><b>GET only</b>, like everything else in this folder. Nothing but the request line and
/// four headers is written.</para>
/// </summary>
internal static class RawHttpGet
{
    /// <summary>Far bigger than any diagnostic page (IOSTATE.DG on a full R-30iB is ~130 KB).</summary>
    public const int MaxBytes = 16 * 1024 * 1024;

    public static async Task<HttpExchange> GetAsync(IPAddress address, int port, string path, TimeSpan timeout, CancellationToken cancellationToken)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(timeout);
        var watch = Stopwatch.StartNew();

        using var client = new TcpClient(address.AddressFamily) { NoDelay = true };
        await client.ConnectAsync(address, port, cts.Token).ConfigureAwait(false);
        NetworkStream stream = client.GetStream();

        string host = port == 80 ? address.ToString() : $"{address}:{port.ToString(CultureInfo.InvariantCulture)}";
        byte[] request = Encoding.ASCII.GetBytes(
            $"GET {path} HTTP/1.0\r\nHost: {host}\r\nAccept: */*\r\nUser-Agent: RobControl\r\nConnection: close\r\n\r\n");
        await stream.WriteAsync(request, cts.Token).ConfigureAwait(false);

        using var buffer = new MemoryStream();
        byte[] chunk = new byte[16 * 1024];
        try
        {
            int read;
            while ((read = await stream.ReadAsync(chunk, cts.Token).ConfigureAwait(false)) > 0)
            {
                buffer.Write(chunk, 0, read);
                if (buffer.Length > MaxBytes)
                {
                    throw new IOException($"The reply to {path} was larger than {MaxBytes / (1024 * 1024)} MB.");
                }
            }
        }
        catch (IOException) when (LooksComplete(buffer.ToArray()))
        {
            // Some embedded servers reset the socket instead of closing it once they have sent the
            // page. A reply that is whole by its own Content-Length, or that ends with </html>, is
            // kept; anything else is a truncated reply and the exception stands.
        }

        return Parse(path, buffer.ToArray(), watch.Elapsed);
    }

    /// <summary>Splits a whole HTTP/1.x reply into status, type, date and body.</summary>
    internal static HttpExchange Parse(string path, byte[] reply, TimeSpan elapsed)
    {
        (int headerEnd, int bodyStart) = HeaderEnd(reply);
        if (headerEnd < 0)
        {
            throw new IOException($"The reply to {path} ended before its headers did ({reply.Length} bytes).");
        }

        string[] head = Encoding.Latin1.GetString(reply, 0, headerEnd).Split('\n');
        string[] status = head[0].Trim().Split(' ', 3, StringSplitOptions.RemoveEmptyEntries);
        if (status.Length < 2 || !status[0].StartsWith("HTTP/", StringComparison.OrdinalIgnoreCase)
            || !int.TryParse(status[1], NumberStyles.None, CultureInfo.InvariantCulture, out int code))
        {
            throw new IOException($"The reply to {path} did not start with an HTTP status line: '{head[0].Trim()}'.");
        }

        string? contentType = null;
        DateTimeOffset? date = null;
        bool chunked = false;
        foreach (string line in head.Skip(1))
        {
            int colon = line.IndexOf(':', StringComparison.Ordinal);
            if (colon <= 0)
            {
                continue;
            }

            string name = line[..colon].Trim();
            string value = line[(colon + 1)..].Trim();
            if (name.Equals("Content-Type", StringComparison.OrdinalIgnoreCase))
            {
                contentType = value;
            }
            else if (name.Equals("Date", StringComparison.OrdinalIgnoreCase)
                && DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out DateTimeOffset parsed))
            {
                date = parsed;
            }
            else if (name.Equals("Transfer-Encoding", StringComparison.OrdinalIgnoreCase)
                && value.Contains("chunked", StringComparison.OrdinalIgnoreCase))
            {
                chunked = true;
            }
        }

        byte[] body = reply[bodyStart..];
        if (chunked)
        {
            body = Dechunk(body);
        }

        return new HttpExchange(path, code, contentType, body, elapsed)
        {
            ServerDate = date,
            ReceivedUtc = DateTimeOffset.UtcNow,
        };
    }

    private static bool LooksComplete(byte[] reply)
    {
        (int headerEnd, int bodyStart) = HeaderEnd(reply);
        if (headerEnd < 0)
        {
            return false;
        }

        string head = Encoding.Latin1.GetString(reply, 0, headerEnd);
        foreach (string line in head.Split('\n'))
        {
            int colon = line.IndexOf(':', StringComparison.Ordinal);
            if (colon > 0 && line[..colon].Trim().Equals("Content-Length", StringComparison.OrdinalIgnoreCase)
                && long.TryParse(line[(colon + 1)..].Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out long length))
            {
                return reply.Length - bodyStart >= length;
            }
        }

        string tail = Encoding.Latin1.GetString(reply, Math.Max(bodyStart, reply.Length - 64), Math.Min(64, reply.Length - bodyStart));
        return tail.TrimEnd().EndsWith("</html>", StringComparison.OrdinalIgnoreCase);
    }

    private static (int HeaderEnd, int BodyStart) HeaderEnd(byte[] reply)
    {
        for (int i = 0; i + 1 < reply.Length; i++)
        {
            if (reply[i] == '\n' && reply[i + 1] == '\n')
            {
                return (i, i + 2);
            }

            if (i + 3 < reply.Length && reply[i] == '\r' && reply[i + 1] == '\n' && reply[i + 2] == '\r' && reply[i + 3] == '\n')
            {
                return (i, i + 4);
            }
        }

        return (-1, -1);
    }

    private static byte[] Dechunk(byte[] body)
    {
        using var output = new MemoryStream();
        int at = 0;
        while (at < body.Length)
        {
            int lineEnd = Array.IndexOf(body, (byte)'\n', at);
            if (lineEnd < 0)
            {
                break;
            }

            string sizeText = Encoding.ASCII.GetString(body, at, lineEnd - at).Trim().Split(';')[0];
            if (!int.TryParse(sizeText, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int size) || size <= 0)
            {
                break;
            }

            at = lineEnd + 1;
            int take = Math.Min(size, body.Length - at);
            output.Write(body, at, take);
            at += take;
            while (at < body.Length && body[at] is (byte)'\r' or (byte)'\n')
            {
                at++;
            }
        }

        return output.ToArray();
    }
}
