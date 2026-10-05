using RobControl.Core.Backup;
using RobControl.Core.History;
using RobControl.RobotSim;
using Xunit;

namespace RobControl.Tests;

public class BackupComparerTests
{
    [Fact]
    public async Task AnEditedProgramShowsAsChangedWithItsLines()
    {
        using var temp = new TempFolder();
        string profileFolder = Fixtures.CopyOf(Fixtures.Synthetic, temp);
        await using SimRig rig = SimRig.Start(SimProfile.Load(profileFolder));
        var archive = new BackupArchive(Path.Combine(temp.Path, "archive"));
        var runner = new BackupRunner(archive, "test");

        BackupResult before = await runner.RunAsync(rig.Robot);

        string main = Path.Combine(profileFolder, "MD", "MAIN.LS");
        File.WriteAllText(main, File.ReadAllText(main).Replace("CNT100", "CNT50", StringComparison.Ordinal));
        File.Delete(Path.Combine(profileFolder, "MD", "WELD_A.LS"));
        File.WriteAllText(Path.Combine(profileFolder, "MD", "WELD_B.LS"), "/PROG WELD_B\r\n/END\r\n");

        BackupResult after = await runner.RunAsync(rig.Robot);

        var older = new BackupSet(before.FolderPath!, before.Manifest);
        var newer = new BackupSet(after.FolderPath!, after.Manifest);
        IReadOnlyList<FileComparison> files = BackupComparer.Compare(older, newer);

        FileComparison changed = files.Single(f => f.RelativePath == "MD/MAIN.LS");
        Assert.Equal(FileChange.Changed, changed.Change);
        Assert.True(changed.CanDiff);
        Assert.Equal(FileChange.Removed, files.Single(f => f.RelativePath == "MD/WELD_A.LS").Change);
        Assert.Equal(FileChange.Added, files.Single(f => f.RelativePath == "MD/WELD_B.LS").Change);
        Assert.Equal(FileChange.Same, files.Single(f => f.RelativePath == "MD/MAIN.TP").Change);
        Assert.False(files.Single(f => f.RelativePath == "MD/MAIN.TP").CanDiff);

        IReadOnlyList<DiffLine> diff = BackupComparer.DiffFile(older, newer, changed);
        Assert.Contains(diff, l => l.Kind == DiffLineKind.Removed && l.Text.Contains("CNT100", StringComparison.Ordinal));
        Assert.Contains(diff, l => l.Kind == DiffLineKind.Added && l.Text.Contains("CNT50", StringComparison.Ordinal));
    }

    [Fact]
    public void DiagnosticFilesAreFlaggedAsLiveData()
    {
        Assert.True(new FileComparison("MD/SUMMARY.DG", FileChange.Changed, null, null).IsLiveData);
        Assert.False(new FileComparison("MD/MAIN.LS", FileChange.Changed, null, null).IsLiveData);
    }

    [Fact]
    public void AManifestCannotPointOutsideItsBackup()
    {
        var manifest = new BackupManifest { Tool = "t", RobotName = "r", Address = "127.0.0.1" };
        var set = new BackupSet(Path.Combine(Path.GetTempPath(), "some-backup"), manifest);
        Assert.Throws<BackupArchiveException>(() => set.PathOf(new BackupFileRecord("MD:", "x", "../../outside.txt", 1, "00")));
    }
}
