using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace RobControl.Core.Transports.Ftp;

/// <summary>
/// A deliberately small FTP client: log in, look around, download. Nothing else.
///
/// <para><b>Read-only by construction.</b> <see cref="AllowedVerbs"/> is the complete list of
/// commands this class can put on the wire, and <see cref="SendAsync"/> refuses anything else
/// before a byte is written. There is no STOR, DELE, RNFR, MKD or SITE here, and adding one is a
/// change to the Safety section of CLAUDE.md first. A backup tool that can delete files on a
/// production robot because of a bug in a filename loop is not a backup tool.</para>
///
/// <para><b>Why not FtpWebRequest or a package.</b> <c>FtpWebRequest</c> is obsolete and hides the
/// reply text, which is the one thing a "why did the backup fail" message needs. A package would
/// bring a large surface we would then have to promise never calls. The subset a FANUC controller
/// needs is a few hundred lines.</para>
///
/// <para>Passive mode only for now. If an older controller turns out to need active mode, that is
/// recorded in CLAUDE.md's Controller gotchas with the version it was seen on, and built then.</para>
/// </summary>
public sealed class FtpClient : IAsyncDisposable
{
    /// <summary>Every verb this client is able to send. See the class remarks.</summary>
    public static IReadOnlySet<string> AllowedVerbs { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        "USER", "PASS", "SYST", "TYPE", "PWD", "CWD", "PASV", "NLST", "LIST", "RETR", "NOOP", "QUIT",
    };

    // Latin-1 maps every byte to one char and back, so nothing a controller sends can fail to
    // decode, and a name read from NLST round-trips byte for byte into RETR.
    private static readonly Encoding Wire = Encoding.Latin1;

    private readonly TcpClient _control;
    private readonly NetworkStream _stream;
    private readonly IPAddress _host;
    private readonly FtpClientOptions _options;
    private readonly FtpTranscript _transcript;
    private readonly FtpReplyReader _replies = new();
    private readonly byte[] _readBuffer = new byte[4096];
    private readonly StringBuilder _pendingLine = new();
    private readonly Queue<string> _pendingLines = new();
    private bool _binary;
    private bool _disposed;

    private FtpClient(TcpClient control, IPAddress host, FtpClientOptions options, FtpTranscript transcript)
    {
        _control = control;
        _stream = control.GetStream();
        _host = host;
        _options = options;
        _transcript = transcript;
    }

    /// <summary>What the server said when the connection opened - often names the controller.</summary>
    public FtpReply Banner { get; private set; } = new(0, []);

    public FtpTranscript Transcript => _transcript;

    /// <summary>Opens the control connection and reads the greeting. Does not log in.</summary>
    public static async Task<FtpClient> ConnectAsync(
        IPAddress host,
        int port = 21,
        FtpClientOptions? options = null,
        FtpTranscript? transcript = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(host);
        options ??= FtpClientOptions.Default;
        transcript ??= new FtpTranscript();

        var tcp = new TcpClient(host.AddressFamily) { NoDelay = true };
        try
        {
            transcript.Note(string.Create(CultureInfo.InvariantCulture, $"connect {host}:{port}"));
            await ConnectWithTimeoutAsync(tcp, new IPEndPoint(host, port), options.ConnectTimeout, cancellationToken)
                .ConfigureAwait(false);

            var client = new FtpClient(tcp, host, options, transcript);
            client.Banner = await client.ReadReplyAsync(cancellationToken).ConfigureAwait(false);
            if (!client.Banner.IsCompletion)
            {
                await client.DisposeAsync().ConfigureAwait(false);
                throw new FtpException($"{host} answered on port {port} but is not ready: {client.Banner}.", client.Banner);
            }

            return client;
        }
        catch
        {
            tcp.Dispose();
            throw;
        }
    }

    /// <summary>
    /// USER, then PASS if asked for one. A 530 becomes a message that says where the setting is.
    /// </summary>
    public async Task LoginAsync(FtpCredentials credentials, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(credentials);

        FtpReply user = await SendAsync("USER", credentials.User, cancellationToken).ConfigureAwait(false);
        FtpReply final = user;

        if (user.IsIntermediate)
        {
            final = await SendAsync("PASS", credentials.Password, cancellationToken).ConfigureAwait(false);
        }

        if (!final.IsCompletion)
        {
            throw new FtpException($"The controller refused the FTP login as '{credentials.User}' ({final}).", final)
            {
                Remediation = "The controller may have FTP users configured. Check Setup > Host Comm > "
                    + "FTP on the pendant, and set the robot's FTP user and password in RobControl to match.",
            };
        }
    }

    /// <summary>SYST - the server's own description of itself. Recorded; never relied on.</summary>
    public async Task<FtpReply> SystemAsync(CancellationToken cancellationToken = default) =>
        await SendAsync("SYST", null, cancellationToken).ConfigureAwait(false);

    public async Task<string> PrintWorkingDirectoryAsync(CancellationToken cancellationToken = default)
    {
        FtpReply reply = await ExpectAsync("PWD", null, "read the current directory", cancellationToken)
            .ConfigureAwait(false);

        // 257 "md:" is current directory - the quoted part is the answer.
        string text = reply.Text;
        int open = text.IndexOf('"', StringComparison.Ordinal);
        int close = open >= 0 ? text.IndexOf('"', open + 1) : -1;
        return open >= 0 && close > open ? text[(open + 1)..close] : text;
    }

    /// <summary>CWD - on a FANUC controller this is how a device is chosen: <c>md:</c>, <c>fr:</c>, <c>mc:</c>.</summary>
    public async Task ChangeDirectoryAsync(string path, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        FtpReply reply = await SendAsync("CWD", path, cancellationToken).ConfigureAwait(false);
        if (!reply.IsCompletion)
        {
            throw new FtpException($"The controller would not open '{path}' ({reply}).", reply)
            {
                Remediation = "That device may not exist on this controller - a memory card or USB stick "
                    + "that is not inserted, for example.",
            };
        }
    }

    /// <summary>NLST - the file names in the current directory, or in <paramref name="path"/>.</summary>
    public async Task<IReadOnlyList<string>> ListNamesAsync(string? path = null, CancellationToken cancellationToken = default)
    {
        byte[] raw = await ReadDataAsync("NLST", path, cancellationToken).ConfigureAwait(false);
        return SplitLines(Wire.GetString(raw));
    }

    /// <summary>LIST - the long listing, as text. Format varies by server, so it is kept verbatim.</summary>
    public async Task<string> ListAsync(string? path = null, CancellationToken cancellationToken = default)
    {
        byte[] raw = await ReadDataAsync("LIST", path, cancellationToken).ConfigureAwait(false);
        return Wire.GetString(raw);
    }

    /// <summary>
    /// RETR into <paramref name="destination"/>, in binary. Returns the number of bytes copied.
    /// Binary always - an ASCII-mode transfer rewrites line endings, and then the hash of the same
    /// file differs depending on which way it was fetched.
    /// </summary>
    public async Task<long> RetrieveAsync(string name, Stream destination, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(destination);

        return await TransferAsync("RETR", name, destination, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>NOOP - keeps a long session alive, and proves the control connection still works.</summary>
    public Task<FtpReply> NoOpAsync(CancellationToken cancellationToken = default) =>
        SendAsync("NOOP", null, cancellationToken);

    /// <summary>QUIT, politely. Never throws: the session is ending either way.</summary>
    public async Task QuitAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(3));
            await SendAsync("QUIT", null, cts.Token).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or SocketException or OperationCanceledException
            or FtpProtocolException or ObjectDisposedException)
        {
            _transcript.Note("QUIT not acknowledged: " + ex.Message);
        }
    }

    /// <summary>
    /// Sends one command and reads its reply. Refuses any verb not in <see cref="AllowedVerbs"/>,
    /// and any argument holding a line break - which is how a hostile file name would smuggle a
    /// second command onto the control connection.
    /// </summary>
    public async Task<FtpReply> SendAsync(string verb, string? argument, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(verb);

        if (!AllowedVerbs.Contains(verb))
        {
            throw new InvalidOperationException(
                $"RobControl's FTP client does not send {verb}. It is read-only by design - see the Safety section of CLAUDE.md.");
        }

        if (argument is not null && argument.AsSpan().IndexOfAny('\r', '\n') >= 0)
        {
            throw new ArgumentException("An FTP argument cannot contain a line break.", nameof(argument));
        }

        string line = argument is null ? verb : $"{verb} {argument}";
        _transcript.Sent(verb == "PASS" ? (argument!.Length == 0 ? "PASS" : "PASS ****") : line);

        byte[] bytes = Wire.GetBytes(line + "\r\n");
        using (var cts = Timeout(_options.ReplyTimeout, cancellationToken))
        {
            await _stream.WriteAsync(bytes, cts.Token).ConfigureAwait(false);
        }

        return await ReadReplyAsync(cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        await _stream.DisposeAsync().ConfigureAwait(false);
        _control.Dispose();
    }

    private async Task<FtpReply> ExpectAsync(string verb, string? argument, string doing, CancellationToken cancellationToken)
    {
        FtpReply reply = await SendAsync(verb, argument, cancellationToken).ConfigureAwait(false);
        if (!reply.IsCompletion)
        {
            throw new FtpException($"The controller would not {doing} ({reply}).", reply);
        }

        return reply;
    }

    private async Task<byte[]> ReadDataAsync(string verb, string? argument, CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        await TransferAsync(verb, argument, buffer, cancellationToken).ConfigureAwait(false);
        return buffer.ToArray();
    }

    /// <summary>
    /// The passive-mode dance: TYPE I once, PASV, connect the data socket, send the command, read
    /// the preliminary reply, copy until the server closes the data socket, read the final reply.
    /// </summary>
    private async Task<long> TransferAsync(string verb, string? argument, Stream destination, CancellationToken cancellationToken)
    {
        if (!_binary)
        {
            await ExpectAsync("TYPE", "I", "switch to binary transfers", cancellationToken).ConfigureAwait(false);
            _binary = true;
        }

        FtpReply pasv = await SendAsync("PASV", null, cancellationToken).ConfigureAwait(false);
        IPEndPoint dataEndpoint = PassiveEndpoint.Parse(pasv, _host);

        using var data = new TcpClient(dataEndpoint.AddressFamily);
        await ConnectWithTimeoutAsync(data, dataEndpoint, _options.ConnectTimeout, cancellationToken).ConfigureAwait(false);

        FtpReply started = await SendAsync(verb, argument, cancellationToken).ConfigureAwait(false);
        if (!started.IsPreliminary && !started.IsCompletion)
        {
            string what = argument is null ? verb : $"{verb} {argument}";
            throw new FtpException($"The controller refused '{what}' ({started}).", started);
        }

        long copied = 0;
        await using (NetworkStream dataStream = data.GetStream())
        {
            byte[] chunk = new byte[64 * 1024];
            while (true)
            {
                int read;
                using (var idle = Timeout(_options.DataIdleTimeout, cancellationToken))
                {
                    try
                    {
                        read = await dataStream.ReadAsync(chunk, idle.Token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                    {
                        throw new FtpTimeoutException(
                            $"'{argument ?? verb}' stopped arriving: nothing for {_options.DataIdleTimeout.TotalSeconds:0} s "
                                + $"after {copied} bytes.");
                    }
                }

                if (read == 0)
                {
                    break;
                }

                await destination.WriteAsync(chunk.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                copied += read;
            }
        }

        _transcript.Note(string.Create(CultureInfo.InvariantCulture, $"data: {copied} bytes"));

        // A server that answered the command itself with 226 (everything already sent) has nothing
        // more to say; otherwise the transfer is only complete once it says so.
        if (started.IsPreliminary)
        {
            FtpReply done = await ReadReplyAsync(cancellationToken).ConfigureAwait(false);
            if (!done.IsCompletion)
            {
                throw new FtpException($"The transfer of '{argument ?? verb}' did not complete ({done}).", done);
            }
        }

        return copied;
    }

    private async Task<FtpReply> ReadReplyAsync(CancellationToken cancellationToken)
    {
        using var cts = Timeout(_options.ReplyTimeout, cancellationToken);
        try
        {
            while (true)
            {
                string line = await ReadLineAsync(cts.Token).ConfigureAwait(false);
                _transcript.Received(line);
                FtpReply? reply = _replies.Push(line);
                if (reply is not null)
                {
                    return reply;
                }
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            _transcript.Note("timed out waiting for a reply");
            throw new FtpTimeoutException(
                $"The controller did not answer within {_options.ReplyTimeout.TotalSeconds:0} s.");
        }
    }

    private async Task<string> ReadLineAsync(CancellationToken cancellationToken)
    {
        while (_pendingLines.Count == 0)
        {
            int read = await _stream.ReadAsync(_readBuffer, cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                throw new FtpProtocolException("The controller closed the FTP connection.");
            }

            foreach (char c in Wire.GetString(_readBuffer, 0, read))
            {
                if (c == '\n')
                {
                    _pendingLines.Enqueue(_pendingLine.ToString().TrimEnd('\r'));
                    _pendingLine.Clear();
                }
                else
                {
                    _pendingLine.Append(c);
                }
            }
        }

        return _pendingLines.Dequeue();
    }

    private static List<string> SplitLines(string text) =>
        [.. text.Split('\n').Select(l => l.TrimEnd('\r').Trim()).Where(l => l.Length > 0)];

    private static CancellationTokenSource Timeout(TimeSpan after, CancellationToken cancellationToken)
    {
        var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(after);
        return cts;
    }

    private static async Task ConnectWithTimeoutAsync(TcpClient tcp, IPEndPoint endpoint, TimeSpan timeout, CancellationToken cancellationToken)
    {
        using var cts = Timeout(timeout, cancellationToken);
        try
        {
            await tcp.ConnectAsync(endpoint, cts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new FtpConnectException(
                $"Nothing answered at {endpoint} within {timeout.TotalSeconds:0} s.",
                "Check the robot is powered and on this network. Diagnose network opens NetControl on it.");
        }
        catch (SocketException ex) when (ex.SocketErrorCode == SocketError.ConnectionRefused)
        {
            throw new FtpConnectException(
                $"{endpoint} refused the connection - the address answers, but nothing is listening on port {endpoint.Port}.",
                "The FTP server may be disabled on this controller (Setup > Host Comm > Servers), or the address "
                    + "belongs to something that is not a robot.");
        }
    }
}
