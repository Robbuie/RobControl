using RobControl.Core.Controllers;
using RobControl.Core.Events;
using RobControl.RobotSim;
using Xunit;

namespace RobControl.Tests;

public class CapabilityProbeTests
{
    [Fact]
    public async Task AnOpenControllerIsFullyAvailableAndIdentified()
    {
        await using SimRig rig = SimRig.Start(Fixtures.Profile(Fixtures.Folder(Fixtures.Synthetic)));
        ProbeReport report = await new CapabilityProbe(rig.Sink).RunAsync(rig.Robot);

        Assert.Equal(ProbeOutcome.Available, report.Ftp);
        Assert.Equal(ProbeOutcome.Available, report.DiagnosticFiles);
        Assert.Equal(ProbeOutcome.Available, report.Kcl);
        Assert.True(report.CanBackUp);
        Assert.Equal(ControllerGeneration.R30iBPlus, report.Identity.Generation);
        Assert.Equal("F123456", report.Identity.FNumber);
        Assert.Single(rig.Events, e => e.Category == EventCategory.Probe);
        Assert.Empty(rig.Refused);
    }

    [Fact]
    public async Task LockedKclIsRefusedWithTheMenuPath()
    {
        await using SimRig rig = SimRig.Start(Fixtures.Profile(Fixtures.Folder(Fixtures.Synthetic), p => p with { KclLocked = true }));
        ProbeReport report = await new CapabilityProbe().RunAsync(rig.Robot);

        Assert.Equal(ProbeOutcome.Available, report.Ftp);
        Assert.Equal(ProbeOutcome.Refused, report.Kcl);
        ProbeStep kcl = report.Steps.Single(s => s.Name == CapabilityProbe.KclStep);
        Assert.Contains("Host Comm > HTTP", kcl.Remediation!, StringComparison.Ordinal);
        Assert.Equal(ControllerGeneration.R30iBPlus, report.Identity.Generation);
    }

    [Fact]
    public async Task LockedDiagnosticFilesStillLetKclAndFtpThrough()
    {
        await using SimRig rig = SimRig.Start(Fixtures.Profile(Fixtures.Folder(Fixtures.Synthetic), p => p with { DiagnosticFilesLocked = true }));
        ProbeReport report = await new CapabilityProbe().RunAsync(rig.Robot);

        Assert.Equal(ProbeOutcome.Refused, report.DiagnosticFiles);
        Assert.Equal(ProbeOutcome.Available, report.Kcl);
        Assert.Equal("V9.40P/23", report.Identity.SoftwareVersion);
    }

    [Fact]
    public async Task AFtpLoginRefusalIsRefusedNotUnreachable()
    {
        await using SimRig rig = SimRig.Start(Fixtures.Profile(Fixtures.Folder(Fixtures.Synthetic), p => p with { FtpUser = "robot", FtpPassword = "x" }));
        ProbeReport report = await new CapabilityProbe().RunAsync(rig.Robot);

        Assert.Equal(ProbeOutcome.Refused, report.Ftp);
        Assert.False(report.CanBackUp);
    }

    [Fact]
    public async Task NothingThereIsUnreachableEverywhere()
    {
        var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        int port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();

        var robot = new Core.Robots.Robot("Gone", System.Net.IPAddress.Loopback) { FtpPort = port, HttpPort = port };
        ProbeReport report = await new CapabilityProbe().RunAsync(robot);

        Assert.Equal(ProbeOutcome.Unreachable, report.Ftp);
        Assert.Equal(ProbeOutcome.Unreachable, report.DiagnosticFiles);
        Assert.Equal(ProbeOutcome.NotChecked, report.Kcl);
    }
}
