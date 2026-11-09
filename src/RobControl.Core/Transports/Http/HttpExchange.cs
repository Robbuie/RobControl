namespace RobControl.Core.Transports.Http;

/// <summary>
/// One request and what came back, kept whole - status, content type and the exact bytes - so the
/// probe tool can save it as a fixture and a parser can be tested against it later.
/// </summary>
public sealed record HttpExchange(string Path, int StatusCode, string? ContentType, byte[] Body, TimeSpan Elapsed)
{
    public bool IsSuccess => StatusCode is >= 200 and < 300;

    /// <summary>Which way of asking got this answer - see <see cref="HttpAttempt"/>.</summary>
    public HttpAttempt Via { get; init; } = HttpAttempt.FirstTry;

    /// <summary>"first try", "after a retry", "only as plain HTTP/1.0" - for messages and the event log.</summary>
    public string ViaText => Describe(Via);

    public static string Describe(HttpAttempt via) => via switch
    {
        HttpAttempt.AfterRetry => "after a retry",
        HttpAttempt.PlainHttp10 => "only as plain HTTP/1.0",
        _ => "first try",
    };

    /// <summary>The server's <c>Date</c> header, when it sent one - its clock, to the second.</summary>
    public DateTimeOffset? ServerDate { get; init; }

    /// <summary>This PC's clock when the reply arrived, to compare <see cref="ServerDate"/> with.</summary>
    public DateTimeOffset ReceivedUtc { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// The body as text. Latin-1, for the same reason as the FTP client: controller files are bytes
    /// first, and a decoder that can fail is a parser that can fail on a robot nobody has seen.
    /// </summary>
    public string Text => System.Text.Encoding.Latin1.GetString(Body);
}
