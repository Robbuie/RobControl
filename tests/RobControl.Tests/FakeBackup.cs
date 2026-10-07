using System.Security.Cryptography;
using System.Text;
using RobControl.Core.Backup;
using RobControl.Core.Insight;

namespace RobControl.Tests;

/// <summary>
/// Writes a backup folder straight to disk - manifest and files - in the archive layout, without a
/// simulator. For tests of what is read from backups rather than how they are taken.
/// </summary>
internal static class FakeBackup
{
    public static RobotBackup Write(string archiveRoot, string robot, DateTimeOffset started, params (string Name, string Content)[] files) =>
        Write(archiveRoot, robot, started, BackupOutcome.Complete, files);

    public static RobotBackup Write(string archiveRoot, string robot, DateTimeOffset started, BackupOutcome outcome, params (string Name, string Content)[] files)
    {
        string stamp = ArchiveNames.Stamp(started);
        string folder = Path.Combine(archiveRoot, ArchiveNames.RobotFolder(robot), stamp);
        Directory.CreateDirectory(Path.Combine(folder, "MD"));
        var records = new List<BackupFileRecord>();
        foreach ((string name, string content) in files)
        {
            byte[] bytes = Encoding.Latin1.GetBytes(content.Replace("\n", "\r\n", StringComparison.Ordinal));
            File.WriteAllBytes(Path.Combine(folder, "MD", name), bytes);
            records.Add(new BackupFileRecord("md:", name, "MD/" + name, bytes.Length, Convert.ToHexString(SHA256.HashData(bytes))));
        }

        var manifest = new BackupManifest
        {
            Tool = "test",
            RobotName = robot,
            Address = "10.0.0.1",
            StartedUtc = started,
            FinishedUtc = started.AddMinutes(1),
            Outcome = outcome,
            Devices = ["md:"],
            Files = records,
        };
        File.WriteAllText(Path.Combine(folder, BackupManifest.FileName), manifest.ToJson());
        return new RobotBackup(robot, new BackupSet(folder, manifest));
    }
}
