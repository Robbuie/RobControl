using System.Globalization;
using System.Net;
using System.Text;
using RobControl.Core;
using RobControl.Core.Controllers;
using RobControl.Core.Insight;
using RobControl.Core.Kcl;
using RobControl.Core.Transports.Ftp;
using RobControl.Core.Transports.Http;
using RobControl.Core.Trending;
using RobControl.RobotSim;

// robcontrol-probe <address> [--user anonymous] [--password ""] [--ftp-port 21] [--http-port 80] [--out folder]
//
// Read-only. Sends: FTP USER/PASS/SYST/PWD/TYPE/CWD/PASV/NLST/LIST/RETR/QUIT, HTTP GET, and KCL
// SHOW commands that go through the same classifier as the app. Nothing that writes.

if (args.Length == 0 || args[0] is "-h" or "--help")
{
    Console.WriteLine("robcontrol-probe <address> [--user anonymous] [--password \"\"] [--ftp-port 21] [--http-port 80] [--out folder]");
    Console.WriteLine();
    Console.WriteLine("Records what one controller says, read-only, into a profile folder for tests/Fixtures.");
    return 1;
}

IPAddress address = IPAddress.Parse(args[0]);
string user = "anonymous";
string password = string.Empty;
int ftpPort = 21;
int httpPort = 80;
string? outDir = null;

for (int i = 1; i < args.Length - 1; i++)
{
    switch (args[i])
    {
        case "--user": user = args[++i]; break;
        case "--password": password = args[++i]; break;
        case "--ftp-port": ftpPort = int.Parse(args[++i], CultureInfo.InvariantCulture); break;
        case "--http-port": httpPort = int.Parse(args[++i], CultureInfo.InvariantCulture); break;
        case "--out": outDir = args[++i]; break;
    }
}

outDir ??= $"capture-{address}-{DateTime.Now:yyyyMMdd-HHmmss}";
Directory.CreateDirectory(outDir);
Directory.CreateDirectory(Path.Combine(outDir, "MD"));
Directory.CreateDirectory(Path.Combine(outDir, "_raw"));

var report = new StringBuilder();
void Say(string line)
{
    Console.WriteLine(line);
    report.AppendLine(line);
}

Say($"RobControl probe of {address} at {DateTimeOffset.Now:O}");
Say($"Output: {Path.GetFullPath(outDir)}");
Say(string.Empty);

var identity = ControllerIdentity.Unknown;
string banner = string.Empty;
string system = string.Empty;
bool prefixed = false;
var kclPages = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
bool diagLocked = false;
bool kclLocked = false;

// ---------------------------------------------------------------- FTP
Say("== FTP ==");
var transcript = new FtpTranscript();
try
{
    await using FtpClient ftp = await FtpClient.ConnectAsync(address, ftpPort, transcript: transcript);
    banner = ftp.Banner.Text;
    Say($"Banner: {banner}");
    await ftp.LoginAsync(new FtpCredentials(user, password));
    Say($"Logged in as {user}.");
    system = (await ftp.SystemAsync()).Text;
    Say($"SYST: {system}");
    Say($"PWD: {await ftp.PrintWorkingDirectoryAsync()}");
    await ftp.ChangeDirectoryAsync("md:");

    IReadOnlyList<string> names = await ftp.ListNamesAsync();
    prefixed = names.Any(n => n.StartsWith("md:", StringComparison.OrdinalIgnoreCase));
    Say($"NLST md: -> {names.Count} names{(prefixed ? " (prefixed with md:)" : string.Empty)}");
    await File.WriteAllLinesAsync(Path.Combine(outDir, "_raw", "nlst-md.txt"), names);

    string list = await ftp.ListAsync();
    await File.WriteAllTextAsync(Path.Combine(outDir, "_raw", "list-md.txt"), list, Encoding.Latin1);
    Say($"LIST md: -> {list.Split('\n').Length} lines");

    // A small, representative set: the summary, the first program listing, the first variable
    // listing, and one binary. Enough to pin down formats without copying a whole robot into a repo.
    string[] plain = [.. names.Select(n => n.StartsWith("md:", StringComparison.OrdinalIgnoreCase) ? n[3..] : n)];
    var pick = new List<string>();
    void Pick(Func<string, bool> match)
    {
        string? found = plain.FirstOrDefault(match);
        if (found is not null && !pick.Contains(found))
        {
            pick.Add(found);
        }
    }

    Pick(n => n.Equals("SUMMARY.DG", StringComparison.OrdinalIgnoreCase));
    Pick(n => n.Equals("ERRALL.LS", StringComparison.OrdinalIgnoreCase));
    Pick(n => n.EndsWith(".LS", StringComparison.OrdinalIgnoreCase) && !n.Equals("ERRALL.LS", StringComparison.OrdinalIgnoreCase));
    Pick(n => n.EndsWith(".VA", StringComparison.OrdinalIgnoreCase));
    Pick(n => n.EndsWith(".TP", StringComparison.OrdinalIgnoreCase));

    foreach (string name in pick)
    {
        if (!RobControl.Core.Backup.ArchiveNames.IsSafeFileName(name, out string? problem))
        {
            Say($"  skipped {name}: {problem}");
            continue;
        }

        await using var file = File.Create(Path.Combine(outDir, "MD", name));
        long bytes = await ftp.RetrieveAsync(name, file);
        Say($"  RETR {name}: {bytes} bytes");
    }

    // What the backup-reading views would make of these files - their formats are assumptions until
    // a real controller shows them. Zero here means the format is not what was assumed.
    foreach (string name in pick)
    {
        string saved = Path.Combine(outDir, "MD", name);
        if (!File.Exists(saved))
        {
            continue;
        }

        IReadOnlyList<string> lines = RobControl.Core.History.BackupComparer.ReadLines(saved);
        if (AlarmLogParser.IsAlarmLog(name))
        {
            IReadOnlyList<AlarmEntry> alarms = AlarmLogParser.Parse("probe", lines);
            Say($"  alarm parser: {alarms.Count} alarms recognised in {name}, {alarms.Count(e => e.When is not null)} with a date");
        }
        else if (name.EndsWith(".LS", StringComparison.OrdinalIgnoreCase))
        {
            ProgramListing? listing = ProgramListingParser.Parse(lines);
            Say(listing is null
                ? $"  program parser: {name} not recognised as a TP listing"
                : $"  program parser: {listing.Name}, {listing.Lines.Count} lines, {listing.References.Count} references, calls {string.Join(" ", listing.Calls)}");
        }
        else if (name.EndsWith(".VA", StringComparison.OrdinalIgnoreCase))
        {
            int headers = lines.Count(l => l.StartsWith('$') || l.StartsWith('['));
            Say($"  settings watch: {headers} variable headers in {name}");
        }
    }

    await ftp.QuitAsync();
    identity = identity.Merge(ControllerIdentityParser.Parse(banner));
}
catch (RobControlException ex)
{
    Say($"FTP stopped: {ex.Message}");
    if (ex.Remediation is not null)
    {
        Say($"  -> {ex.Remediation}");
    }
}
catch (Exception ex) when (ex is IOException or System.Net.Sockets.SocketException)
{
    Say($"FTP stopped: {ex.Message}");
}

await File.WriteAllTextAsync(Path.Combine(outDir, "_raw", "ftp-transcript.txt"), transcript.ToString());
Say(string.Empty);

// ---------------------------------------------------------------- HTTP
Say("== Web server ==");
using (var web = new ControllerWebClient(address, httpPort))
{
    foreach (string name in new[] { "SUMMARY.DG", "ERRALL.LS", "IOSTATE.DG", "NUMREG.VA", "PRGSTATE.DG", "CURPOS.DG", "VERSION.DG" })
    {
        try
        {
            HttpExchange exchange = await web.GetDiagnosticFileAsync(name);
            await SaveExchange(outDir, "http-md-" + name, exchange);
            Say($"GET /MD/{name}: {exchange.StatusCode}, {exchange.Body.Length} bytes, {exchange.ContentType}");
            if (exchange.IsSuccess)
            {
                string target = Path.Combine(outDir, "MD", name);
                if (!File.Exists(target))
                {
                    await File.WriteAllBytesAsync(target, exchange.Body);
                }

                identity = identity.Merge(ControllerIdentityParser.Parse(exchange.Text));

                // What trending would make of it - the formats it depends on are exactly these two.
                if (name == RobotSampler.RegisterFile)
                {
                    Say($"  trending parser: {RegisterFileParser.Parse(exchange.Text).Count} registers recognised");
                }
                else if (name == RobotSampler.IoFile)
                {
                    Say($"  trending parser: {IoStateParser.Parse(exchange.Text).Count} I/O points recognised");
                }
            }
        }
        catch (HttpResourceLockedException ex)
        {
            diagLocked = true;
            Say($"GET /MD/{name}: locked ({ex.StatusCode})");
            break;
        }
        catch (RobControlException ex)
        {
            Say($"GET /MD/{name}: {ex.Message}");
            break;
        }
    }

    Say(string.Empty);
    Say("== KCL (SHOW only) ==");
    foreach (string command in new[] { "SHOW VAR $VERSION", "SHOW CLOCK" })
    {
        try
        {
            KclResult result = await web.KclAsync(command);
            await SaveExchange(outDir, "kcl-" + command.Replace(' ', '_').Replace('$', '_'), result.Exchange);
            kclPages[command] = result.Exchange.Text;
            Say($"{command}: {result.Exchange.StatusCode} -> {Shorten(result.Output)}");
            if (result.Error is { } error)
            {
                Say($"  controller error: {error}");
            }

            if (command == "SHOW VAR $VERSION")
            {
                Say($"  parsed value: {ShowVarParser.Value(result.Output) ?? "(none - parser needs a case for this format)"}");
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
}

Say(string.Empty);
Say("== What RobControl made of it ==");
Say($"Identity: {identity.Describe()}");
Say($"  generation {identity.Generation}{(identity.GenerationInferred ? " (inferred from version)" : string.Empty)}, "
    + $"version {identity.SoftwareVersion ?? "?"}, application {identity.Application ?? "?"}, model {identity.RobotModel ?? "?"}, F-number {identity.FNumber ?? "?"}");
Say(string.Empty);
Say("Check MD/ and kcl.json for anything site-confidential before committing this folder.");

var profile = new SimProfile
{
    Banner = banner,
    System = system.Length == 0 ? "UNIX Type: L8" : system,
    NlstWithDevicePrefix = prefixed,
    DiagnosticFilesLocked = diagLocked,
    KclLocked = kclLocked,
    Kcl = kclPages,
};
profile.Save(outDir);
await File.WriteAllTextAsync(Path.Combine(outDir, "_raw", "probe-report.txt"), report.ToString());
return 0;

static async Task SaveExchange(string outDir, string stem, HttpExchange exchange)
{
    string safe = string.Concat(stem.Select(c => char.IsLetterOrDigit(c) || c is '-' or '_' or '.' ? c : '_'));
    await File.WriteAllBytesAsync(Path.Combine(outDir, "_raw", safe + ".body"), exchange.Body);
    await File.WriteAllTextAsync(Path.Combine(outDir, "_raw", safe + ".meta.txt"),
        $"GET {exchange.Path}\nstatus {exchange.StatusCode}\ncontent-type {exchange.ContentType}\nelapsed {exchange.Elapsed.TotalMilliseconds:0} ms\n");
}

static string Shorten(string text)
{
    string one = text.Replace('\n', ' ').Trim();
    return one.Length <= 100 ? one : one[..100] + "...";
}
