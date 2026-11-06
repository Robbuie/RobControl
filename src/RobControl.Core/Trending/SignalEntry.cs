namespace RobControl.Core.Trending;

/// <summary>
/// One signal the browser offers: what it is, what the controller calls it, and the value it had
/// when it was listed.
/// </summary>
/// <param name="Address">Normalised, and already accepted by <see cref="SignalAddress.TryParse"/> - so anything listed can be added.</param>
/// <param name="Comment">The register or I/O comment from the controller, or a variable's type and access. Null when there is none.</param>
/// <param name="Value">As the controller printed it: <c>1287</c>, <c>ON</c>, <c>1.450000e+01</c>.</param>
public sealed record SignalEntry(SignalAddress Address, string? Comment, string? Value);
