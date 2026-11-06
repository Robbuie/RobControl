using RobControl.Core.Events;
using RobControl.Core.Insight;
using RobControl.Core.Robots;
using RobControl.Core.Trending;
using RobControl.Core.Transports.Http;

namespace RobControl.Core.Live;

/// <summary>
/// The Live tab's read: a handful of the controller's own diagnostic pages over HTTP GET, one after
/// another on one client - alarms, I/O, where the arm is, what program is running.
///
/// <para><b>Reads only, and nothing new on the wire.</b> Every request is a GET of <c>/MD/&lt;file&gt;</c>
/// through <see cref="ControllerWebClient.GetDiagnosticFileAsync"/>, the same path trending and the
/// probe use. One event row per read, naming the robot and what came back - not one per page.</para>
///
/// <para><b>The page names are FANUC's as documented, not yet seen on our controllers</b> (CLAUDE.md,
/// Phase 0). A page a controller does not have is shown as "not available", never an error, and
/// every page is kept verbatim so the person can read what it really says.</para>
/// </summary>
public static class LiveReader
{
    /// <summary>Never more often than this per robot, whatever the auto-refresh is set to.</summary>
    public static readonly TimeSpan MinimumInterval = TimeSpan.FromSeconds(5);

    /// <summary>Alarm-history lines kept for the tab. The log can hold hundreds.</summary>
    public const int RecentAlarmCount = 50;

    /// <summary>The pages read, in order: title and file name.</summary>
    public static IReadOnlyList<(string Title, string File)> Pages { get; } =
    [
        ("Active alarms", "ERRACT.LS"),
        ("Alarm history", "ERRALL.LS"),
        ("Program state", "PRGSTATE.DG"),
        ("Current position", "CURPOS.DG"),
        ("I/O", RobotSampler.IoFile),
    ];

    public static async Task<LiveSnapshot> ReadAsync(
        Robot robot, ControllerWebClient web, IEventSink? events = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(robot);
        ArgumentNullException.ThrowIfNull(web);

        var files = new List<LiveFile>();
        foreach ((string title, string file) in Pages)
        {
            cancellationToken.ThrowIfCancellationRequested();
            files.Add(await ReadOneAsync(web, title, file, cancellationToken).ConfigureAwait(false));
        }

        IReadOnlyList<AlarmEntry> active = Alarms(robot, files, "ERRACT.LS", int.MaxValue);
        IReadOnlyList<AlarmEntry> recent = Alarms(robot, files, "ERRALL.LS", RecentAlarmCount);
        LiveFile? io = files.FirstOrDefault(f => f.FileName == RobotSampler.IoFile);
        IReadOnlyDictionary<string, double> points = io is { IsOk: true } ? IoStateParser.Parse(io.Text) : new Dictionary<string, double>();

        var snapshot = new LiveSnapshot(DateTimeOffset.UtcNow, files, active, recent, points);
        string detail = string.Join('\n', files.Select(f => $"GET /MD/{f.FileName}: {f.Status}{(f.Message is null ? string.Empty : " - " + f.Message)}"));
        if (files.All(f => f.Status is LiveFileStatus.Failed or LiveFileStatus.Locked))
        {
            events?.Warn(EventCategory.Http, robot, "Live read: " + snapshot.Summary, detail);
        }
        else
        {
            events?.Info(EventCategory.Http, robot, "Live read: " + snapshot.Summary, detail);
        }

        return snapshot;
    }

    private static async Task<LiveFile> ReadOneAsync(ControllerWebClient web, string title, string file, CancellationToken cancellationToken)
    {
        try
        {
            HttpExchange exchange = await web.GetDiagnosticFileAsync(file, cancellationToken).ConfigureAwait(false);
            if (exchange.StatusCode == 404)
            {
                return new LiveFile(title, file, LiveFileStatus.NotAvailable, string.Empty, "This controller does not serve this page.");
            }

            return exchange.IsSuccess
                ? new LiveFile(title, file, LiveFileStatus.Ok, exchange.Text, null)
                : new LiveFile(title, file, LiveFileStatus.Failed, exchange.Text, $"HTTP {exchange.StatusCode}");
        }
        catch (HttpResourceLockedException ex)
        {
            return new LiveFile(title, file, LiveFileStatus.Locked, string.Empty,
                ex.Message + " Unlock it on the pendant: Setup > Host Comm > HTTP.");
        }
        catch (ControllerHttpException ex)
        {
            return new LiveFile(title, file, LiveFileStatus.Failed, string.Empty, ex.Message);
        }
    }

    private static IReadOnlyList<AlarmEntry> Alarms(Robot robot, List<LiveFile> files, string name, int max)
    {
        LiveFile? file = files.FirstOrDefault(f => f.FileName == name);
        if (file is not { IsOk: true })
        {
            return [];
        }

        string[] lines = file.Text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        return [.. AlarmLogParser.Parse(robot.Name, lines).Take(max)];
    }
}
