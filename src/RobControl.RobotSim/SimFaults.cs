using System.Collections.Concurrent;

namespace RobControl.RobotSim;

/// <summary>
/// Misbehaviour to inject, for the tests that matter most: what the backup does when the controller
/// is not cooperating.
/// </summary>
public sealed class SimFaults
{
    /// <summary>RETR of these names answers 550, every time.</summary>
    public ConcurrentDictionary<string, bool> RefuseFiles { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// RETR of these names drops the control connection part way through - once each, so a retry on
    /// a fresh session succeeds. Models a controller that hiccups while generating a listing.
    /// </summary>
    public ConcurrentDictionary<string, bool> DropOnceOnFile { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Extra names NLST returns that do not exist - and hostile names, for the path-safety tests.</summary>
    public ConcurrentBag<string> ExtraListedNames { get; } = [];
}
