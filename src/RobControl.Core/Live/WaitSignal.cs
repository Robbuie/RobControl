namespace RobControl.Core.Live;

/// <summary>One signal named on the line a task is at, with what the line wants and what it is now.</summary>
/// <param name="Point"><c>DI[34]</c>.</param>
/// <param name="Expected">"ON", "OFF" or a number when the line compares it (<c>WAIT DI[34]=ON</c>); null when it only uses it.</param>
/// <param name="Now">The value on the controller's I/O page, or null when the page did not list it.</param>
/// <param name="Satisfied">Whether <paramref name="Now"/> is what the line waits for; null when either side is unknown.</param>
public sealed record WaitSignal(string Point, string Comment, string? Expected, string? Now, bool? Satisfied)
{
    public string Verdict => Satisfied switch
    {
        true => "met",
        false => "NOT met - this is what it is waiting for",
        null when Now is null => "not on the I/O page",
        _ => "used here",
    };
}
