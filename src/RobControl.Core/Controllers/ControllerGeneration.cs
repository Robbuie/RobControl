namespace RobControl.Core.Controllers;

/// <summary>The controller cabinet family. Mates and Compacts are folded into their parent family.</summary>
public enum ControllerGeneration
{
    Unknown,

    /// <summary>R-30iA (and Mate). Software V7.x.</summary>
    R30iA,

    /// <summary>R-30iB (and Mate). Software V8.x.</summary>
    R30iB,

    /// <summary>R-30iB Plus (and Mate Plus, Compact Plus). Software V9.x.</summary>
    R30iBPlus,

    /// <summary>R-50iA and later. Software V10.x and up. Recognised so it is not called an iB.</summary>
    R50iA,
}
