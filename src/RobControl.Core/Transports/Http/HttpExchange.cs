namespace RobControl.Core.Transports.Http;

/// <summary>
/// One request and what came back, kept whole - status, content type and the exact bytes - so the
/// probe tool can save it as a fixture and a parser can be tested against it later.
/// </summary>
public sealed record HttpExchange(string Path, int StatusCode, string? ContentType, byte[] Body, TimeSpan Elapsed)
{
    public bool IsSuccess => StatusCode is >= 200 and < 300;

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
