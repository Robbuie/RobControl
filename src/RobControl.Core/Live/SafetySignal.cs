namespace RobControl.Core.Live;

/// <summary>One line of <c>SFTYSIG.DG</c>: <c>External ESTOP   FALSE</c>.</summary>
/// <param name="Name">As the controller prints it.</param>
/// <param name="IsOn">TRUE on the page. For most of these TRUE is the alarm state (an E-stop pressed, a fence open).</param>
public sealed record SafetySignal(string Name, bool IsOn);
