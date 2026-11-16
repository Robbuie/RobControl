using System.Globalization;
using System.IO.Compression;
using System.Text;
using RobControl.Core.Backup;
using RobControl.Core.Controllers;
using RobControl.Core.Events;
using RobControl.Core.History;
using RobControl.Core.Robots;

namespace RobControl.Core.Capture;

/// <summary>
/// One button for a plant visit: every robot of a system in turn - probe, back up, capture - then one
/// report and one zip to take home.
///
/// <para><b>One robot at a time, on purpose.</b> A visit is a sweep of the line, not a race: each robot
/// gets its probe, its FTP session and its capture's GETs one after another, and the next robot starts
/// only when the last is done. Nothing new goes on the wire - it is the same probe, backup (RETR only)
/// and capture (GET and KCL SHOW only) the menus already run, in sequence.</para>
///
/// <para><b>A robot that does not answer is not waited on three times.</b> When the probe says FTP did
/// not answer, the backup is not tried; when nothing answered at all, neither is the capture. The
/// report says which step was left out and why.</para>
/// </summary>
public static class SiteVisit
{
    public const string ReportFileName = "visit-report.md";

    public static async Task<VisitResult> RunAsync(
        IReadOnlyList<Robot> robots,
        string parentFolder,
        SiteVisitOptions options,
        IProgress<string>? progress = null,
        IEventSink? events = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(robots);
        ArgumentException.ThrowIfNullOrWhiteSpace(parentFolder);
        ArgumentNullException.ThrowIfNull(options);

        DateTimeOffset started = DateTimeOffset.UtcNow;
        Directory.CreateDirectory(parentFolder);
        string folder = UniqueFolder(parentFolder, $"visit_{ArchiveNames.RobotFolder(options.Title)}_{ArchiveNames.Stamp(started)}");
        Directory.CreateDirectory(folder);
        events?.Info(EventCategory.App, null, string.Create(CultureInfo.InvariantCulture, $"Site visit started: {robots.Count} robot(s) of {options.Title}."), folder);

        var probe = new CapabilityProbe(events);
        var runner = new BackupRunner(options.Archive, options.Tool, events);
        var done = new List<VisitRobot>();
        int index = 0;
        foreach (Robot robot in robots)
        {
            index++;
            if (cancellationToken.IsCancellationRequested)
            {
                done.Add(new VisitRobot(robot) { Notes = ["not reached: the visit was stopped"] });
                continue;
            }

            string where = string.Create(CultureInfo.InvariantCulture, $"({index} of {robots.Count})");
            if (options.Skip(robot) is { } why)
            {
                done.Add(new VisitRobot(robot) { Notes = [$"skipped: {why}"] });
                progress?.Report($"{robot.Name} {where}: skipped - {why}");
                continue;
            }

            if (done.Count > 0)
            {
                try
                {
                    await Task.Delay(options.Pause, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    done.Add(new VisitRobot(robot) { Notes = ["not reached: the visit was stopped"] });
                    continue;
                }
            }

            options.Activity?.Invoke(robot, "Visit: probing...");
            try
            {
                done.Add(await VisitOneAsync(robot, folder, where, options, probe, runner, progress, events, cancellationToken).ConfigureAwait(false));
            }
            catch (OperationCanceledException)
            {
                done.Add(new VisitRobot(robot) { Notes = ["stopped part way: anything cut short is marked INCOMPLETE"] });
            }
            finally
            {
                options.Activity?.Invoke(robot, null);
            }
        }

        string report = Report(options, started, done);
        await File.WriteAllTextAsync(Path.Combine(folder, ReportFileName), report, CancellationToken.None).ConfigureAwait(false);

        string? zip = null;
        if (options.Zip)
        {
            progress?.Report("Zipping the visit...");
            zip = folder + ".zip";
            ZipFile.CreateFromDirectory(folder, zip, CompressionLevel.Optimal, includeBaseDirectory: true);
        }

        var result = new VisitResult(folder, zip, done, report);
        events?.Info(EventCategory.App, null, "Site visit finished: " + result.Summary, zip ?? folder);
        return result;
    }

    private static async Task<VisitRobot> VisitOneAsync(
        Robot robot, string folder, string where, SiteVisitOptions options, CapabilityProbe probe, BackupRunner runner,
        IProgress<string>? progress, IEventSink? events, CancellationToken token)
    {
        var notes = new List<string>();
        progress?.Report($"{robot.Name} {where}: probing...");
        ProbeReport report = await probe.RunAsync(robot, token).ConfigureAwait(false);
        options.Probed?.Invoke(robot, report);

        bool silent = !report.Steps.Any(s => s.Outcome is ProbeOutcome.Available or ProbeOutcome.Refused);
        BackupResult? backup = null;
        RobotChanges? changes = null;
        if (!options.Backup)
        {
            // Left out by the caller (tests); nothing to say.
        }
        else if (report.Ftp is ProbeOutcome.Unreachable or ProbeOutcome.Refused)
        {
            notes.Add($"backup not tried: FTP {(report.Ftp == ProbeOutcome.Refused ? "refused the login" : "did not answer")} at the probe");
        }
        else
        {
            options.Activity?.Invoke(robot, "Visit: backing up...");
            progress?.Report($"{robot.Name} {where}: backing up...");
            backup = await runner.RunAsync(robot, options.BackupOptionsFor(robot), report.Identity, null, token).ConfigureAwait(false);
            options.BackedUp?.Invoke(robot, backup);
            if (backup.Outcome != BackupOutcome.Complete)
            {
                notes.Add($"backup {backup.Outcome.ToString().ToUpperInvariant()}: {backup.Manifest.Summary}");
            }
            else if (backup.FolderPath is { } path && BackupArchive.TryRead(path) is { } set)
            {
                try
                {
                    changes = ChangeReport.ForRobot(options.Archive, robot, options.IgnoreChanges, set);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or BackupArchiveException)
                {
                    notes.Add($"changes not worked out: {ex.Message}");
                }
            }
        }

        CaptureResult? capture = null;
        if (!options.Capture)
        {
        }
        else if (silent)
        {
            notes.Add("capture not tried: nothing answered at the probe");
        }
        else
        {
            options.Activity?.Invoke(robot, "Visit: capturing...");
            progress?.Report($"{robot.Name} {where}: capturing...");
            var captureOptions = new CaptureOptions
            {
                Tool = options.Tool,
                Zip = false,

                // The backup just taken already has every file FTP would give; no second session for a sample.
                IncludeFtp = backup?.Outcome != BackupOutcome.Complete,
                BackupFolder = backup?.Outcome == BackupOutcome.Complete ? backup.FolderPath : null,
                EventLogCsv = options.EventLog?.Invoke(robot),
            };
            var lines = new Progress<string>(line =>
            {
                if (line.Length > 0)
                {
                    progress?.Report($"{robot.Name} {where}: {line}");
                }
            });
            try
            {
                capture = await ControllerCapture.RunAsync(robot, folder, captureOptions, lines, events, token).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                notes.Add($"capture could not be written: {ex.Message}");
            }
        }

        return new VisitRobot(robot) { Probe = report, Backup = backup, Capture = capture, Changes = changes, Notes = notes };
    }

    /// <summary>The visit as a table and a section per robot with anything worth a look.</summary>
    internal static string Report(SiteVisitOptions options, DateTimeOffset started, IReadOnlyList<VisitRobot> robots)
    {
        var md = new StringBuilder();
        md.AppendLine(CultureInfo.InvariantCulture, $"# Site visit - {options.Title}");
        md.AppendLine();
        md.AppendLine(CultureInfo.InvariantCulture, $"*{started.ToLocalTime():yyyy-MM-dd HH:mm} to {DateTimeOffset.Now:HH:mm} - {options.Tool}. Read-only: probe, FTP RETR, HTTP GET, KCL SHOW. Nothing was written to a robot.*");
        md.AppendLine();
        md.AppendLine("| Robot | Address | Controller | Probe | Backup | Capture | Changed since previous backup |");
        md.AppendLine("|---|---|---|---|---|---|---|");
        foreach (VisitRobot r in robots)
        {
            string controller = r.Probe?.Identity.Describe() ?? "-";
            string probe = r.Probe?.Summary ?? "-";
            string backup = r.Backup is null ? "-" : $"{r.Backup.Outcome}: {r.Backup.Manifest.Files.Count} files";
            string capture = r.Capture?.Summary ?? "-";
            string changed = r.Changes?.Short ?? "-";
            md.AppendLine(CultureInfo.InvariantCulture,
                $"| {Cell(r.Robot.Name)} | {r.Robot.Address} | {Cell(controller)} | {Cell(probe)} | {Cell(backup)} | {Cell(capture)} | {Cell(changed)} |");
        }

        List<VisitRobot> attention = [.. robots.Where(r => r.Notes.Count > 0 || r.Changes?.HasSafetyChanges == true || r.Probe?.Clock is { IsOff: true })];
        if (attention.Count > 0)
        {
            md.AppendLine();
            md.AppendLine("## To look at");
            md.AppendLine();
            foreach (VisitRobot r in attention)
            {
                foreach (string note in r.Notes)
                {
                    md.AppendLine(CultureInfo.InvariantCulture, $"- **{r.Robot.Name}**: {note}");
                }

                if (r.Changes?.HasSafetyChanges == true)
                {
                    md.AppendLine(CultureInfo.InvariantCulture, $"- **{r.Robot.Name}**: DCS (safety) files changed since the previous backup - see the changes below.");
                }

                if (r.Probe?.Clock is { IsOff: true } clock)
                {
                    md.AppendLine(CultureInfo.InvariantCulture, $"- **{r.Robot.Name}**: controller clock {clock.Describe()}");
                }
            }
        }

        List<RobotChanges> changes = [.. robots.Select(r => r.Changes).OfType<RobotChanges>().Where(c => c.HasRealChanges)];
        if (changes.Count > 0)
        {
            md.AppendLine();
            string body = ChangeReport.ToMarkdown(options.Title, changes, started, options.Tool);

            // Under this report's own title, one level down.
            foreach (string line in body.Split('\n').Skip(1))
            {
                md.AppendLine(line.StartsWith('#') ? "#" + line.TrimEnd('\r') : line.TrimEnd('\r'));
            }
        }

        md.AppendLine();
        md.AppendLine("Each robot's capture is in its own folder beside this report, with report.txt (what RobControl made of each page). Read them for site-confidential names before sending the zip on.");
        return md.ToString();
    }

    private static string Cell(string text) => text.Replace('|', '/').Replace('\n', ' ').Trim();

    private static string UniqueFolder(string parent, string name)
    {
        string path = Path.Combine(parent, name);
        for (int i = 2; Directory.Exists(path) || File.Exists(path + ".zip"); i++)
        {
            path = Path.Combine(parent, string.Create(CultureInfo.InvariantCulture, $"{name}_{i}"));
        }

        return path;
    }
}
