namespace RobControl.Core.History;

/// <summary>
/// What sort of file changed, for "what changed since the last backup". Programs and settings are what
/// a person changed; values and logs change on their own while a robot runs.
/// </summary>
public enum ChangeKind
{
    /// <summary>A TP or KAREL program, its listing, or a KAREL variable file (.TP .LS .PC .VR).</summary>
    Program,

    /// <summary>System variables, I/O configuration, data files - set up by someone (.SV .VA .IO .DT and the rest).</summary>
    Setting,

    /// <summary>Register files (NUMREG, POSREG, STRREG): counters and positions that move every cycle.</summary>
    Values,

    /// <summary>Live snapshots and logs (.DG, ERR*.LS, LOGBOOK) and anything the site's ignore list names.</summary>
    Noise,
}
