using RobControl.Core.Controllers;
using RobControl.Core.Events;
using RobControl.Core.Persistence;
using Xunit;

namespace RobControl.Tests;

public class ClockTests
{
    private static readonly TimeZoneInfo Detroit = TimeZoneInfo.CreateCustomTimeZone("test-4", TimeSpan.FromHours(-4), "test-4", "test-4");
    private static readonly DateTimeOffset PcUtc = new(2026, 10, 7, 14, 0, 0, TimeSpan.Zero);

    [Fact]
    public void AControllerOnUtcThatAgreesIsNotOff()
    {
        ClockReading r = ClockReading.Compare(PcUtc.AddSeconds(4), PcUtc, Detroit);
        Assert.False(r.IsOff);
        Assert.False(r.AsLocalTime);
        Assert.Equal("Controller clock matches this PC (within 4 s).", r.Describe());
    }

    [Fact]
    public void LocalTimeLabelledGmtIsReadAsTheRightTimeNotFourHoursOut()
    {
        // 10:00 local in Detroit, sent as "10:00 GMT".
        var header = new DateTimeOffset(2026, 10, 7, 10, 0, 30, TimeSpan.Zero);
        ClockReading r = ClockReading.Compare(header, PcUtc, Detroit);
        Assert.True(r.AsLocalTime);
        Assert.False(r.IsOff);
        Assert.Equal(TimeSpan.FromSeconds(30), r.Skew);
    }

    [Fact]
    public void ADriftedClockIsOffAndSaysWhichWay()
    {
        ClockReading behind = ClockReading.Compare(PcUtc.AddMinutes(-12).AddSeconds(-30), PcUtc, Detroit);
        Assert.True(behind.IsOff);
        Assert.Equal("Controller clock is 12 min 30 s behind this PC.", behind.Describe());

        ClockReading ahead = ClockReading.Compare(PcUtc.AddDays(400), PcUtc, Detroit);
        Assert.True(ahead.IsOff);
        Assert.StartsWith("Controller clock is 400 d", ahead.Describe(), StringComparison.Ordinal);
        Assert.Contains("ahead of", ahead.Describe(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheProbeReadsTheClockFromTheWebServerAndWarnsWhenItIsOff()
    {
        await using SimRig rig = SimRig.Start(Fixtures.Profile(Fixtures.Folder(Fixtures.Synthetic), p => p with { ClockOffsetSeconds = -900 }));

        ProbeReport report = await new CapabilityProbe(rig.Sink).RunAsync(rig.Robot);

        Assert.NotNull(report.Clock);
        Assert.True(report.Clock!.IsOff);
        ProbeStep http = Assert.Single(report.Steps, s => s.Name == CapabilityProbe.HttpStep);
        Assert.Contains("behind this PC", http.Message, StringComparison.Ordinal);
        Assert.Contains("clock", http.Remediation, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(rig.Events, e => e.Category == EventCategory.Probe && e.Severity == EventSeverity.Warn && e.Message.Contains("behind", StringComparison.Ordinal));
        Assert.Empty(rig.Refused);
    }

    [Fact]
    public async Task NoDateHeaderMeansNoClockReadingAndNoComplaint()
    {
        await using SimRig rig = SimRig.Start(Fixtures.Profile(Fixtures.Folder(Fixtures.Synthetic)));

        ProbeReport report = await new CapabilityProbe(rig.Sink).RunAsync(rig.Robot);

        Assert.Null(report.Clock);
        Assert.Null(Assert.Single(report.Steps, s => s.Name == CapabilityProbe.HttpStep).Remediation);
    }

    [Fact]
    public async Task ARightClockSendingLocalTimeIsNotFlagged()
    {
        await using SimRig rig = SimRig.Start(Fixtures.Profile(Fixtures.Folder(Fixtures.Synthetic), p => p with { ClockOffsetSeconds = 0, ClockIsLocalTime = true }));

        ProbeReport report = await new CapabilityProbe(rig.Sink).RunAsync(rig.Robot);

        Assert.False(report.Clock!.IsOff);
    }
}
