using RobControl.Core.Transports.Http;

namespace RobControl.Core.Capture;

/// <summary>One GET a capture made and what came back.</summary>
public sealed record CapturePage(string Path, int? StatusCode, int Bytes, HttpAttempt? Via, TimeSpan Elapsed, string? Error)
{
    public bool IsOk => StatusCode is >= 200 and < 300;

    public string Describe() => Error is not null
        ? $"GET {Path}: {Error}"
        : $"GET {Path}: {StatusCode}, {Bytes} bytes, {Elapsed.TotalMilliseconds:0} ms ({HttpExchange.Describe(Via ?? HttpAttempt.FirstTry)})";
}
