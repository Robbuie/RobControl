namespace RobControl.Core.Transports.Http;

/// <summary>
/// The three resources a FANUC web server can lock separately under Setup &gt; Host Comm &gt; HTTP.
/// Each can be unlocked, locked, or password-protected on its own, which is why a controller can
/// happily serve its diagnostic files and refuse KCL.
/// </summary>
public enum HttpResourceKind
{
    /// <summary><c>/MD/...</c> - diagnostic and listing files.</summary>
    DiagnosticFiles,

    /// <summary><c>/KCL/...</c> - KCL commands.</summary>
    Kcl,

    /// <summary><c>/KAREL/...</c> - running KAREL programs already on the controller.</summary>
    Karel,
}
