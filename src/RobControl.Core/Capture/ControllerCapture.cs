using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using RobControl.Core.Backup;
using RobControl.Core.Controllers;
using RobControl.Core.Events;
using RobControl.Core.History;
using RobControl.Core.Insight;
using RobControl.Core.Kcl;
using RobControl.Core.Live;
using RobControl.Core.Robots;
using RobControl.Core.Transports.Ftp;
using RobControl.Core.Transports.Http;
using RobControl.Core.Trending;

namespace RobControl.Core.Capture;

/// <summary>
/// Phase 0 inside the app: everything one controller says about itself, read-only, into a folder
/// that <c>robotsim</c> can serve and the tests can run against - and a zip of it to send.
///
/// <para><b>What it sends.</b> FTP: USER/PASS, SYST, PWD, CWD md:, NLST, LIST and RETR of a handful of
/// files (the same verbs a backup uses). HTTP: GET of <c>/</c>, of the index pages it links to, and of
/// every diagnostic file those pages list under <c>/MD/</c> - capped at
/// <see cref="CaptureOptions.PageLimit"/>, one at a time with a pause between them. KCL: two
/// <c>SHOW</c> commands through <see cref="KclClassifier"/>. Nothing that writes; the FTP client and
/// the web client cannot.</para>
///
/// <para><b>What it is for.</b> Every format RobControl parses is an assumption until a controller
/// shows it. A capture records the controller's exact bytes and, beside them, what each parser made of
/// them, so "zero I/O points recognised" arrives with the page that explains why. The same folder
/// with the newest backup and the event log added is the bundle for FANUC support.</para>
/// </summary>
public static class ControllerCapture
{
    /// <summary>Always asked for, whether or not an index links them: the pages RobControl reads.</summary>
    public static IReadOnlyList<string> KnownPages { get; } =
    [
        "SUMMARY.DG", "VERSION.DG", "ERRACT.LS", "ERRALL.LS", "IOSTATE.DG", "NUMREG.VA", "POSREG.VA",
        "PRGSTATE.DG", "CURPOS.DG", LiveReader.SafetyFile, "TASKLIST.DG", "ETHERNET.DG", "IOCONFIG.DG",
    ];

    /// <summary>The KCL reads a capture makes. Both are SHOW commands; the classifier is still asked.</summary>
    public static IReadOnlyList<string> KclCommands { get; } = ["SHOW VAR $VERSION", "SHOW CLOCK"];

    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true };

    public static async Task<CaptureResult> RunAsync(
        Robot robot,
        string parentFolder,
        CaptureOptions? options = null,
        IProgress<string>? progress = null,
        IEventSink? events = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(robot);
        ArgumentException.ThrowIfNullOrWhiteSpace(parentFolder);
        options ??= new CaptureOptions();

        DateTimeOffset started = DateTimeOffset.UtcNow;
        string folder = UniqueFolder(parentFolder, $"{ArchiveNames.RobotFolder(robot.Name)}_{ArchiveNames.Stamp(started)}");
        string md = Path.Combine(folder, "MD");
        string raw = Path.Combine(folder, "_raw");
        Directory.CreateDirectory(md);
        Directory.CreateDirectory(raw);

        var report = new StringBuilder();
        void Say(string line)
        {
            report.AppendLine(line);
            progress?.Report(line);
        }

        Say($"RobControl capture of {robot.Describe()} at {started.ToLocalTime():yyyy-MM-dd HH:mm:ss zzz}");
        Say($"Taken by {options.Tool}. Read-only: FTP RETR, HTTP GET, KCL SHOW.");
        Say(string.Empty);

        ControllerIdentity identity = ControllerIdentity.Unknown;
        var ftp = new FtpPart();
        if (options.IncludeFtp)
        {
            Say("== FTP ==");
            ftp = await CaptureFtpAsync(robot, md, raw, Say, cancellationToken).ConfigureAwait(false);
            identity = identity.Merge(ControllerIdentityParser.Parse(ftp.Banner));
            Say(string.Empty);
        }

        Say("== Web server ==");
        var pages = new List<CapturePage>();
        var kcl = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var texts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        bool diagLocked = false;
        bool kclLocked = false;
        int? clockOffset = null;
        bool clockLocal = false;
        using (var web = new ControllerWebClient(robot.Address, robot.HttpPort))
        {
            bool stop = false;

            async Task<HttpExchange?> FetchAsync(string path)
            {
                if (stop)
                {
                    return null;
                }

                if (pages.Count > 0)
                {
                    await Task.Delay(options.Pause, cancellationToken).ConfigureAwait(false);
                }

                try
                {
                    HttpExchange exchange = await web.GetAsync(path, HttpResourceKind.DiagnosticFiles, cancellationToken).ConfigureAwait(false);
                    pages.Add(new CapturePage(path, exchange.StatusCode, exchange.Body.Length, exchange.Via, exchange.Elapsed, null));
                    await SaveExchangeAsync(raw, path, exchange, cancellationToken).ConfigureAwait(false);
                    if (clockOffset is null && exchange.ServerDate is { } date)
                    {
                        ClockReading clock = ClockReading.Compare(date, exchange.ReceivedUtc);
                        clockOffset = (int)Math.Round(clock.Skew.TotalSeconds);
                        clockLocal = clock.AsLocalTime;
                        Say($"Clock: {clock.Describe()}");
                    }

                    Say(pages[^1].Describe());
                    return exchange;
                }
                catch (HttpResourceLockedException ex)
                {
                    diagLocked = true;
                    stop = true;
                    pages.Add(new CapturePage(path, ex.StatusCode, 0, null, TimeSpan.Zero, $"locked ({ex.StatusCode})"));
                    Say($"GET {path}: locked ({ex.StatusCode}) - Setup > Host Comm > HTTP on the pendant. No more pages asked for.");
                    return null;
                }
                catch (ControllerHttpException ex)
                {
                    pages.Add(new CapturePage(path, ex.StatusCode, 0, null, TimeSpan.Zero, ex.Message));
                    Say($"GET {path}: {ex.Message}");
                    if (ex.StatusCode is null)
                    {
                        // Nothing answered: every further page would wait out the same timeout.
                        stop = true;
                        if (ex.Remediation is not null)
                        {
                            Say($"  -> {ex.Remediation}");
                        }

                        Say("  No more pages asked for.");
                    }

                    return null;
                }
            }

            // The home page and the index pages it links: they say which files this controller has.
            var queue = new List<string>();
            var indexes = new List<string> { "INDEX_ER.HTM" };
            HttpExchange? home = await FetchAsync("/").ConfigureAwait(false);
            if (home is { IsSuccess: true })
            {
                // Beside profile.json as home.htm: what robotsim serves at "/" when this folder replays.
                await File.WriteAllBytesAsync(Path.Combine(folder, "home.htm"), home.Body, cancellationToken).ConfigureAwait(false);
                queue.AddRange(CaptureIndex.Files(home.Text, out IReadOnlyList<string> linked));
                indexes.AddRange(linked.Where(l => !indexes.Contains(l)));
            }

            foreach (string index in indexes)
            {
                HttpExchange? page = await FetchAsync("/MD/" + index).ConfigureAwait(false);
                if (page is { IsSuccess: true })
                {
                    queue.AddRange(CaptureIndex.Files(page.Text, out _).Where(f => !queue.Contains(f)));
                }
            }

            List<string> wanted = [.. KnownPages, .. queue.Where(f => !KnownPages.Contains(f, StringComparer.OrdinalIgnoreCase))];
            if (wanted.Count > options.PageLimit)
            {
                Say($"The index lists {wanted.Count} files; only the first {options.PageLimit} are asked for.");
                wanted = wanted[..options.PageLimit];
            }

            foreach (string name in wanted)
            {
                cancellationToken.ThrowIfCancellationRequested();
                HttpExchange? exchange = await FetchAsync("/MD/" + name).ConfigureAwait(false);
                if (exchange is not { IsSuccess: true })
                {
                    continue;
                }

                texts[name] = DiagnosticPage.Text(exchange.Text);
                identity = identity.Merge(ControllerIdentityParser.Parse(texts[name]));

                // Keep the FTP copy if there is one: that is the file as it is on MD:, without the
                // web server's HTML around it, and what robotsim serves over both.
                string target = Path.Combine(md, name);
                if (!File.Exists(target))
                {
                    await File.WriteAllBytesAsync(target, exchange.Body, cancellationToken).ConfigureAwait(false);
                }
            }

            Say(string.Empty);
            if (options.IncludeKcl && !stop)
            {
                Say("== KCL (SHOW only) ==");
                foreach (string command in KclCommands)
                {
                    await Task.Delay(options.Pause, cancellationToken).ConfigureAwait(false);
                    try
                    {
                        KclResult result = await web.KclAsync(command, cancellationToken).ConfigureAwait(false);
                        await SaveExchangeAsync(raw, "kcl " + command, result.Exchange, cancellationToken).ConfigureAwait(false);
                        kcl[command] = result.Exchange.Text;
                        Say($"{command}: {result.Exchange.StatusCode} ({result.Exchange.ViaText}) -> {Shorten(result.Output)}");
                        if (result.Error is { } error)
                        {
                            Say($"  controller error: {error}");
                        }

                        if (command.StartsWith("SHOW VAR", StringComparison.Ordinal))
                        {
                            Say($"  value parsed: {ShowVarParser.Value(result.Output) ?? "(none - the parser needs a case for this format)"}");
                            identity = identity.Merge(ControllerIdentityParser.Parse(result.Output));
                        }
                    }
                    catch (HttpResourceLockedException ex)
                    {
                        kclLocked = true;
                        Say($"{command}: locked ({ex.StatusCode}). {ex.Remediation}");
                        break;
                    }
                    catch (RobControlException ex)
                    {
                        Say($"{command}: {ex.Message}");
                        break;
                    }
                }

                Say(string.Empty);
            }
        }

        Say("== What RobControl made of it ==");
        foreach (string line in ParserReport(robot, texts, md))
        {
            Say(line);
        }

        Say($"Identity: {identity.Describe()}");
        Say(string.Empty);

        if (options.BackupFolder is { } backup && Directory.Exists(backup))
        {
            string to = Path.Combine(folder, "backup", Path.GetFileName(Path.TrimEndingDirectorySeparator(backup)));
            int copied = CopyFolder(backup, to);
            Say($"Newest backup copied in: {Path.GetFileName(to)} ({copied} files).");
        }

        if (options.EventLogCsv is { } csv)
        {
            await File.WriteAllTextAsync(Path.Combine(folder, "events.csv"), csv, new UTF8Encoding(true), cancellationToken).ConfigureAwait(false);
            Say("Event log for this robot written to events.csv.");
        }

        Say("Read MD/ and _raw/ for anything site-confidential (program names, comments) before sending this to anyone.");

        await WriteProfileAsync(folder, ftp, diagLocked, kclLocked, clockOffset, clockLocal, kcl, cancellationToken).ConfigureAwait(false);
        await File.WriteAllTextAsync(Path.Combine(folder, "report.txt"), report.ToString(), cancellationToken).ConfigureAwait(false);
        var summary = new
        {
            tool = options.Tool,
            robot = robot.Name,
            address = robot.Address.ToString(),
            startedUtc = started,
            finishedUtc = DateTimeOffset.UtcNow,
            identity = identity.Describe(),
            ftp = ftp.Summary,
            pages = pages.Select(p => new { path = p.Path, status = p.StatusCode, bytes = p.Bytes, via = p.Via?.ToString(), ms = (int)p.Elapsed.TotalMilliseconds, error = p.Error }),
        };
        await File.WriteAllTextAsync(Path.Combine(folder, "capture.json"), JsonSerializer.Serialize(summary, Json), cancellationToken).ConfigureAwait(false);

        string? zip = null;
        if (options.Zip)
        {
            zip = folder + ".zip";
            if (File.Exists(zip))
            {
                File.Delete(zip);
            }

            ZipFile.CreateFromDirectory(folder, zip, CompressionLevel.Optimal, includeBaseDirectory: true);
        }

        var captured = new CaptureResult(folder, zip, identity, pages, ftp.Summary, report.ToString());
        string detail = string.Join('\n', pages.Select(p => p.Describe())) + "\nFTP: " + ftp.Summary;
        if (pages.Any(p => p.IsOk) || ftp.LoggedIn)
        {
            events?.Info(EventCategory.Probe, robot, "Capture: " + captured.Summary, detail);
        }
        else
        {
            events?.Warn(EventCategory.Probe, robot, "Capture: " + captured.Summary, detail);
        }

        return captured;
    }

    /// <summary>The FTP half: what it found, kept for the profile.</summary>
    private sealed class FtpPart
    {
        public string Banner { get; set; } = string.Empty;

        public string System { get; set; } = string.Empty;

        public bool Prefixed { get; set; }

        public bool LoggedIn { get; set; }

        public string Summary { get; set; } = "not asked";
    }

    private static async Task<FtpPart> CaptureFtpAsync(Robot robot, string md, string raw, Action<string> say, CancellationToken cancellationToken)
    {
        var part = new FtpPart();
        var transcript = new FtpTranscript();
        try
        {
            FtpClient client;
            try
            {
                client = await FtpClient.ConnectAsync(robot.Address, robot.FtpPort, transcript: transcript, cancellationToken: cancellationToken).ConfigureAwait(false);
            }
            catch (FtpConnectException ex)
            {
                throw await FtpReachability.ExplainAsync(robot, ex, transcript, cancellationToken: cancellationToken).ConfigureAwait(false);
            }

            await using (client.ConfigureAwait(false))
            {
                part.Banner = client.Banner.Text;
                say($"Banner: {part.Banner}");
                await client.LoginAsync(robot.Ftp, cancellationToken).ConfigureAwait(false);
                part.LoggedIn = true;
                say($"Logged in as {robot.Ftp.User}.");
                part.System = (await client.SystemAsync(cancellationToken).ConfigureAwait(false)).Text;
                say($"SYST: {part.System}");
                say($"PWD: {await client.PrintWorkingDirectoryAsync(cancellationToken).ConfigureAwait(false)}");
                await client.ChangeDirectoryAsync("md:", cancellationToken).ConfigureAwait(false);

                IReadOnlyList<string> names = await client.ListNamesAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
                part.Prefixed = names.Any(n => n.StartsWith("md:", StringComparison.OrdinalIgnoreCase));
                say($"NLST md: -> {names.Count} names{(part.Prefixed ? " (prefixed with md:)" : string.Empty)}");
                await File.WriteAllLinesAsync(Path.Combine(raw, "nlst-md.txt"), names, cancellationToken).ConfigureAwait(false);
                string list = await client.ListAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
                await File.WriteAllTextAsync(Path.Combine(raw, "list-md.txt"), list, Encoding.Latin1, cancellationToken).ConfigureAwait(false);

                // A representative handful, not the whole robot: the formats are the question, and a
                // backup is what copies everything.
                string[] plain = [.. names.Select(n => ArchiveNames.StripDevice(n, "md:"))];
                var pick = new List<string>();
                void Pick(Func<string, bool> match)
                {
                    if (plain.FirstOrDefault(match) is { } found && !pick.Contains(found))
                    {
                        pick.Add(found);
                    }
                }

                foreach (string exact in new[] { "SUMMARY.DG", "ERRALL.LS", "ERRACT.LS", "NUMREG.VA", "SYSHOST.VA", "SYSVARS.VA" })
                {
                    Pick(n => n.Equals(exact, StringComparison.OrdinalIgnoreCase));
                }

                Pick(n => n.EndsWith(".LS", StringComparison.OrdinalIgnoreCase) && !AlarmLogParser.IsAlarmLog(n));
                Pick(n => n.EndsWith(".TP", StringComparison.OrdinalIgnoreCase));

                foreach (string name in pick)
                {
                    if (!ArchiveNames.IsSafeFileName(name, out string? problem))
                    {
                        say($"  skipped {name}: {problem}");
                        continue;
                    }

                    var file = File.Create(Path.Combine(md, name));
                    await using (file.ConfigureAwait(false))
                    {
                        long bytes = await client.RetrieveAsync(name, file, cancellationToken).ConfigureAwait(false);
                        say(string.Create(CultureInfo.InvariantCulture, $"  RETR {name}: {bytes} bytes"));
                    }
                }

                await client.QuitAsync(cancellationToken).ConfigureAwait(false);
                part.Summary = string.Create(CultureInfo.InvariantCulture, $"logged in, {names.Count} names on MD:, {pick.Count} copied");
            }
        }
        catch (RobControlException ex)
        {
            part.Summary = ex.Message;
            say($"FTP stopped: {ex.Message}");
            if (ex.Remediation is not null)
            {
                say($"  -> {ex.Remediation}");
            }
        }
        catch (Exception ex) when (ex is IOException or System.Net.Sockets.SocketException)
        {
            part.Summary = ex.Message;
            say($"FTP stopped: {ex.Message}");
        }

        await File.WriteAllTextAsync(Path.Combine(raw, BackupRunner.TranscriptFileName), transcript.ToString(), cancellationToken).ConfigureAwait(false);
        return part;
    }

    /// <summary>What each parser recognised in what came back. A zero is the interesting line.</summary>
    internal static IEnumerable<string> ParserReport(Robot robot, IReadOnlyDictionary<string, string> texts, string md)
    {
        string? Text(string name)
        {
            if (texts.TryGetValue(name, out string? text))
            {
                return text;
            }

            string file = Path.Combine(md, name);
            return File.Exists(file) ? DiagnosticPage.Text(Encoding.Latin1.GetString(File.ReadAllBytes(file))) : null;
        }

        string Count(string name, Func<string, string> describe) =>
            Text(name) is { } t ? $"{name}: {describe(t)}" : $"{name}: not read";

        yield return Count(RobotSampler.IoFile, t =>
        {
            IReadOnlyList<IoPoint> points = IoStateParser.Points(t);
            string types = string.Join(", ", points.GroupBy(p => p.Type).Select(g => $"{g.Count()} {g.Key}"));
            return $"{points.Count} I/O points ({(types.Length == 0 ? "none" : types)}), {IoStateParser.Parse(t).Count} trendable";
        });
        yield return Count(RobotSampler.RegisterFile, t => $"{RegisterFileParser.Parse(t).Count} registers");
        yield return Count("PRGSTATE.DG", t =>
        {
            ProgramStateReport report = ProgramStateParser.Parse(t);
            return $"{report.Tasks.Count} tasks, {report.Programs.Count} programs";
        });
        yield return Count("CURPOS.DG", t => $"{PositionParser.Parse(t).Count} position frames");
        yield return Count(LiveReader.SafetyFile, t => $"{SafetySignalParser.Parse(t).Count} stop signals");
        foreach (string log in new[] { "ERRACT.LS", "ERRALL.LS" })
        {
            yield return Count(log, t =>
            {
                IReadOnlyList<AlarmEntry> alarms = AlarmLogParser.Parse(robot.Name, t.Split('\n'));
                return $"{alarms.Count} alarms, {alarms.Count(a => a.When is not null)} with a date";
            });
        }

        foreach (string file in Directory.EnumerateFiles(md, "*.LS").Where(f => !AlarmLogParser.IsAlarmLog(Path.GetFileName(f))).Take(3))
        {
            ProgramListing? listing = ProgramListingParser.Parse(BackupComparer.ReadLines(file));
            yield return listing is null
                ? $"{Path.GetFileName(file)}: not recognised as a TP listing"
                : $"{Path.GetFileName(file)}: program {listing.Name}, {listing.Lines.Count} lines, {listing.References.Count} references";
        }
    }

    private static async Task WriteProfileAsync(
        string folder, FtpPart ftp, bool diagLocked, bool kclLocked, int? clockOffset, bool clockLocal, Dictionary<string, string> kcl, CancellationToken cancellationToken)
    {
        // The same shape as RobotSim's SimProfile (the engine cannot reference the simulator); a test
        // loads one with SimProfile.Load to keep the two in step.
        var profile = new
        {
            banner = ftp.Banner.Length == 0 ? "FTP server ready." : ftp.Banner,
            system = ftp.System.Length == 0 ? "UNIX Type: L8" : ftp.System,
            homeDevice = "md:",
            nlstWithDevicePrefix = ftp.Prefixed,
            diagnosticFilesLocked = diagLocked,
            kclLocked,
            clockOffsetSeconds = clockOffset,
            clockIsLocalTime = clockLocal,
        };
        await File.WriteAllTextAsync(Path.Combine(folder, "profile.json"), JsonSerializer.Serialize(profile, Json), cancellationToken).ConfigureAwait(false);
        await File.WriteAllTextAsync(Path.Combine(folder, "kcl.json"), JsonSerializer.Serialize(kcl, Json), cancellationToken).ConfigureAwait(false);
    }

    private static async Task SaveExchangeAsync(string raw, string path, HttpExchange exchange, CancellationToken cancellationToken)
    {
        string stem = path == "/" ? "home" : path.TrimStart('/');
        string safe = string.Concat(stem.Select(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.' ? c : '_'));
        await File.WriteAllBytesAsync(Path.Combine(raw, safe + ".body"), exchange.Body, cancellationToken).ConfigureAwait(false);
        await File.WriteAllTextAsync(Path.Combine(raw, safe + ".meta.txt"), string.Create(CultureInfo.InvariantCulture,
            $"GET {exchange.Path}\nstatus {exchange.StatusCode}\ncontent-type {exchange.ContentType}\nelapsed {exchange.Elapsed.TotalMilliseconds:0} ms\nanswered {exchange.ViaText}\ndate {exchange.ServerDate?.ToString("O", CultureInfo.InvariantCulture) ?? "(none)"}\nreceived {exchange.ReceivedUtc:O}\n"),
            cancellationToken).ConfigureAwait(false);
    }

    private static int CopyFolder(string from, string to)
    {
        int count = 0;
        foreach (string file in Directory.EnumerateFiles(from, "*", SearchOption.AllDirectories))
        {
            string target = Path.Combine(to, Path.GetRelativePath(from, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, overwrite: false);
            count++;
        }

        return count;
    }

    private static string UniqueFolder(string parent, string name)
    {
        string path = Path.Combine(parent, name);
        for (int i = 2; Directory.Exists(path) || File.Exists(path + ".zip"); i++)
        {
            path = Path.Combine(parent, string.Create(CultureInfo.InvariantCulture, $"{name}_{i}"));
        }

        return path;
    }

    private static string Shorten(string text)
    {
        string one = text.Replace('\n', ' ').Trim();
        return one.Length <= 100 ? one : one[..100] + "...";
    }
}
