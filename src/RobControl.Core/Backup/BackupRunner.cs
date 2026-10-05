using System.Globalization;
using System.Security.Cryptography;
using RobControl.Core.Controllers;
using RobControl.Core.Events;
using RobControl.Core.Robots;
using RobControl.Core.Transports.Ftp;

namespace RobControl.Core.Backup;

/// <summary>
/// Backs up one robot over FTP into the archive.
///
/// <para><b>What it sends:</b> USER/PASS, TYPE I, CWD to each device, PASV, NLST, RETR. Nothing that
/// writes - <see cref="FtpClient"/> cannot send anything else.</para>
///
/// <para><b>Complete or marked incomplete, never in between.</b> Files land in
/// <c>&lt;stamp&gt;.partial</c>; only when the manifest is written is the folder renamed - to
/// <c>&lt;stamp&gt;</c> if every listed file arrived, to <c>&lt;stamp&gt;_INCOMPLETE</c> otherwise. A
/// crash mid-backup leaves a <c>.partial</c> folder the history list shows as unreadable, never a
/// folder that looks like a good backup.</para>
///
/// <para><b>One file failing does not end the backup.</b> A controller can refuse to generate one
/// listing while busy; that file is retried once on a fresh session, then recorded as a failure,
/// and the rest of the backup carries on.</para>
/// </summary>
public sealed class BackupRunner
{
    public const string TranscriptFileName = "ftp-transcript.txt";

    private readonly BackupArchive _archive;
    private readonly IEventSink _events;
    private readonly string _tool;
    private readonly TimeProvider _time;

    /// <param name="tool">Written into every manifest: "RobControl 0.1.0+a1b2c3d".</param>
    public BackupRunner(BackupArchive archive, string tool, IEventSink? events = null, TimeProvider? time = null)
    {
        ArgumentNullException.ThrowIfNull(archive);
        ArgumentException.ThrowIfNullOrWhiteSpace(tool);
        _archive = archive;
        _tool = tool;
        _events = events ?? NullEventSink.Instance;
        _time = time ?? TimeProvider.System;
    }

    public async Task<BackupResult> RunAsync(
        Robot robot,
        BackupOptions? options = null,
        ControllerIdentity? knownIdentity = null,
        IProgress<BackupProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(robot);
        options ??= BackupOptions.Default;

        DateTimeOffset started = _time.GetUtcNow();
        string robotDir = _archive.RobotFolder(robot);
        Directory.CreateDirectory(robotDir);
        string stamp = UniqueStamp(robotDir, ArchiveNames.Stamp(started));
        string work = Path.Combine(robotDir, stamp + ArchiveNames.InProgressSuffix);
        Directory.CreateDirectory(work);

        var transcript = new FtpTranscript(_time);
        var files = new List<BackupFileRecord>();
        var failures = new List<BackupFileProblem>();
        var skipped = new List<BackupFileProblem>();
        ControllerIdentity identity = knownIdentity ?? ControllerIdentity.Unknown;
        BackupOutcome outcome;
        string summary;

        _events.Info(EventCategory.Backup, robot, "Backup started.", string.Join(", ", options.Devices));
        var session = new Session(robot, options, transcript);

        try
        {
            FtpClient first = await session.OpenAsync(null, cancellationToken).ConfigureAwait(false);
            identity = identity.Merge(ControllerIdentityParser.Parse(first.Banner.Text));

            foreach (string device in options.Devices)
            {
                string label = ArchiveNames.DeviceLabel(device);
                IReadOnlyList<string> names;
                try
                {
                    progress?.Report(new BackupProgress(robot.Name, $"Listing {label}", 0, 0, null, Total(files)));
                    FtpClient ftp = await session.OpenAsync(device, cancellationToken).ConfigureAwait(false);
                    names = await ftp.ListNamesAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex) when (IsTransferFailure(ex, cancellationToken))
                {
                    failures.Add(new BackupFileProblem(label, "*", $"Could not list {label}: {ex.Message}"));
                    await session.DropAsync().ConfigureAwait(false);
                    continue;
                }

                string deviceDir = Path.Combine(work, ArchiveNames.DeviceFolder(device));
                Directory.CreateDirectory(deviceDir);

                var wanted = new List<string>();
                foreach (string listed in names)
                {
                    string name = ArchiveNames.StripDevice(listed, device);
                    if (ArchiveNames.IsSafeFileName(name, out string? problem))
                    {
                        wanted.Add(name);
                    }
                    else
                    {
                        skipped.Add(new BackupFileProblem(label, listed, problem!));
                    }
                }

                for (int i = 0; i < wanted.Count; i++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    string name = wanted[i];
                    progress?.Report(new BackupProgress(robot.Name, $"Copying {label}", i + 1, wanted.Count, name, Total(files)));

                    BackupFileRecord? record = await FetchAsync(session, device, label, name, deviceDir, options, failures, cancellationToken)
                        .ConfigureAwait(false);
                    if (record is not null)
                    {
                        files.Add(record);
                    }

                    if (options.PauseBetweenFiles > TimeSpan.Zero)
                    {
                        await Task.Delay(options.PauseBetweenFiles, _time, cancellationToken).ConfigureAwait(false);
                    }
                }
            }

            (outcome, summary) = Judge(files, failures, skipped);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            outcome = BackupOutcome.Cancelled;
            summary = string.Create(CultureInfo.InvariantCulture, $"Stopped after {files.Count} files.");
        }
        catch (Exception ex) when (IsTransferFailure(ex, cancellationToken))
        {
            outcome = BackupOutcome.Failed;
            string next = ex is RobControlException { Remediation: { } r } ? " " + r : string.Empty;
            summary = ex.Message + next;
        }
        finally
        {
            await session.DropAsync().ConfigureAwait(false);
        }

        var manifest = new BackupManifest
        {
            Tool = _tool,
            RobotName = robot.Name,
            Address = robot.Address.ToString(),
            Line = robot.Line,
            Identity = identity,
            StartedUtc = started,
            FinishedUtc = _time.GetUtcNow(),
            Outcome = outcome,
            Summary = summary,
            Devices = [.. options.Devices.Select(ArchiveNames.DeviceLabel)],
            Files = files,
            Failures = failures,
            Skipped = skipped,
        };

        string? final = Seal(work, robotDir, stamp, manifest, transcript);
        LogResult(robot, manifest);
        return new BackupResult(manifest, final, transcript.ToString());
    }

    private static async Task<BackupFileRecord?> FetchAsync(
        Session session, string device, string label, string name, string deviceDir,
        BackupOptions options, List<BackupFileProblem> failures, CancellationToken cancellationToken)
    {
        string target = Path.Combine(deviceDir, name);
        Exception? last = null;

        for (int attempt = 1; attempt <= Math.Max(1, options.AttemptsPerFile); attempt++)
        {
            try
            {
                FtpClient ftp = await session.OpenAsync(device, cancellationToken).ConfigureAwait(false);
                long bytes;
                await using (var file = new FileStream(target, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    bytes = await ftp.RetrieveAsync(name, file, cancellationToken).ConfigureAwait(false);
                    await file.FlushAsync(cancellationToken).ConfigureAwait(false);
                }

                string sha;
                await using (var read = new FileStream(target, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    sha = Convert.ToHexStringLower(await SHA256.HashDataAsync(read, cancellationToken).ConfigureAwait(false));
                }

                return new BackupFileRecord(label, name, $"{ArchiveNames.DeviceFolder(device)}/{name}", bytes, sha);
            }
            catch (Exception ex) when (IsTransferFailure(ex, cancellationToken))
            {
                last = ex;
                TryDelete(target);

                // A refused file (550) leaves the session healthy; anything else may not have. Either
                // way the retry starts clean, because a half-read data connection can leave a reply
                // queued that would be read as the answer to the next command.
                await session.DropAsync().ConfigureAwait(false);
            }
        }

        failures.Add(new BackupFileProblem(label, name, last?.Message ?? "Unknown failure."));
        return null;
    }

    private static (BackupOutcome, string) Judge(List<BackupFileRecord> files, List<BackupFileProblem> failures, List<BackupFileProblem> skipped)
    {
        string skippedNote = skipped.Count == 0
            ? string.Empty
            : string.Create(CultureInfo.InvariantCulture, $" {skipped.Count} listed names were not safe to write and were skipped.");

        if (files.Count == 0)
        {
            return (BackupOutcome.Failed, failures.Count == 0
                ? "The controller listed no files." + skippedNote
                : "No files arrived. First problem: " + failures[0].Reason + skippedNote);
        }

        if (failures.Count > 0 || skipped.Count > 0)
        {
            return (BackupOutcome.Partial, string.Create(CultureInfo.InvariantCulture,
                $"{files.Count} files arrived; {failures.Count} did not.{skippedNote}"));
        }

        return (BackupOutcome.Complete, string.Create(CultureInfo.InvariantCulture,
            $"{files.Count} files, {Total(files):N0} bytes."));
    }

    /// <summary>Writes the manifest and transcript, then renames the folder to say what it is.</summary>
    private static string? Seal(string work, string robotDir, string stamp, BackupManifest manifest, FtpTranscript transcript)
    {
        File.WriteAllText(Path.Combine(work, BackupManifest.FileName), manifest.ToJson());
        File.WriteAllText(Path.Combine(work, TranscriptFileName), transcript.ToString());

        string finalName = manifest.Outcome == BackupOutcome.Complete ? stamp : stamp + ArchiveNames.IncompleteSuffix;
        string final = Path.Combine(robotDir, finalName);
        Directory.Move(work, final);
        return final;
    }

    private void LogResult(Robot robot, BackupManifest manifest)
    {
        string detail = string.Join('\n', manifest.Failures.Concat(manifest.Skipped).Select(p => $"{p.Device}{p.Name}: {p.Reason}"));
        string message = $"Backup {manifest.Outcome.ToString().ToLowerInvariant()}: {manifest.Summary}";
        switch (manifest.Outcome)
        {
            case BackupOutcome.Complete:
                _events.Info(EventCategory.Backup, robot, message, detail.Length == 0 ? null : detail);
                break;
            case BackupOutcome.Failed:
                _events.Error(EventCategory.Backup, robot, message, detail.Length == 0 ? null : detail);
                break;
            default:
                _events.Warn(EventCategory.Backup, robot, message, detail.Length == 0 ? null : detail);
                break;
        }
    }

    private static string UniqueStamp(string robotDir, string stamp)
    {
        string candidate = stamp;
        for (int n = 2; Exists(candidate); n++)
        {
            candidate = string.Create(CultureInfo.InvariantCulture, $"{stamp}-{n}");
        }

        return candidate;

        bool Exists(string s) =>
            Directory.Exists(Path.Combine(robotDir, s))
            || Directory.Exists(Path.Combine(robotDir, s + ArchiveNames.IncompleteSuffix))
            || Directory.Exists(Path.Combine(robotDir, s + ArchiveNames.InProgressSuffix));
    }

    private static long Total(List<BackupFileRecord> files) => files.Sum(f => f.Bytes);

    private static bool IsTransferFailure(Exception ex, CancellationToken cancellationToken) =>
        ex is RobControlException or IOException or System.Net.Sockets.SocketException
            || (ex is OperationCanceledException && !cancellationToken.IsCancellationRequested);

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Left behind; the manifest does not list it, so it is not part of the backup.
        }
    }

    /// <summary>
    /// One FTP session, reopened on demand. Keeps track of which device it is in so a retry after
    /// a dropped connection lands back in the right place.
    /// </summary>
    private sealed class Session(Robot robot, BackupOptions options, FtpTranscript transcript)
    {
        private FtpClient? _client;
        private string? _device;

        public async Task<FtpClient> OpenAsync(string? device, CancellationToken cancellationToken)
        {
            if (_client is null)
            {
                _client = await FtpClient.ConnectAsync(robot.Address, robot.FtpPort, options.Ftp, transcript, cancellationToken)
                    .ConfigureAwait(false);
                await _client.LoginAsync(robot.Ftp, cancellationToken).ConfigureAwait(false);
                _device = null;
            }

            if (device is not null && !string.Equals(device, _device, StringComparison.OrdinalIgnoreCase))
            {
                await _client.ChangeDirectoryAsync(device, cancellationToken).ConfigureAwait(false);
                _device = device;
            }

            return _client;
        }

        public async Task DropAsync()
        {
            if (_client is null)
            {
                return;
            }

            FtpClient client = _client;
            _client = null;
            _device = null;
            await client.QuitAsync().ConfigureAwait(false);
            await client.DisposeAsync().ConfigureAwait(false);
        }
    }
}
