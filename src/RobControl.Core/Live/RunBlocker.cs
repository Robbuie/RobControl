namespace RobControl.Core.Live;

/// <summary>How much a blocker stops the robot.</summary>
public enum BlockerLevel
{
    /// <summary>Stops it: an E-stop, a fault, *IMSTP.</summary>
    Stop,

    /// <summary>Holds it or keeps it from starting: *HOLD, the TP enabled, a paused task.</summary>
    Hold,

    /// <summary>Worth knowing, not by itself a reason: nothing running, UOP looks unused.</summary>
    Note,
}

/// <summary>One reason a robot is not running, in words, with where to look.</summary>
/// <param name="What">"UI[2] *HOLD is OFF".</param>
/// <param name="Why">"The hold input is low - the PLC or the cell is holding the robot."</param>
/// <param name="Where">"The PLC's hold output, or the cell's hold circuit."</param>
public sealed record RunBlocker(BlockerLevel Level, string What, string Why, string Where)
{
    public string LevelText => Level switch
    {
        BlockerLevel.Stop => "Stops it",
        BlockerLevel.Hold => "Holds it",
        _ => "Note",
    };
}
