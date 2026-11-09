using System.Net;
using System.Net.Sockets;
using RobControl.Core.Net;
using RobControl.Core.Robots;
using RobControl.Core.Transports.Ftp;
using Xunit;

namespace RobControl.Tests;

/// <summary>
/// "FTP did not answer" split into its causes. Silence on loopback cannot be produced (the kernel
/// refuses or accepts at once), so the web check is injected for those cases.
/// </summary>
public class FtpReachabilityTests
{
    private static readonly Robot Robot = new("R1-01", IPAddress.Loopback);

    private static FtpConnectException Silent() =>
        new("Nothing answered at 127.0.0.1:21 within 5 s.", "Check the robot is powered.", FtpConnectFailure.NoAnswer);

    [Fact]
    public async Task WebAnswersButFtpIsSilentPointsAtTheFtpServerNotTheNetwork()
    {
        FtpConnectException ex = await FtpReachability.ExplainAsync(Robot, Silent(), checkWeb: _ => Task.FromResult(PortState.Open));
        Assert.Equal(PortState.Open, ex.WebPort);
        Assert.Contains("is on the network", ex.Message, StringComparison.Ordinal);
        Assert.Contains("port 21", ex.Message, StringComparison.Ordinal);
        Assert.Contains("Host Comm", ex.Remediation, StringComparison.Ordinal);
        Assert.Contains("Servers", ex.Remediation, StringComparison.Ordinal);
        Assert.DoesNotContain("powered", ex.Remediation!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task NothingAnsweringAnywhereIsANetworkProblem()
    {
        FtpConnectException ex = await FtpReachability.ExplainAsync(Robot, Silent(), checkWeb: _ => Task.FromResult(PortState.NoAnswer));
        Assert.Contains("Nothing at R1-01 (127.0.0.1) answered", ex.Message, StringComparison.Ordinal);
        Assert.Contains("Diagnose network", ex.Remediation, StringComparison.Ordinal);
    }

    [Fact]
    public async Task WebRefusedToo()
    {
        FtpConnectException ex = await FtpReachability.ExplainAsync(Robot, Silent(), checkWeb: _ => Task.FromResult(PortState.Refused));
        Assert.Equal(PortState.Refused, ex.WebPort);
        Assert.Contains("refused at once", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ARefusalIsPassedThroughWithNothingSent()
    {
        var refused = new FtpConnectException("refused", "off", FtpConnectFailure.Refused);
        bool looked = false;
        FtpConnectException ex = await FtpReachability.ExplainAsync(Robot, refused, checkWeb: _ => { looked = true; return Task.FromResult(PortState.Open); });
        Assert.Same(refused, ex);
        Assert.False(looked);
    }

    [Fact]
    public async Task TheCheckIsWrittenIntoTheTranscript()
    {
        var transcript = new FtpTranscript();
        await FtpReachability.ExplainAsync(Robot, Silent(), transcript, _ => Task.FromResult(PortState.Open));
        Assert.Contains("web port 80: Open", transcript.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task PortCheckTellsOpenFromRefused()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int open = ((IPEndPoint)listener.LocalEndpoint).Port;
        Assert.Equal(PortState.Open, await PortCheck.CheckAsync(IPAddress.Loopback, open, TimeSpan.FromSeconds(3)));
        listener.Stop();
        Assert.Equal(PortState.Refused, await PortCheck.CheckAsync(IPAddress.Loopback, open, TimeSpan.FromSeconds(3)));
    }

    [Fact]
    public void PortCheckRefusesABroadcastAddressBeforeSending() =>
        Assert.Throws<RobotAddressException>(() => PortCheck.CheckAsync(IPAddress.Broadcast, 80, TimeSpan.FromSeconds(1)).GetAwaiter().GetResult());

    [Fact]
    public async Task AConnectionWithNoGreetingSaysAnotherClientMayHoldTheSessions()
    {
        // A port that accepts and then says nothing: what a controller with every FTP server tag
        // in use is expected to look like.
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        Task<TcpClient> accepted = listener.AcceptTcpClientAsync();
        try
        {
            var options = new FtpClientOptions { ReplyTimeout = TimeSpan.FromSeconds(1) };
            FtpConnectException ex = await Assert.ThrowsAsync<FtpConnectException>(() => FtpClient.ConnectAsync(IPAddress.Loopback, port, options));
            Assert.Equal(FtpConnectFailure.NoGreeting, ex.Failure);
            Assert.Contains("never said hello", ex.Message, StringComparison.Ordinal);
            Assert.Contains("Another FTP client", ex.Remediation, StringComparison.Ordinal);
        }
        finally
        {
            (await accepted).Dispose();
            listener.Stop();
        }
    }

    [Fact]
    public async Task ARefusedPortIsRefusedNotNoAnswer()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        FtpConnectException ex = await Assert.ThrowsAsync<FtpConnectException>(() => FtpClient.ConnectAsync(IPAddress.Loopback, port));
        Assert.Equal(FtpConnectFailure.Refused, ex.Failure);
    }
}
