namespace RobControl.Core.Insight;

/// <summary>What a teach pendant line refers to.</summary>
public enum ReferenceKind
{
    /// <summary>Another program, through CALL or RUN.</summary>
    Program,

    /// <summary>R[n].</summary>
    Register,

    /// <summary>PR[n] - including PR[n,i] elements.</summary>
    PositionRegister,

    /// <summary>SR[n].</summary>
    StringRegister,

    /// <summary>DI, DO, RI, RO, GI, GO, AI, AO, UI, UO, SI, SO, WI, WO, F and M ports.</summary>
    Io,
}
