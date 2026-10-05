namespace RobControl.Core.Controllers;

/// <summary>How one step of a capability probe went.</summary>
public enum ProbeOutcome
{
    /// <summary>Not attempted - an earlier step showed there was no point.</summary>
    NotChecked,

    /// <summary>Worked.</summary>
    Available,

    /// <summary>The controller answered and refused: locked, or a login it would not accept.</summary>
    Refused,

    /// <summary>Nothing answered.</summary>
    Unreachable,

    /// <summary>It answered with something unexpected. The message says what.</summary>
    Failed,
}
