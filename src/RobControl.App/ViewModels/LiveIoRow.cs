namespace RobControl.App.ViewModels;

/// <summary>One I/O point (or stop signal) on the Live tab.</summary>
/// <param name="Category">"Digital", "UOP", "Safe I/O (DCS / CIP Safety)", "Stop signals"...</param>
/// <param name="Direction">"Input", "Output" or "Internal".</param>
/// <param name="Name"><c>DI[1]</c>, or a stop signal's name.</param>
/// <param name="State">ON/OFF, TRUE/FALSE, or a group/analog value.</param>
/// <param name="Comment">The controller's I/O comment.</param>
/// <param name="IsOn">Lit - drawn in the accent colour.</param>
/// <param name="Listed">How the page itself wrote the name (DIN[1]), for the tooltip.</param>
public sealed record LiveIoRow(string Category, string Direction, string Name, string State, string Comment, bool IsOn, string Listed);
