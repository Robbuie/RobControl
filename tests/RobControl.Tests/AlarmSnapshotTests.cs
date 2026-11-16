using System.Text;
using RobControl.Core.Live;
using RobControl.Core.Trending;
using Xunit;

namespace RobControl.Tests;

public sealed class AlarmSnapshotTests : IDisposable
{
    private readonly TempFolder _temp = new();

    public void Dispose() => _temp.Dispose();

    [Fact]
    public async Task ANewAlarmWhileRecordingSavesTheLivePagesOnceAndAlarmsAlreadyActiveDoNot()
    {
        string folder = Fixtures.CopyOf(Fixtures.Synthetic, _temp);
        await using SimRig rig = SimRig.Start(Fixtures.Profile(folder));
        rig.Sim.MdOverrides["ERRACT.LS"] = Encoding.Latin1.GetBytes("16-NOV-26 08:00  SRVO-062 BZAL alarm\r\n");
        string snapshots = Path.Combine(_temp.Path, "snapshots");
        var taken = new List<AlarmSnapshot>();
        await using var recorder = new TrendRecorder(new NullStore(), rig.Sink) { Floor = TimeSpan.FromMilliseconds(30) };
        recorder.SnapshotTaken += (_, s) => { lock (taken) { taken.Add(s); } };

        var watch = new AlarmWatchOptions(snapshots) { CheckEvery = TimeSpan.FromMilliseconds(30), MinimumGap = TimeSpan.FromHours(1) };
        await recorder.StartAsync(rig.Robot, [], TimeSpan.FromMilliseconds(30), watch);
        Assert.True(recorder.IsWatchingAlarms(rig.Robot.Id));
        await Task.Delay(300);
        Assert.Empty(taken);

        rig.Sim.MdOverrides["ERRACT.LS"] = Encoding.Latin1.GetBytes("16-NOV-26 08:05  SRVO-007 External emergency stops\r\n16-NOV-26 08:00  SRVO-062 BZAL alarm\r\n");
        for (int i = 0; i < 200 && taken.Count == 0; i++)
        {
            await Task.Delay(25);
        }

        // A second new alarm inside the minimum gap is logged, not snapshotted.
        rig.Sim.MdOverrides["ERRACT.LS"] = Encoding.Latin1.GetBytes("16-NOV-26 08:06  SRVO-001 Operator panel E-stop\r\n");
        await Task.Delay(300);
        await recorder.StopAllAsync();

        AlarmSnapshot snapshot = Assert.Single(taken);
        Assert.Equal("SRVO-007", Assert.Single(snapshot.NewAlarms).Code);
        Assert.True(File.Exists(Path.Combine(snapshot.Folder, "MD", "IOSTATE.DG")));
        string md = File.ReadAllText(Path.Combine(snapshot.Folder, AlarmSnapshots.MarkdownFile));
        Assert.Contains("**SRVO-007**", md, StringComparison.Ordinal);
        Assert.Contains("Why it would not run", md, StringComparison.Ordinal);

        AlarmSnapshot listed = Assert.Single(AlarmSnapshots.List(snapshots));
        Assert.Equal(snapshot.Folder, listed.Folder);
        Assert.Equal("SRVO-007", listed.NewAlarms[0].Code);
        Assert.Contains(rig.Events, e => e.Message.StartsWith("Alarm snapshot: SRVO-007", StringComparison.Ordinal));
        Assert.Contains(rig.Events, e => e.Message.Contains("no snapshot", StringComparison.Ordinal));
        Assert.Empty(rig.Refused);
    }

    [Fact]
    public async Task NothingToRecordAndNoAlarmWatchIsRefused()
    {
        await using var recorder = new TrendRecorder(new NullStore());
        await Assert.ThrowsAsync<ArgumentException>(() => recorder.StartAsync(new Core.Robots.Robot("R", System.Net.IPAddress.Loopback), []));
    }

    private sealed class NullStore : ITrendStore
    {
        public bool Append(IReadOnlyList<TrendSample> samples) => true;
    }
}
