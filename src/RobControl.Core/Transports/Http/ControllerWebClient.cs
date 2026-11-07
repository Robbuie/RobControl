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
    private readonly IPAddress _address;

    /// <summary>How long to wait before asking again after the controller dropped a connection.</summary>
    internal static TimeSpan RetryPause { get; set; } = TimeSpan.FromMilliseconds(750);

    public ControllerWebClient(IPAddress address, int port = 80, HttpClient? http = null, TimeSpan? timeout = null)
    {
        UnicastTarget.Ensure(address);
        _address = address;
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
            HttpExchange exchange;
            try
            {
                exchange = await SendWithRetryAsync(uri, path, cancellationToken).ConfigureAwait(false);
            }
            catch (HttpRequestException ex) when (ex.InnerException is SocketException or IOException)
            {
                throw Unreachable(path, ex);
            }
            catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
            {
                throw new ControllerHttpException($"The web server at {_base} did not answer {path} in time.", null, ex)
                {
                    Remediation = "The controller may be busy generating a large file (IOSTATE.DG can take several seconds). "
                        + "Try again; if it keeps happening, raise the timeout.",
                };
            }

            if (exchange.StatusCode is 401 or 403)
            {
                throw new HttpResourceLockedException(
                    resource,
                    exchange.StatusCode,
                    $"The controller refused {path} ({exchange.StatusCode}): "
                        + $"the {HttpResourceLockedException.Name(resource)} resource is locked.");
            }

            return exchange;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// One GET, tried up to three ways when the controller drops the connection: the request as
    /// normal, the same again after a pause, then by hand as HTTP/1.0 (<see cref="RawHttpGet"/>).
    /// A connection that is refused or never answers is not retried - nothing is listening, and
    /// asking again only makes the person wait longer.
    /// </summary>
    private async Task<HttpExchange> SendWithRetryAsync(Uri uri, string path, CancellationToken cancellationToken)
    {
        try
        {
            return await SendOnceAsync(uri, path, cancellationToken).ConfigureAwait(false);
        }
        catch (HttpRequestException first) when (_ownsHttp && IsDropped(first))
        {
            TraceDrop(path, "first try", first);
        }

        await Task.Delay(RetryPause, cancellationToken).ConfigureAwait(false);
        try
        {
            return await SendOnceAsync(uri, path, cancellationToken).ConfigureAwait(false);
        }
        catch (HttpRequestException second) when (IsDropped(second))
        {
            TraceDrop(path, "second try", second);
            try
            {
                return await RawHttpGet.GetAsync(_address, _base.Port, path, _http.Timeout, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception raw) when (raw is IOException or SocketException
                || (raw is OperationCanceledException && !cancellationToken.IsCancellationRequested))
            {
                throw Dropped(path, second, raw);
            }
        }
    }

    private async Task<HttpExchange> SendOnceAsync(Uri uri, string path, CancellationToken cancellationToken)
    {
        var watch = Stopwatch.StartNew();
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);

        // One request per connection, and say what is acceptable. A controller's web server is small
        // and old: a kept-alive connection it has already given up on is the usual way to be reset,
        // and a request with no Accept header is not what a browser - the client it was tested
        // with - ever sends.
        request.Headers.ConnectionClose = true;
        request.Headers.Accept.ParseAdd("text/html, text/plain, */*");

        using HttpResponseMessage response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        byte[] body = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
        return new HttpExchange(path, (int)response.StatusCode, response.Content.Headers.ContentType?.ToString(), body, watch.Elapsed)
        {
            ServerDate = response.Headers.Date,
            ReceivedUtc = DateTimeOffset.UtcNow,
        };
    }

    /// <summary>
    /// The server took the connection and then dropped it - a reset, or a reply cut short or
    /// malformed. Distinct from refused or timed out, which mean nothing answered at all.
    /// </summary>
    internal static bool IsDropped(HttpRequestException ex)
    {
        if (ex.HttpRequestError is HttpRequestError.ResponseEnded or HttpRequestError.InvalidResponse)
        {
            return true;
        }

        for (Exception? inner = ex.InnerException; inner is not null; inner = inner.InnerException)
        {
            if (inner is SocketException { SocketErrorCode: SocketError.ConnectionReset or SocketError.ConnectionAborted or SocketError.Shutdown })
            {
                return true;
            }
        }

        return false;
    }

    private ControllerHttpException Unreachable(string path, HttpRequestException ex)
    {
        Exception cause = ex.InnerException ?? ex;
        if (IsDropped(ex))
        {
            return Dropped(path, ex, null);
        }

        return new ControllerHttpException($"Could not reach the web server at {_base}: {cause.Message}", null, ex)
        {
            Remediation = "Check the robot is powered and on this network, and that its web server is enabled "
                + "(Setup > Host Comm > Servers).",
        };
    }

    private ControllerHttpException Dropped(string path, HttpRequestException ex, Exception? raw)
    {
        string cause = (ex.InnerException ?? ex).Message;
        string rawNote = raw is null ? string.Empty : $" A plain HTTP/1.0 request was tried as well and failed too ({raw.Message}).";
        return new ControllerHttpException(
            $"The web server at {_base} accepted the connection for {path} and then dropped it: {cause}.{rawNote}", null, ex)
        {
            Remediation = $"Something answered on port {_base.Port}, so the robot is on the network. Open {_base}MD/SUMMARY.DG "
                + "in a browser on this PC: if that works and RobControl still cannot, a firewall or endpoint-security "
                + "product on this PC may be blocking RobControl.exe - allow it for the robot network. If the browser "
                + "fails too, the controller's web server is refusing this PC: check Setup > Host Comm > HTTP on the pendant.",
        };
    }

    private void TraceDrop(string path, string attempt, HttpRequestException ex) =>
        Trace.WriteLine($"[http] {_base}{path.TrimStart('/')}: connection dropped on the {attempt} ({ex.HttpRequestError}): {(ex.InnerException ?? ex).Message}");

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
