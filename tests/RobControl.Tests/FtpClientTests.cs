using System.Net;
using RobControl.Core.Transports.Ftp;
using RobControl.RobotSim;
using Xunit;

namespace RobControl.Tests;

public class FtpClientTests
{
    [Fact]
    public async Task ListsAndRetrievesFromTheSimulator()
    {
        await using SimRig rig = SimRig.Start(Fixtures.Profile(Fixtures.Folder(Fixtures.Synthetic)));
        await using FtpClient ftp = await FtpClient.ConnectAsync(IPAddress.Loopback, rig.Sim.FtpPort);
        Assert.Contains("R-30iB Plus", ftp.Banner.Text, StringComparison.Ordinal);

        await ftp.LoginAsync(FtpCredentials.Default);
        Assert.Equal("md:", await ftp.PrintWorkingDirectoryAsync());

        IReadOnlyList<string> names = await ftp.ListNamesAsync();
        Assert.Contains("SUMMARY.DG", names);
        Assert.Contains("MAIN.TP", names);

        using var buffer = new MemoryStream();
        long bytes = await ftp.RetrieveAsync("MAIN.TP", buffer);
        byte[] expected = File.ReadAllBytes(Path.Combine(Fixtures.Folder(Fixtures.Synthetic), "MD", "MAIN.TP"));
        Assert.Equal(expected.Length, bytes);
        Assert.Equal(expected, buffer.ToArray());

        await ftp.QuitAsync();
        Assert.Empty(rig.Refused);
    }

    [Theory]
    [InlineData("STOR")]
    [InlineData("DELE")]
    [InlineData("RNFR")]
    [InlineData("MKD")]
    [InlineData("SITE")]
    [InlineData("APPE")]
    public async Task WritingVerbsCannotBeSentAtAll(string verb)
    {
        await using SimRig rig = SimRig.Start(Fixtures.Profile(Fixtures.Folder(Fixtures.Synthetic)));
        await using FtpClient ftp = await FtpClient.ConnectAsync(IPAddress.Loopback, rig.Sim.FtpPort);
        await ftp.LoginAsync(FtpCredentials.Default);

        await Assert.ThrowsAsync<InvalidOperationException>(() => ftp.SendAsync(verb, "MAIN.TP"));
        Assert.DoesNotContain(rig.Sim.FtpCommands, c => c.StartsWith(verb, StringComparison.Ordinal));
    }

    [Fact]
    public async Task ALineBreakInAnArgumentIsRefusedBeforeSending()
    {
        await using SimRig rig = SimRig.Start(Fixtures.Profile(Fixtures.Folder(Fixtures.Synthetic)));
        await using FtpClient ftp = await FtpClient.ConnectAsync(IPAddress.Loopback, rig.Sim.FtpPort);
        await ftp.LoginAsync(FtpCredentials.Default);

        await Assert.ThrowsAsync<ArgumentException>(() => ftp.SendAsync("RETR", "MAIN.TP\r\nDELE MAIN.TP"));
        Assert.Empty(rig.Refused);
    }

    [Fact]
    public async Task AWrongPasswordSaysWhereTheSettingIs()
    {
        SimProfile profile = Fixtures.Profile(Fixtures.Folder(Fixtures.Synthetic), p => p with { FtpUser = "robot", FtpPassword = "secret" });
        await using SimRig rig = SimRig.Start(profile);
        await using FtpClient ftp = await FtpClient.ConnectAsync(IPAddress.Loopback, rig.Sim.FtpPort);

        FtpException ex = await Assert.ThrowsAsync<FtpException>(() => ftp.LoginAsync(new FtpCredentials("robot", "wrong")));
        Assert.Equal(530, ex.Reply!.Code);
        Assert.Contains("Host Comm", ex.Remediation!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ThePasswordNeverReachesTheTranscript()
    {
        SimProfile profile = Fixtures.Profile(Fixtures.Folder(Fixtures.Synthetic), p => p with { FtpUser = "robot", FtpPassword = "hunter2" });
        await using SimRig rig = SimRig.Start(profile);
        await using FtpClient ftp = await FtpClient.ConnectAsync(IPAddress.Loopback, rig.Sim.FtpPort);
        await ftp.LoginAsync(new FtpCredentials("robot", "hunter2"));

        Assert.DoesNotContain("hunter2", ftp.Transcript.ToString(), StringComparison.Ordinal);
        Assert.Contains("PASS ****", ftp.Transcript.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task NothingListeningIsAConnectErrorThatSaysSo()
    {
        // Bind and release a port so nothing is on it.
        var probe = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        int port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();

        FtpConnectException ex = await Assert.ThrowsAsync<FtpConnectException>(() => FtpClient.ConnectAsync(IPAddress.Loopback, port));
        Assert.Contains("refused", ex.Message, StringComparison.Ordinal);
    }
}
