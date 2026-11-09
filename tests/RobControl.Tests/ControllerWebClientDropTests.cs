using System.Net;
using System.Net.Sockets;
using System.Text;
using RobControl.Core.Transports.Http;
using Xunit;

namespace RobControl.Tests;

/// <summary>
/// A web server that takes the connection and then resets it - what a controller did to RobControl
/// ("An existing connection was forcibly closed by the remote host") while a browser read the same
/// pages. The client asks again, then by hand as HTTP/1.0, and says plainly what happened if neither works.
/// </summary>
public class ControllerWebClientDropTests
{
    private const string Page = "<html><body><pre>F Number: 0\nDIN[   1]  ON  *IMSTP\n</pre></body></html>";

    public ControllerWebClientDropTests() => ControllerWebClient.RetryPause = TimeSpan.FromMilliseconds(20);

    [Fact]
    public async Task ADroppedConnectionIsAskedAgainThenAskedAsHttp10()
    {
        await using var server = new ResettingServer(resets: 2, closeWithReset: true);
        using var web = new ControllerWebClient(IPAddress.Loopback, server.Port, timeout: TimeSpan.FromSeconds(10));

        HttpExchange exchange = await web.GetDiagnosticFileAsync("IOSTATE.DG");

        Assert.Equal(200, exchange.StatusCode);
        Assert.Contains("*IMSTP", DiagnosticPage.Text(exchange.Text), StringComparison.Ordinal);
        Assert.Equal(3, server.Requests.Count);
        Assert.All(server.Requests, r => Assert.StartsWith("GET /MD/IOSTATE.DG HTTP/1.", r, StringComparison.Ordinal));
        Assert.StartsWith("GET /MD/IOSTATE.DG HTTP/1.0", server.Requests[2], StringComparison.Ordinal);
        Assert.Contains("Connection: close", server.Requests[0], StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Accept:", server.Requests[0], StringComparison.OrdinalIgnoreCase);
        Assert.Equal(HttpAttempt.PlainHttp10, exchange.Via);
        Assert.Equal("only as plain HTTP/1.0", exchange.ViaText);
    }

    [Theory]
    [InlineData(0, HttpAttempt.FirstTry)]
    [InlineData(1, HttpAttempt.AfterRetry)]
    public async Task TheExchangeSaysWhichWayOfAskingAnswered(int resets, HttpAttempt expected)
    {
        await using var server = new ResettingServer(resets: resets, closeWithReset: false);
        using var web = new ControllerWebClient(IPAddress.Loopback, server.Port, timeout: TimeSpan.FromSeconds(10));
        HttpExchange exchange = await web.GetDiagnosticFileAsync("IOSTATE.DG");
        Assert.Equal(200, exchange.StatusCode);
        Assert.Equal(expected, exchange.Via);
        Assert.Equal(resets + 1, server.Requests.Count);
    }

    [Fact]
    public void ALiveReadSaysWhenThePagesNeededAFallback()
    {
        var files = new List<RobControl.Core.Live.LiveFile>
        {
            new("Active alarms", "ERRACT.LS", RobControl.Core.Live.LiveFileStatus.Ok, "", null) { Via = HttpAttempt.PlainHttp10 },
            new("I/O", "IOSTATE.DG", RobControl.Core.Live.LiveFileStatus.Ok, "", null) { Via = HttpAttempt.PlainHttp10 },
            new("Program state", "PRGSTATE.DG", RobControl.Core.Live.LiveFileStatus.Ok, "", null) { Via = HttpAttempt.AfterRetry },
        };
        var snapshot = new RobControl.Core.Live.LiveSnapshot(DateTimeOffset.UtcNow, files, [], [], new Dictionary<string, double>());
        Assert.Equal("Web server answered after a retry (1 page), only as plain HTTP/1.0 (2 pages).", snapshot.Route);
        Assert.EndsWith(snapshot.Route!, snapshot.Summary, StringComparison.Ordinal);

        var plain = new RobControl.Core.Live.LiveSnapshot(DateTimeOffset.UtcNow,
            [new("I/O", "IOSTATE.DG", RobControl.Core.Live.LiveFileStatus.Ok, "", null) { Via = HttpAttempt.FirstTry }], [], [], new Dictionary<string, double>());
        Assert.Null(plain.Route);
    }

    [Fact]
    public async Task AServerThatAlwaysDropsIsReportedAsDroppingNotAsUnreachable()
    {
        await using var server = new ResettingServer(resets: int.MaxValue, closeWithReset: false);
        using var web = new ControllerWebClient(IPAddress.Loopback, server.Port, timeout: TimeSpan.FromSeconds(10));

        ControllerHttpException ex = await Assert.ThrowsAsync<ControllerHttpException>(() => web.GetDiagnosticFileAsync("ERRACT.LS"));

        Assert.Null(ex.StatusCode);
        Assert.Contains("accepted the connection", ex.Message, StringComparison.Ordinal);
        Assert.Contains("browser", ex.Remediation!, StringComparison.Ordinal);
        Assert.Equal(3, server.Requests.Count);
    }

    [Fact]
    public async Task ARefusedConnectionIsNotRetried()
    {
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        int port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();

        using var web = new ControllerWebClient(IPAddress.Loopback, port, timeout: TimeSpan.FromSeconds(10));
        ControllerHttpException ex = await Assert.ThrowsAsync<ControllerHttpException>(() => web.GetDiagnosticFileAsync("ERRACT.LS"));
        Assert.Contains("Could not reach", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AnHttp10ReplyIsSplitIntoStatusTypeAndBody()
    {
        byte[] reply = Encoding.Latin1.GetBytes("HTTP/1.0 404 Not Found\r\nContent-Type: text/html\r\nDate: Fri, 06 Nov 2026 17:01:44 GMT\r\n\r\nmissing");
        HttpExchange exchange = RawHttpGet.Parse("/MD/X.DG", reply, TimeSpan.Zero);
        Assert.Equal(404, exchange.StatusCode);
        Assert.Equal("text/html", exchange.ContentType);
        Assert.Equal("missing", exchange.Text);
        Assert.Equal(new DateTimeOffset(2026, 11, 6, 17, 1, 44, TimeSpan.Zero), exchange.ServerDate);
    }

    /// <summary>Resets the first <c>resets</c> connections once their request has arrived, then answers.</summary>
    private sealed class ResettingServer : IAsyncDisposable
    {
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
        private readonly CancellationTokenSource _stop = new();
        private readonly Task _loop;
        private readonly int _resets;
        private readonly bool _closeWithReset;

        public ResettingServer(int resets, bool closeWithReset)
        {
            _resets = resets;
            _closeWithReset = closeWithReset;
            _listener.Start();
            _loop = Task.Run(LoopAsync);
        }

        public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;

        public List<string> Requests { get; } = [];

        public async ValueTask DisposeAsync()
        {
            await _stop.CancelAsync();
            _listener.Stop();
            try
            {
                await _loop;
            }
            catch (Exception ex) when (ex is OperationCanceledException or SocketException or ObjectDisposedException)
            {
            }

            _stop.Dispose();
        }

        private async Task LoopAsync()
        {
            while (!_stop.IsCancellationRequested)
            {
                using TcpClient client = await _listener.AcceptTcpClientAsync(_stop.Token);
                NetworkStream stream = client.GetStream();
                string request = await ReadRequestAsync(stream);
                int count;
                lock (Requests)
                {
                    Requests.Add(request);
                    count = Requests.Count;
                }

                if (count > _resets)
                {
                    // Answer like a controller: no Content-Length, the page ends with </html>.
                    byte[] reply = Encoding.Latin1.GetBytes("HTTP/1.0 200 OK\r\nContent-Type: text/html\r\n\r\n" + Page);
                    await stream.WriteAsync(reply, _stop.Token);
                    await stream.FlushAsync(_stop.Token);
                    if (!_closeWithReset)
                    {
                        continue;
                    }

                    await Task.Delay(50, _stop.Token);
                }

                client.Client.LingerState = new LingerOption(true, 0);
                client.Client.Close();
            }
        }

        private static async Task<string> ReadRequestAsync(NetworkStream stream)
        {
            var text = new StringBuilder();
            byte[] buffer = new byte[4096];
            while (!text.ToString().Contains("\r\n\r\n", StringComparison.Ordinal))
            {
                int read = await stream.ReadAsync(buffer);
                if (read == 0)
                {
                    break;
                }

                text.Append(Encoding.ASCII.GetString(buffer, 0, read));
            }

            return text.ToString();
        }
    }
}
