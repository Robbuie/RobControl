namespace RobControl.Core.Kcl;

/// <summary>What a KCL command would do to a controller. See <see cref="KclClassifier"/>.</summary>
public enum KclCommandClass
{
    /// <summary>Only reads: SHOW, DIRECTORY, TYPE, HELP. Allowed.</summary>
    Read,

    /// <summary>
    /// Changes a value but cannot start anything: SET VAR. Refused until the write gate exists, and
    /// then only through it - armed per robot, confirmed, read back and logged.
    /// </summary>
    Write,

    /// <summary>
    /// Everything else, and everything not recognised. Never sent, whatever is armed. Includes every
    /// command that can start, stop or move anything, delete or load files, or touch safety.
    /// </summary>
    Never,
}
