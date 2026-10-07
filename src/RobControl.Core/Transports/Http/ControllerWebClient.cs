using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using RobControl.Core.Kcl;
using RobControl.Core.Net;

namespace RobControl.Core.Transports.Http;

/// <summary>
/// The controller's own web server: diagnostic files under <c>/MD/</c> and KCL commands under
/// <c>/KCL/</c>.
///
/// <para><b>Only GET, and KCL only through the classifier.</b> <see cref="KclAsync"/> asks
/// <see cref="KclClassifier"/> before anything is sent and refuses everything that is not a read.
/// The write gate, when it exists, will be a separate method with its own confirmation - not a
/// flag on this one.</para>
///
/// <para>One request at a time per robot (<see cref="_gate"/>). Controller web servers are small, and
/// a trend poll overlapping a backup should queue rather than pile up.</para>
/// </summary>
public sealed class ControllerWebClient : IDisposable
{
    private readonly HttpClient _http;
    private readonly bool _ownsHttp;
    private readonly Uri _base;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public ControllerWebClient(IPAddress address, int port = 80, HttpClient? http = null, TimeSpan? timeout = null)
    {
        UnicastTarget.Ensure(address);
        _base = new UriBuilder(Uri.UriSchemeHttp, address.ToString(), port).Uri;
        _ownsHttp = http is null;
        _http = http ?? new HttpClient(new SocketsHttpHandler
        {
            // A plant laptop with a system proxy configured would otherwise try to send robot
            // traffic to it, and fail in a way that names the proxy rather than the robot.
            UseProxy = false,
            ConnectTimeout = TimeSpan.FromSeconds(5),
            PooledConnectionLifetime = TimeSpan.FromMinutes(1),
        });

        if (_ownsHttp)
        {
            _http.Timeout = timeout ?? TimeSpan.FromSeconds(30);
            _http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("RobControl", "0"));
        }
    }

    /// <summary>
    /// GET <c>/MD/{fileName}</c>, for example <c>SUMMARY.DG</c> or <c>ERRALL.LS</c>.
    /// </summary>
    public Task<HttpExchange> GetDiagnosticFileAsync(string fileName, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        if (fileName.AsSpan().IndexOfAny("/\\?#%") >= 0 || fileName.Contains("..", StringComparison.Ordinal))
        {
            throw new ArgumentException($"'{fileName}' is not a plain file name.", nameof(fileName));
        }

        return GetAsync("/MD/" + fileName, HttpResourceKind.DiagnosticFiles, cancellationToken);
    }

    /// <summary>
    /// Runs one KCL command - <b>reads only</b>. The reply's text is in <see cref="KclResult.Output"/>,
    /// already pulled out of the HTML page the controller wraps it in.
    /// </summary>
    /// <exception cref="KclRefusedException">The command is not a read. Nothing was sent.</exception>
    public async Task<KclResult> KclAsync(string command, CancellationToken cancellationToken = default)
    {
        KclClassification classification = KclClassifier.Classify(command);
        if (classification.Class != KclCommandClass.Read)
        {
            throw new KclRefusedException(classification);
        }

        string path = "/KCL/" + KclPath(classification.Normalised);
        HttpExchange exchange = await GetAsync(path, HttpResourceKind.Kcl, cancellationToken).ConfigureAwait(false);
        return new KclResult(classification.Normalised, KclResponse.ExtractText(exchange.Text), exchange);
    }

    /// <summary>A raw GET of any path - the probe tool's escape hatch, still GET only.</summary>
    public async Task<HttpExchange> GetAsync(string path, HttpResourceKind resource, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var uri = new Uri(_base, path);

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var watch = Stopwatch.StartNew();
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            using HttpResponseMessage response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
            byte[] body = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
            var exchange = new HttpExchange(path, (int)response.StatusCode, response.Content.Headers.ContentType?.ToString(), body, watch.Elapsed)
            {
                ServerDate = response.Headers.Date,
                ReceivedUtc = DateTimeOffset.UtcNow,
            };

            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            {
                throw new HttpResourceLockedException(
                    resource,
                    exchange.StatusCode,
                    $"The controller refused {path} ({exchange.StatusCode} {response.ReasonPhrase}): "
                        + $"the {HttpResourceLockedException.Name(resource)} resource is locked.");
            }

            return exchange;
        }
        catch (HttpRequestException ex) when (ex.InnerException is SocketException or IOException)
        {
            throw new ControllerHttpException($"Could not reach the web server at {_base}: {ex.InnerException!.Message}", null, ex)
            {
                Remediation = "Check the robot is powered and on this network, and that its web server is enabled "
                    + "(Setup > Host Comm > Servers).",
            };
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new ControllerHttpException($"The web server at {_base} did not answer {path} in time.", null, ex)
            {
                Remediation = "The controller may be busy generating a large file. Try again; if it keeps "
                    + "happening, raise the timeout.",
            };
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Escapes only what a URL path cannot carry. Spaces become %20; <c>$</c>, <c>[</c>, <c>]</c> and
    /// <c>.</c> stay literal, because that is how KCL URLs are written in the field and a controller
    /// web server is not guaranteed to decode more than it has to. Unconfirmed - Phase 0.
    /// </summary>
    internal static string KclPath(string command)
    {
        var builder = new System.Text.StringBuilder(command.Length + 16);
        foreach (char c in command)
        {
            builder.Append(c switch
            {
                ' ' => "%20",
                '%' => "%25",
                '#' => "%23",
                '?' => "%3F",
                '"' => "%22",
                '\'' => "%27",
                _ when c > 126 => Uri.EscapeDataString(c.ToString()),
                _ => c.ToString(),
            });
        }

        return builder.ToString();
    }

    public void Dispose()
    {
        if (_ownsHttp)
        {
            _http.Dispose();
        }

        _gate.Dispose();
    }
}
