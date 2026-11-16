using RobControl.Core.Backup;
using Xunit;

namespace RobControl.Tests;

public sealed class LooseArchiveTests : IDisposable
{
    private readonly TempFolder _temp = new();

    public void Dispose() => _temp.Dispose();

    [Fact]
    public void AnArchiveARobotFolderAndOneBackupAllOpen()
    {
        DateTimeOffset t = new(2026, 11, 10, 3, 0, 0, TimeSpan.Zero);
        string root = Path.Combine(_temp.Path, "Stick", "Backups");
        FakeBackup.Write(root, "R1-01", t, ("A.TP", "1"));
        Core.Insight.RobotBackup newest = FakeBackup.Write(root, "R1-01", t.AddDays(1), ("A.TP", "2"));
        FakeBackup.Write(root, "R1-02", t, ("A.TP", "1"));
        Directory.CreateDirectory(Path.Combine(root, "not a robot"));

        LooseArchive archive = LooseArchive.Open(root);
        Assert.Equal("archive", archive.Shape);
        Assert.Equal(["R1-01", "R1-02"], archive.Robots.Select(r => r.Name));
        Assert.Equal("10.0.0.1", archive.Robots[0].Address.ToString());
        Assert.Equal(2, archive.Archive.List(archive.Robots[0]).Count);

        LooseArchive robot = LooseArchive.Open(Path.Combine(root, "R1-01"));
        Assert.Equal("one robot", robot.Shape);
        Assert.Equal(2, robot.Archive.List(Assert.Single(robot.Robots)).Count);

        LooseArchive one = LooseArchive.Open(newest.Set.FolderPath);
        Assert.Equal("one backup", one.Shape);
        Assert.Equal("R1-01", Assert.Single(one.Robots).Name);
        Assert.Contains("2 backup(s)", one.Describe(), StringComparison.Ordinal);
    }

    [Fact]
    public void AFolderWithoutBackupsSaysWhatToOpen()
    {
        Directory.CreateDirectory(Path.Combine(_temp.Path, "empty", "sub"));
        var ex = Assert.Throws<BackupArchiveException>(() => LooseArchive.Open(Path.Combine(_temp.Path, "empty")));
        Assert.Contains("manifest.json", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AManifestAddressThatIsNotAUnicastHostGetsThePlaceholder()
    {
        DateTimeOffset t = new(2026, 11, 10, 3, 0, 0, TimeSpan.Zero);
        Core.Insight.RobotBackup b = FakeBackup.Write(_temp.Path, "R9", t, ("A.TP", "1"));
        string manifest = Path.Combine(b.Set.FolderPath, BackupManifest.FileName);
        File.WriteAllText(manifest, File.ReadAllText(manifest).Replace("10.0.0.1", "255.255.255.255", StringComparison.Ordinal));

        Assert.Equal(LooseArchive.Placeholder, Assert.Single(LooseArchive.Open(_temp.Path).Robots).Address);
    }
}
