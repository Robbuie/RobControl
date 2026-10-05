using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace RobControl.RobotSim;

/// <summary>
/// The fake controller: an FTP server and an HTTP server over one <see cref="SimProfile"/>.
///
/// <para><b>It records every command it is sent</b>, including the ones it refuses, so a test can
/// assert the thing that matters most about RobControl: that it never sent STOR, DELE, or anything
/// but GET. A command outside the read set is answered <c>502</c> and kept in
/// <see cref="RefusedCommands"/>.</para>
///
/// <para>Not a faithful FANUC FTP server - it is the subset RobControl uses, with the controller's
/// observable habits (device-style CWD, generated listings) where we know them, and a profile flag
/// where we do not (<see cref="SimProfile.NlstWithDevicePrefix"/>).</para>
/// </summary>
public sealed class SimController : IAsyncDisposable
{
    private static readonly Encoding Wire = Encoding.Latin1;
    private static readonly HashSet<string> ReadVerbs = new(StringComparer.OrdinalIgnoreCase)
    {
        "USER", "PASS", "SYST", "TYPE", "PWD", "CWD", "PASV", "NLST", "LIST", "RETR", "NOOP", "QUIT",
    };

    private readonly SimProfile _profile;
    private readonly IPAddress _address;
    private readonly CancellationTokenSource _stop = new();
    private TcpListener? _ftp;
    private TcpListener? _http;
    private Task? _ftpLoop;
    private Task? _httpLoop;

    public SimController(SimProfile profile, IPAddress? address = null)
    {
        _profile = profile ?? throw new ArgumentNullException(nameof(profile));
        _address = address ?? IPAddress.Loopback;
    }

    public SimFaults Faults { get; } = new();

    /// <summary>
    /// Files on md: whose content is supplied live rather than read from the profile folder - what
    /// <see cref="SimAnimator"/> and the trending tests use to make values change between polls.
    /// Keyed by upper-case file name. Served to both FTP RETR and HTTP GET.
    /// </summary>
    public ConcurrentDictionary<string, byte[]> MdOverrides { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>KCL commands answered live, same idea. The value is the text that goes inside &lt;XMP&gt;.</summary>
    public ConcurrentDictionary<string, string> KclOverrides { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Every FTP command line received, password masked, in order.</summary>
    public ConcurrentQueue<string> FtpCommands { get; } = new();

    /// <summary>Every HTTP request line received.</summary>
    public ConcurrentQueue<string> HttpRequests { get; } = new();

    /// <summary>Anything that was not a read: FTP verbs outside the read set, HTTP methods other than GET.</summary>
    public ConcurrentQueue<string> RefusedCommands { get; } = new();

    public IPAddress Address => _address;

    public int FtpPort { get; private set; }

    public int HttpPort { get; private set; }

    /// <summary>Starts both servers. Port 0 picks a free one; read it back from the properties.</summary>
    public void Start(int ftpPort = 0, int httpPort = 0)
    {
        _ftp = new TcpListener(_address, ftpPort);
        _ftp.Start();
        FtpPort = ((IPEndPoint)_ftp.LocalEndpoint).Port;

        _http = new TcpListener(_address, httpPort);
        _http.Start();
        HttpPort = ((IPEndPoint)_http.LocalEndpoint).Port;

        _ftpLoop = AcceptLoop(_ftp, ServeFtpAsync);
        _httpLoop = AcceptLoop(_http, ServeHttpAsync);
    }

    public async ValueTask DisposeAsync()
    {
        await _stop.CancelAsync().ConfigureAwait(false);
        _ftp?.Stop();
        _http?.Stop();
        foreach (Task? loop in new[] { _ftpLoop, _httpLoop })
        {
            if (loop is not null)
            {
                try
                {
                    await loop.ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is OperationCanceledException or SocketException or ObjectDisposedException)
                {
                }
            }
        }

        _stop.Dispose();
    }

    private async Task AcceptLoop(TcpListener listener, Func<TcpClient, CancellationToken, Task> serve)
    {
        while (!_stop.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await listener.AcceptTcpClientAsync(_stop.Token).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is OperationCanceledException or SocketException or ObjectDisposedException)
            {
                return;
            }

            _ = Task.Run(async () =>
            {
                using (client)
                {
                    try
                    {
                        await serve(client, _stop.Token).ConfigureAwait(false);
                    }
                    catch (Exception ex) when (ex is IOException or SocketException or OperationCanceledException or ObjectDisposedException)
                    {
                        // A client going away mid-session is normal.
                    }
                }
            });
        }
    }

    // ------------------------------------------------------------------ FTP

    private async Task ServeFtpAsync(TcpClient client, CancellationToken cancellationToken)
    {
        NetworkStream stream = client.GetStream();
        using var reader = new StreamReader(stream, Wire, false, 1024, leaveOpen: true);
        string device = _profile.HomeDevice;
        string? user = null;
        bool loggedIn = false;
        TcpListener? passive = null;

        async Task Reply(string line) =>
            await stream.WriteAsync(Wire.GetBytes(line + "\r\n"), cancellationToken).ConfigureAwait(false);

        await Reply("220-" + _profile.Banner).ConfigureAwait(false);
        await Reply("220 Ready.").ConfigureAwait(false);

        try
        {
            while (true)
            {
                string? line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
                if (line is null)
                {
                    return;
                }

                int space = line.IndexOf(' ', StringComparison.Ordinal);
                string verb = (space < 0 ? line : line[..space]).ToUpperInvariant();
                string arg = space < 0 ? string.Empty : line[(space + 1)..];
                FtpCommands.Enqueue(verb == "PASS" ? "PASS ****" : line);

                if (!ReadVerbs.Contains(verb))
                {
                    RefusedCommands.Enqueue("FTP " + line);
                    await Reply("502 Command not implemented.").ConfigureAwait(false);
                    continue;
                }

                if (!loggedIn && verb is not ("USER" or "PASS" or "QUIT" or "SYST" or "NOOP"))
                {
                    await Reply("530 Not logged in.").ConfigureAwait(false);
                    continue;
                }

                switch (verb)
                {
                    case "USER":
                        user = arg;
                        await Reply("331 User name okay, need password.").ConfigureAwait(false);
                        break;
                    case "PASS":
                        loggedIn = _profile.FtpUser is null
                            || (string.Equals(user, _profile.FtpUser, StringComparison.Ordinal) && arg == (_profile.FtpPassword ?? string.Empty));
                        await Reply(loggedIn ? "230 User logged in [NORM]." : "530 Login incorrect.").ConfigureAwait(false);
                        break;
                    case "SYST":
                        await Reply("215 " + _profile.System).ConfigureAwait(false);
                        break;
                    case "NOOP":
                        await Reply("200 NOOP command successful.").ConfigureAwait(false);
                        break;
                    case "TYPE":
                        await Reply("200 Type set to " + arg + ".").ConfigureAwait(false);
                        break;
                    case "PWD":
                        await Reply($"257 \"{device}\" is current directory.").ConfigureAwait(false);
                        break;
                    case "CWD":
                        if (_profile.DeviceFolder(arg) is not null)
                        {
                            device = arg.ToLowerInvariant();
                            await Reply("250 CWD command successful.").ConfigureAwait(false);
                        }
                        else
                        {
                            await Reply("550 " + arg + ": No such device.").ConfigureAwait(false);
                        }

                        break;
                    case "PASV":
                        passive?.Stop();
                        passive = new TcpListener(_address, 0);
                        passive.Start();
                        int port = ((IPEndPoint)passive.LocalEndpoint).Port;
                        byte[] ip = _address.GetAddressBytes();
                        await Reply(string.Create(CultureInfo.InvariantCulture,
                            $"227 Entering Passive Mode ({ip[0]},{ip[1]},{ip[2]},{ip[3]},{port >> 8},{port & 255}).")).ConfigureAwait(false);
                        break;
                    case "NLST":
                    case "LIST":
                    case "RETR":
                        if (passive is null)
                        {
                            await Reply("425 Use PASV first.").ConfigureAwait(false);
                            break;
                        }

                        bool keepGoing = await TransferAsync(verb, arg, device, passive, Reply, cancellationToken).ConfigureAwait(false);
                        passive.Stop();
                        passive = null;
                        if (!keepGoing)
                        {
                            return;
                        }

                        break;
                    case "QUIT":
                        await Reply("221 Goodbye.").ConfigureAwait(false);
                        return;
                }
            }
        }
        finally
        {
            passive?.Stop();
        }
    }

    /// <summary>Returns false when a fault says the control connection should now be dropped.</summary>
    private async Task<bool> TransferAsync(
        string verb, string arg, string device, TcpListener passive, Func<string, Task> reply, CancellationToken cancellationToken)
    {
        string folder = _profile.DeviceFolder(device)!;
        byte[] payload;

        if (verb == "RETR")
        {
            string name = arg.Trim();
            string path = Path.Combine(folder, name);
            bool overridden = device.StartsWith("md", StringComparison.OrdinalIgnoreCase) && MdOverrides.ContainsKey(name);
            if (Faults.RefuseFiles.ContainsKey(name) || name.AsSpan().IndexOfAny("/\\") >= 0 || (!overridden && !File.Exists(path)))
            {
                await reply("550 " + name + ": File not found.").ConfigureAwait(false);
                return true;
            }

            if (Faults.DropOnceOnFile.TryRemove(name, out _))
            {
                await reply("150 Opening BINARY mode data connection.").ConfigureAwait(false);
                using TcpClient half = await passive.AcceptTcpClientAsync(cancellationToken).ConfigureAwait(false);
                byte[] all = await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
                await half.GetStream().WriteAsync(all.AsMemory(0, all.Length / 2), cancellationToken).ConfigureAwait(false);
                return false;
            }

            payload = overridden && MdOverrides.TryGetValue(name, out byte[]? live)
                ? live
                : await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            string prefix = _profile.NlstWithDevicePrefix ? device : string.Empty;
            IEnumerable<string> names = Directory.EnumerateFiles(folder).Select(Path.GetFileName).Order(StringComparer.OrdinalIgnoreCase)!;
            names = names.Concat(Faults.ExtraListedNames);
            var text = new StringBuilder();
            foreach (string name in names)
            {
                if (verb == "NLST")
                {
                    text.Append(prefix).Append(name).Append("\r\n");
                }
                else
                {
                    string path = Path.Combine(folder, name);
                    long size = File.Exists(path) ? new FileInfo(path).Length : 0;
                    text.Append(CultureInfo.InvariantCulture, $"-rw-rw-rw-   1 noone    nogroup  {size,10} Jan  1 00:00 {name}\r\n");
                }
            }

            payload = Wire.GetBytes(text.ToString());
        }

        await reply("150 Opening BINARY mode data connection.").ConfigureAwait(false);
        using (TcpClient data = await passive.AcceptTcpClientAsync(cancellationToken).ConfigureAwait(false))
        {
            await data.GetStream().WriteAsync(payload, cancellationToken).ConfigureAwait(false);
        }

        await reply("226 Transfer complete.").ConfigureAwait(false);
        return true;
    }

    // ------------------------------------------------------------------ HTTP

    private async Task ServeHttpAsync(TcpClient client, CancellationToken cancellationToken)
    {
        NetworkStream stream = client.GetStream();
        using var reader = new StreamReader(stream, Wire, false, 1024, leaveOpen: true);

        string? requestLine = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
        if (requestLine is null)
        {
            return;
        }

        while (!string.IsNullOrEmpty(await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false)))
        {
            // Headers are not needed.
        }

        HttpRequests.Enqueue(requestLine);
        string[] parts = requestLine.Split(' ');
        string method = parts[0];
        string target = parts.Length > 1 ? parts[1] : "/";

        (int status, string reason, string type, byte[] body) = method != "GET"
            ? Refuse(requestLine)
            : Route(target);

        string head = string.Create(CultureInfo.InvariantCulture,
            $"HTTP/1.1 {status} {reason}\r\nContent-Type: {type}\r\nContent-Length: {body.Length}\r\nConnection: close\r\n\r\n");
        await stream.WriteAsync(Wire.GetBytes(head), cancellationToken).ConfigureAwait(false);
        await stream.WriteAsync(body, cancellationToken).ConfigureAwait(false);
    }

    private (int, string, string, byte[]) Refuse(string requestLine)
    {
        RefusedCommands.Enqueue("HTTP " + requestLine);
        return (405, "Method Not Allowed", "text/plain", Wire.GetBytes("Method not allowed."));
    }

    private (int, string, string, byte[]) Route(string target)
    {
        string path = Uri.UnescapeDataString(target.Split('?')[0]);

        if (path.StartsWith("/MD/", StringComparison.OrdinalIgnoreCase))
        {
            if (_profile.DiagnosticFilesLocked)
            {
                return (401, "Unauthorized", "text/html", Wire.GetBytes("<html><body>Access denied</body></html>"));
            }

            string name = path[4..];
            if (MdOverrides.TryGetValue(name, out byte[]? live))
            {
                return (200, "OK", "text/plain", live);
            }

            string? folder = _profile.DeviceFolder("md:");
            string file = folder is null || name.AsSpan().IndexOfAny("/\\") >= 0 ? string.Empty : Path.Combine(folder, name);
            return File.Exists(file)
                ? (200, "OK", "text/plain", File.ReadAllBytes(file))
                : (404, "Not Found", "text/html", Wire.GetBytes("<html><body>Not found</body></html>"));
        }

        if (path.StartsWith("/KCL/", StringComparison.OrdinalIgnoreCase))
        {
            if (_profile.KclLocked)
            {
                return (401, "Unauthorized", "text/html", Wire.GetBytes("<html><body>Access denied</body></html>"));
            }

            string command = string.Join(' ', path[5..].Split(' ', StringSplitOptions.RemoveEmptyEntries));
            string page = KclOverrides.TryGetValue(command, out string? liveText)
                ? $"<html><body><XMP>{liveText}</XMP></body></html>"
                : _profile.Kcl.TryGetValue(command, out string? canned)
                ? canned
                : $"<html><body><XMP>KCL-012 Command not recognised by the simulator: {command}</XMP></body></html>";
            return (200, "OK", "text/html", Wire.GetBytes(page));
        }

        return (404, "Not Found", "text/html", Wire.GetBytes("<html><body>Not found</body></html>"));
    }
}
