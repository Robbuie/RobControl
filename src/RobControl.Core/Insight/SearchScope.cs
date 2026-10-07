namespace RobControl.Core.Insight;

/// <summary>Which files of a backup a search reads.</summary>
public enum SearchScope
{
    /// <summary>TP program listings (<c>.LS</c>).</summary>
    Programs,

    /// <summary>Program listings and variable listings (<c>.LS</c>, <c>.VA</c>).</summary>
    ProgramsAndVariables,

    /// <summary>Every file that reads as text, including diagnostic snapshots.</summary>
    AllText,
}
