using RobControl.Core.Backup;
using RobControl.RobotSim;
using Xunit;

namespace RobControl.Tests;

/// <summary>Choosing devices, walking folders inside them, and leaving files out on purpose.</summary>
public sealed class BackupDevicesTests : IDisposable
{
    private const string Tool = "RobControl test";
    private readonly TempFolder _temp = new();
    private readonly string _profile;

    public BackupDevicesTests()
    {
        // The synthetic profile, plus folders on FR: and a FRA: device, the way an automatic-backup
        // area or a logbook folder would look.
        _profile = Path.Combine(_temp.Path, "profile");
        CopyTree(Fixtures.Folder(Fixtures.Synthetic), _profile);
        Write("FR/LOGBOOK/LOG1.LS", "log one");
        Write("FR/LOGBOOK/DEEP/NOTE.TXT", "deep");
        Write("FRA/AUTO1/ALL.VA", "auto backup");
        Write("FRA/README.TXT", "fra root");
    }

    public void Dispose() => _temp.Dispose();

    [Fact]
    public async Task SubfoldersAreCopiedIntoTheSameShapeAndListedInTheManifest()
    {
        await using SimRig rig = SimRig.Start(SimProfile.Load(_profile));
        string archive = Path.Combine(_temp.Path, "archive");
        var options = new BackupOptions { Devices = ["fr:", "fra:"], IncludeSubfolders = true };

        BackupResult result = await new BackupRunner(new BackupArchive(archive), Tool).RunAsync(rig.Robot, options);

        Assert.Equal(BackupOutcome.Complete, result.Outcome);
        Assert.Contains(result.Manifest.Files, f => f.RelativePath == "FR/LOGBOOK/LOG1.LS" && f.Name == "LOGBOOK/LOG1.LS" && f.Device == "FR:");
        Assert.Contains(result.Manifest.Files, f => f.RelativePath == "FR/LOGBOOK/DEEP/NOTE.TXT");
        Assert.Contains(result.Manifest.Files, f => f.RelativePath == "FRA/AUTO1/ALL.VA");
        Assert.Contains(result.Manifest.Files, f => f.RelativePath == "FRA/README.TXT");
        Assert.Equal("deep", File.ReadAllText(Path.Combine(result.FolderPath!, "FR", "LOGBOOK", "DEEP", "NOTE.TXT")));
        Assert.Contains("FR:/LOGBOOK", result.Manifest.Folders);
        Assert.Contains("FR:/LOGBOOK/DEEP", result.Manifest.Folders);
        Assert.Empty(result.Manifest.Failures);
        Assert.Empty(rig.Refused);

        // Whatever the walk did, Verify reads it back clean.
        BackupVerification check = BackupVerifier.Verify(new BackupSet(result.FolderPath!, result.Manifest));
        Assert.True(check.IsIntact, string.Join(", ", check.Missing.Concat(check.Changed).Concat(check.Unlisted)));
    }

    [Fact]
    public async Task WithSubfoldersOffAFolderIsNotedAndTheBackupIsStillComplete()
    {
        await using SimRig rig = SimRig.Start(SimProfile.Load(_profile));
        string archive = Path.Combine(_temp.Path, "archive");

        BackupResult result = await new BackupRunner(new BackupArchive(archive), Tool)
            .RunAsync(rig.Robot, new BackupOptions { Devices = ["fr:"], IncludeSubfolders = false });

        Assert.Equal(BackupOutcome.Complete, result.Outcome);
        Assert.Contains(result.Manifest.Files, f => f.RelativePath == "FR/EXTRA.TXT");
        Assert.DoesNotContain(result.Manifest.Files, f => f.RelativePath.StartsWith("FR/LOGBOOK", StringComparison.Ordinal));
        BackupFileProblem folder = Assert.Single(result.Manifest.NotCopied);
        Assert.Equal("LOGBOOK/", folder.Name);
        Assert.Contains("left out as asked", result.Manifest.Summary, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExcludedFilesAreLeftOutAndNamedButNeverAFailure()
    {
        await using SimRig rig = SimRig.Start(SimProfile.Load(_profile));
        string archive = Path.Combine(_temp.Path, "archive");
        var options = new BackupOptions { Devices = ["md:", "fr:"], IncludeSubfolders = true, Exclude = ["*.DG", "LOGBOOK/DEEP/*"] };

        BackupResult result = await new BackupRunner(new BackupArchive(archive), Tool).RunAsync(rig.Robot, options);

        Assert.Equal(BackupOutcome.Complete, result.Outcome);
        Assert.DoesNotContain(result.Manifest.Files, f => f.Name.EndsWith(".DG", StringComparison.Ordinal));
        Assert.Contains(result.Manifest.NotCopied, p => p.Name == "SUMMARY.DG" && p.Reason.Contains("*.DG", StringComparison.Ordinal));
        Assert.Contains(result.Manifest.NotCopied, p => p.Name == "LOGBOOK/DEEP/NOTE.TXT");
        Assert.Contains(result.Manifest.Files, f => f.RelativePath == "FR/LOGBOOK/LOG1.LS");
        Assert.False(File.Exists(Path.Combine(result.FolderPath!, "MD", "SUMMARY.DG")));
    }

    [Fact]
    public async Task MdNeedsNoExtraCommandsForFolders()
    {
        // Every MD: name has an extension, so the folder test never runs there: a backup of MD: sends
        // exactly the commands it always has.
        await using SimRig rig = SimRig.Start(SimProfile.Load(_profile));
        await new BackupRunner(new BackupArchive(Path.Combine(_temp.Path, "archive")), Tool)
            .RunAsync(rig.Robot, new BackupOptions { Devices = ["md:"], IncludeSubfolders = true });

        Assert.Single(rig.Sim.FtpCommands, c => c.StartsWith("CWD", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AMissingDeviceFailsThatDeviceOnlyAndTheRestArrives()
    {
        await using SimRig rig = SimRig.Start(SimProfile.Load(_profile));
        BackupResult result = await new BackupRunner(new BackupArchive(Path.Combine(_temp.Path, "archive")), Tool)
            .RunAsync(rig.Robot, new BackupOptions { Devices = ["md:", "mc:"] });

        Assert.Equal(BackupOutcome.Partial, result.Outcome);
        BackupFileProblem failure = Assert.Single(result.Manifest.Failures);
        Assert.Equal("MC:", failure.Device);
        Assert.Contains(result.Manifest.Files, f => f.Device == "MD:");
    }

    [Theory]
    [InlineData("*.DG", "SUMMARY.DG", true)]
    [InlineData("*.dg", "SUMMARY.DG", true)]
    [InlineData("*.DG", "LOGBOOK/X.DG", true)]
    [InlineData("LOGBOOK/*", "LOGBOOK/X.DG", true)]
    [InlineData("LOGBOOK/*", "LOGBOOK/DEEP/X.DG", false)]
    [InlineData("LOGBOOK/*/*", "LOGBOOK/DEEP/X.DG", true)]
    [InlineData("WELD_?.LS", "WELD_A.LS", true)]
    [InlineData("WELD_?.LS", "WELD_AB.LS", false)]
    [InlineData("*", "MAIN.TP", true)]
    [InlineData("MAIN.TP", "MAIN.TPX", false)]
    [InlineData("*.TP", "LOGBOOK/MAIN.LS", false)]
    public void PatternsMatchAsDocumented(string pattern, string path, bool expected) =>
        Assert.Equal(expected, FileNamePattern.FirstMatch([pattern], path) is not null);

    private void Write(string relative, string text)
    {
        string path = Path.Combine([_profile, .. relative.Split('/')]);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
    }

    private static void CopyTree(string from, string to)
    {
        foreach (string dir in Directory.GetDirectories(from, "*", SearchOption.AllDirectories))
        {
            Directory.CreateDirectory(Path.Combine(to, Path.GetRelativePath(from, dir)));
        }

        Directory.CreateDirectory(to);
        foreach (string file in Directory.GetFiles(from, "*", SearchOption.AllDirectories))
        {
            File.Copy(file, Path.Combine(to, Path.GetRelativePath(from, file)));
        }
    }
}
