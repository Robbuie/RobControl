namespace RobControl.Core.Transports.Http;

/// <summary>
/// Which of <see cref="ControllerWebClient"/>'s three ways of asking got the answer. Kept on every
/// exchange and written into the event log, because which one a controller needs is still unknown:
/// an R-30iB V8.30 answered a browser and reset RobControl 0.7.0's connections (6 Nov 2026), and
/// 0.7.1's fallbacks were written without being able to see which of them cures it.
/// </summary>
public enum HttpAttempt
{
    /// <summary>The ordinary request answered.</summary>
    FirstTry,

    /// <summary>The first was dropped; the same request again after a pause answered.</summary>
    AfterRetry,

    /// <summary>Both were dropped; only the hand-written HTTP/1.0 request answered.</summary>
    PlainHttp10,
}
