using System.Diagnostics;
using System.Globalization;
using RobControl.Core.Events;
using RobControl.Core.Kcl;
using RobControl.Core.Robots;
using RobControl.Core.Transports.Ftp;
using RobControl.Core.Transports.Http;

namespace RobControl.Core.Controllers;

/// <summary>
/// Finds out what a robot is and which ways in are open, without changing anything.
///
/// <para>Three steps, each independent - a locked web server does not stop the FTP check, and an
/// FTP login refusal does not stop the web check - because the point is a complete picture of one
/// controller, not the first failure:</para>
/// <list type="number">
///   <item><b>FTP</b>: connect, log in, open <c>md:</c>, list it. This is what backups need.</item>
///   <item><b>Diagnostic files</b>: GET <c>/MD/SUMMARY.DG</c>, read the identity out of it.</item>
///   <item><b>KCL</b>: <c>SHOW VAR $VERSION</c>. Only reads - it goes through the classifier like
///         every other KCL command.</item>
/// </list>
/// <para>Every step writes an event row: these are packets on a production network.</para>
/// </summary>
public sealed class CapabilityProbe
{
    public const string FtpStep = "FTP";
    public const string HttpStep = "Diagnostic files";
    public const string KclStep = "KCL";

    /// <summary>Asked for by name. Field reports put the identity in it; Phase 0 confirms.</summary>
    public const string SummaryFile = "SUMMARY.DG";

    private readonly IEventSink _events;
    private readonly FtpClientOptions _ftpOptions;
    private readonly Func<Robot, ControllerWebClient> _webFactory;

    public CapabilityProbe(IEventSink? events = null, FtpClientOptions? ftpOptions = null, Func<Robot, ControllerWebClient>? webFactory = null)
    {
        _events = events ?? NullEventSink.Instance;
        _ftpOptions = ftpOptions ?? FtpClientOptions.Default;
        _webFactory = webFactory ?? (r => new ControllerWebClient(r.Address, r.HttpPort));
    }

    public async Task<ProbeReport> RunAsync(Robot robot, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(robot);

        var steps = new List<ProbeStep>();
        ControllerIdentity identity = ControllerIdentity.Unknown;
        ClockReading? clock = null;

        (ProbeStep ftp, ControllerIdentity fromBanner) = await ProbeFtpAsync(robot, cancellationToken).ConfigureAwait(false);
        steps.Add(ftp);

        using (ControllerWebClient web = _webFactory(robot))
        {
            (ProbeStep http, ControllerIdentity fromSummary, clock) = await ProbeHttpAsync(web, cancellationToken).ConfigureAwait(false);
            steps.Add(http);

            // KCL is only worth asking once the web server has shown it is there at all. A locked
            // diagnostic resource does not mean KCL is locked, so Refused still goes on.
            (ProbeStep kcl, ControllerIdentity fromKcl) = http.Outcome == ProbeOutcome.Unreachable
                ? (new ProbeStep(KclStep, ProbeOutcome.NotChecked, "Skipped: the web server did not answer.", null, TimeSpan.Zero), ControllerIdentity.Unknown)
                : await ProbeKclAsync(web, cancellationToken).ConfigureAwait(false);
            steps.Add(kcl);

            // The summary file is the richest source, then KCL, then the banner.
            identity = fromSummary.Merge(fromKcl).Merge(fromBanner);
        }

        var report = new ProbeReport(robot, DateTimeOffset.UtcNow, identity, steps) { Clock = clock };
        string detail = string.Join('\n', steps.Select(s => $"{s.Name}: {s.Outcome} - {s.Message}"));
        if (clock is { IsOff: true })
        {
            _events.Warn(EventCategory.Probe, robot, $"Probed: {report.Summary}. {identity.Describe()}. {clock.Describe()}", detail);
        }
        else
        {
            _events.Info(EventCategory.Probe, robot, $"Probed: {report.Summary}. {identity.Describe()}.", detail);
        }
        return report;
    }

    private async Task<(ProbeStep, ControllerIdentity)> ProbeFtpAsync(Robot robot, CancellationToken cancellationToken)
    {
        var watch = Stopwatch.StartNew();
        try
        {
            await using FtpClient ftp = await FtpClient
                .ConnectAsync(robot.Address, robot.FtpPort, _ftpOptions, cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            ControllerIdentity fromBanner = ControllerIdentityParser.Parse(ftp.Banner.Text);
            await ftp.LoginAsync(robot.Ftp, cancellationToken).ConfigureAwait(false);
            await ftp.ChangeDirectoryAsync("md:", cancellationToken).ConfigureAwait(false);
            IReadOnlyList<string> names = await ftp.ListNamesAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
            await ftp.QuitAsync(cancellationToken).ConfigureAwait(false);

            return (new ProbeStep(FtpStep, ProbeOutcome.Available,
                string.Create(CultureInfo.InvariantCulture, $"Logged in as {robot.Ftp.User}; {names.Count} files on MD:."),
                null, watch.Elapsed), fromBanner);
        }
        catch (FtpConnectException ex)
        {
            return (new ProbeStep(FtpStep, ProbeOutcome.Unreachable, ex.Message, ex.Remediation, watch.Elapsed), ControllerIdentity.Unknown);
        }
        catch (FtpException ex) when (ex.Reply?.Code == 530)
        {
            return (new ProbeStep(FtpStep, ProbeOutcome.Refused, ex.Message, ex.Remediation, watch.Elapsed), ControllerIdentity.Unknown);
        }
        catch (Exception ex) when (ex is RobControlException or IOException or System.Net.Sockets.SocketException
            or OperationCanceledException && !cancellationToken.IsCancellationRequested)
        {
            return (new ProbeStep(FtpStep, ProbeOutcome.Failed, ex.Message, (ex as RobControlException)?.Remediation, watch.Elapsed), ControllerIdentity.Unknown);
        }
    }

    private static async Task<(ProbeStep, ControllerIdentity, ClockReading?)> ProbeHttpAsync(ControllerWebClient web, CancellationToken cancellationToken)
    {
        var watch = Stopwatch.StartNew();
        try
        {
            HttpExchange summary = await web.GetDiagnosticFileAsync(SummaryFile, cancellationToken).ConfigureAwait(false);
            if (!summary.IsSuccess)
            {
                return (new ProbeStep(HttpStep, ProbeOutcome.Failed,
                    string.Create(CultureInfo.InvariantCulture, $"The web server answered {SummaryFile} with status {summary.StatusCode}."),
                    "Report this with the controller's software version - the file may have another name on it.",
                    watch.Elapsed), ControllerIdentity.Unknown, null);
            }

            // The web server's Date header is the one clock reading that costs no extra request.
            ClockReading? clock = summary.ServerDate is { } date ? ClockReading.Compare(date, summary.ReceivedUtc) : null;
            string message = string.Create(CultureInfo.InvariantCulture, $"{SummaryFile}: {summary.Body.Length} bytes.")
                + (clock is null ? string.Empty : " " + clock.Describe());
            string? remediation = clock is { IsOff: true }
                ? "Alarm times and backup history from this robot will not line up with the others. Set the controller's clock on the pendant (System > Clock), and check the backup battery if it has drifted after a power-off."
                : null;

            return (new ProbeStep(HttpStep, ProbeOutcome.Available, message, remediation, watch.Elapsed),
                ControllerIdentityParser.Parse(summary.Text), clock);
        }
        catch (HttpResourceLockedException ex)
        {
            return (new ProbeStep(HttpStep, ProbeOutcome.Refused, ex.Message, ex.Remediation, watch.Elapsed), ControllerIdentity.Unknown, null);
        }
        catch (ControllerHttpException ex) when (ex.StatusCode is null)
        {
            return (new ProbeStep(HttpStep, ProbeOutcome.Unreachable, ex.Message, ex.Remediation, watch.Elapsed), ControllerIdentity.Unknown, null);
        }
        catch (Exception ex) when (ex is RobControlException or HttpRequestException or IOException)
        {
            return (new ProbeStep(HttpStep, ProbeOutcome.Failed, ex.Message, (ex as RobControlException)?.Remediation, watch.Elapsed), ControllerIdentity.Unknown, null);
        }
    }

    private static async Task<(ProbeStep, ControllerIdentity)> ProbeKclAsync(ControllerWebClient web, CancellationToken cancellationToken)
    {
        var watch = Stopwatch.StartNew();
        try
        {
            KclResult result = await web.KclAsync("SHOW VAR $VERSION", cancellationToken).ConfigureAwait(false);
            if (!result.Exchange.IsSuccess)
            {
                return (new ProbeStep(KclStep, ProbeOutcome.Failed,
                    string.Create(CultureInfo.InvariantCulture, $"The web server answered KCL with status {result.Exchange.StatusCode}."),
                    null, watch.Elapsed), ControllerIdentity.Unknown);
            }

            if (result.Error is { } error)
            {
                return (new ProbeStep(KclStep, ProbeOutcome.Failed, $"KCL answered with an error: {error}", null, watch.Elapsed),
                    ControllerIdentity.Unknown);
            }

            string? value = ShowVarParser.Value(result.Output);
            return (new ProbeStep(KclStep, ProbeOutcome.Available, $"$VERSION = {value ?? "(no value in reply)"}", null, watch.Elapsed),
                ControllerIdentityParser.Parse(value ?? result.Output));
        }
        catch (HttpResourceLockedException ex)
        {
            return (new ProbeStep(KclStep, ProbeOutcome.Refused, ex.Message, ex.Remediation, watch.Elapsed), ControllerIdentity.Unknown);
        }
        catch (ControllerHttpException ex) when (ex.StatusCode is null)
        {
            return (new ProbeStep(KclStep, ProbeOutcome.Unreachable, ex.Message, ex.Remediation, watch.Elapsed), ControllerIdentity.Unknown);
        }
        catch (Exception ex) when (ex is RobControlException or HttpRequestException or IOException)
        {
            return (new ProbeStep(KclStep, ProbeOutcome.Failed, ex.Message, (ex as RobControlException)?.Remediation, watch.Elapsed), ControllerIdentity.Unknown);
        }
    }
}
