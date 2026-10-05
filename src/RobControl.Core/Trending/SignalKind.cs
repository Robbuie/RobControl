namespace RobControl.Core.Trending;

/// <summary>Where a signal's value comes from - which decides how it is read.</summary>
public enum SignalKind
{
    /// <summary><c>R[n]</c>. Read from <c>/MD/NUMREG.VA</c> - one fetch gives every register.</summary>
    NumericRegister,

    /// <summary><c>DI[n]</c>, <c>DO[n]</c>, <c>GI[n]</c>... Read from <c>/MD/IOSTATE.DG</c> - one fetch for all I/O.</summary>
    Io,

    /// <summary><c>$...</c> or <c>[PROG]VAR</c>. Read with KCL <c>SHOW VAR</c>, one request each.</summary>
    SystemVariable,
}
