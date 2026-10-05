namespace RobControl.Core.Controllers;

/// <summary>One line of a probe report: "FTP - available - 214 files on MD:".</summary>
public sealed record ProbeStep(string Name, ProbeOutcome Outcome, string Message, string? Remediation, TimeSpan Elapsed);
