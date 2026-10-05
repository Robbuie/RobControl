using System.Net;
using RobControl.Core.Kcl;
using RobControl.Core.Transports.Http;
using Xunit;

namespace RobControl.Tests;

public class ControllerWebClientTests
{
    [Fact]
    public async Task AWriteIsRefusedWithNothingSent()
    {
        await using SimRig rig = SimRig.Start(Fixtures.Profile(Fixtures.Folder(Fixtures.Synthetic)));
        using var web = new ControllerWebClient(IPAddress.Loopback, rig.Sim.HttpPort);

        KclRefusedException ex = await Assert.ThrowsAsync<KclRefusedException>(() => web.KclAsync("SET VAR $NUMREG[2] = 0"));
        Assert.Equal(KclCommandClass.Write, ex.Classification.Class);
        await Assert.ThrowsAsync<KclRefusedException>(() => web.KclAsync("RUN MAIN"));
        Assert.Empty(rig.Sim.HttpRequests);
    }

    [Fact]
    public async Task AReadComesBackAsPlainText()
    {
        await using SimRig rig = SimRig.Start(Fixtures.Profile(Fixtures.Folder(Fixtures.Synthetic)));
        using var web = new ControllerWebClient(IPAddress.Loopback, rig.Sim.HttpPort);

        KclResult result = await web.KclAsync("show  var $NUMREG[2]");
        Assert.Contains("1287", result.Output, StringComparison.Ordinal);
        Assert.Null(result.Error);
        Assert.Contains("/KCL/show%20var%20$NUMREG[2]", rig.Sim.HttpRequests.Single(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task OnlyGetIsEverSent()
    {
        await using SimRig rig = SimRig.Start(Fixtures.Profile(Fixtures.Folder(Fixtures.Synthetic)));
        using var web = new ControllerWebClient(IPAddress.Loopback, rig.Sim.HttpPort);
        await web.GetDiagnosticFileAsync("SUMMARY.DG");
        await web.KclAsync("SHOW CLOCK");

        Assert.All(rig.Sim.HttpRequests, r => Assert.StartsWith("GET ", r, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("../x")]
    [InlineData("a/b")]
    [InlineData("x?y")]
    public async Task DiagnosticFileNamesMustBePlain(string name)
    {
        using var web = new ControllerWebClient(IPAddress.Loopback, 80);
        await Assert.ThrowsAsync<ArgumentException>(() => web.GetDiagnosticFileAsync(name));
    }

    [Fact]
    public void BroadcastIsRefusedBeforeAnythingIsBuilt() =>
        Assert.Throws<Core.Net.RobotAddressException>(() => new ControllerWebClient(IPAddress.Broadcast));
}
