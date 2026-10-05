using System.Net;
using RobControl.Core.Transports.Ftp;
using Xunit;

namespace RobControl.Tests;

public class PassiveEndpointTests
{
    [Fact]
    public void PortComesFromTheReply()
    {
        var reply = new FtpReply(227, ["227 Entering Passive Mode (10,20,1,54,19,137)."]);
        IPEndPoint endpoint = PassiveEndpoint.Parse(reply, IPAddress.Parse("10.20.1.54"));
        Assert.Equal((19 << 8) | 137, endpoint.Port);
    }

    [Fact]
    public void AdvertisedAddressIsIgnoredInFavourOfTheControlHost()
    {
        // A controller with a second port, or behind NAT, can advertise an address we cannot reach.
        var reply = new FtpReply(227, ["227 Entering Passive Mode (192,168,0,1,4,0)."]);
        IPEndPoint endpoint = PassiveEndpoint.Parse(reply, IPAddress.Parse("10.20.1.54"));
        Assert.Equal(IPAddress.Parse("10.20.1.54"), endpoint.Address);
        Assert.Equal(1024, endpoint.Port);
    }

    [Fact]
    public void ARefusalSaysActiveModeMayBeNeeded()
    {
        var reply = new FtpReply(502, ["502 Command not implemented."]);
        FtpException ex = Assert.Throws<FtpException>(() => PassiveEndpoint.Parse(reply, IPAddress.Loopback));
        Assert.Contains("active", ex.Remediation!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void OutOfRangeNumbersAreRejected()
    {
        var reply = new FtpReply(227, ["227 Entering Passive Mode (10,20,1,54,300,1)."]);
        Assert.Throws<FtpProtocolException>(() => PassiveEndpoint.Parse(reply, IPAddress.Loopback));
    }
}
