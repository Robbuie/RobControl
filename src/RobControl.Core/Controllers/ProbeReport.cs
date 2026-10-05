using RobControl.Core.Robots;

namespace RobControl.Core.Controllers;

/// <summary>
/// What a robot can do for RobControl, as found by <see cref="CapabilityProbe"/>. Stored per robot,
/// so every feature can say why it is unavailable instead of just greying out.
/// </summary>
public sealed record ProbeReport(Robot Robot, DateTimeOffset Utc, ControllerIdentity Identity, IReadOnlyList<ProbeStep> Steps)
{
    public ProbeOutcome Ftp => Outcome(CapabilityProbe.FtpStep);

    public ProbeOutcome DiagnosticFiles => Outcome(CapabilityProbe.HttpStep);

    public ProbeOutcome Kcl => Outcome(CapabilityProbe.KclStep);

    /// <summary>Backups need FTP and nothing else.</summary>
    public bool CanBackUp => Ftp == ProbeOutcome.Available;

    /// <summary>One line for a status column.</summary>
    public string Summary =>
        Steps.All(s => s.Outcome == ProbeOutcome.Unreachable)
            ? "Not answering"
            : string.Join(" · ", Steps.Select(s => $"{s.Name} {Word(s.Outcome)}"));

    private ProbeOutcome Outcome(string name) =>
        Steps.FirstOrDefault(s => s.Name == name)?.Outcome ?? ProbeOutcome.NotChecked;

    private static string Word(ProbeOutcome outcome) => outcome switch
    {
        ProbeOutcome.Available => "ok",
        ProbeOutcome.Refused => "locked",
        ProbeOutcome.Unreachable => "no answer",
        ProbeOutcome.Failed => "failed",
        _ => "not checked",
    };
}
