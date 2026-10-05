using RobControl.Core.Transports.Http;

namespace RobControl.Core.Kcl;

/// <summary>One KCL command's output, plus the raw exchange it came from.</summary>
public sealed record KclResult(string Command, string Output, HttpExchange Exchange)
{
    /// <summary>
    /// The first line that looks like a controller error code (<c>VARS-014 ...</c>, <c>KCL-012 ...</c>),
    /// or null. KCL reports its own errors inside a successful HTTP response, so a 200 is not success.
    /// </summary>
    public string? Error => KclResponse.FindError(Output);
}
