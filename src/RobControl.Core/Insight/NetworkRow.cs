namespace RobControl.Core.Insight;

/// <summary>
/// One robot's network identity as its newest complete backup records it, beside the address in
/// RobControl's robot list.
/// </summary>
/// <param name="ListedAddress">What the robot list says - where RobControl sends packets.</param>
/// <param name="Backup">The backup folder the values came from; null when the robot has no complete backup.</param>
/// <param name="Addresses">Every IPv4 address found under an address-like variable.</param>
/// <param name="Settings">Everything found, for the detail view and the CSV.</param>
public sealed record NetworkRow(
    string Robot,
    string ListedAddress,
    string? Backup,
    string? Hostname,
    IReadOnlyList<string> Addresses,
    string? SubnetMask,
    string? Router,
    string? Mac,
    IReadOnlyList<NetworkSetting> Settings)
{
    public string AddressText => string.Join(", ", Addresses);

    /// <summary>
    /// The backup names addresses and none of them is the one RobControl uses. Worth a look: a
    /// swapped controller, a re-addressed port, or a robot list that is out of date.
    /// </summary>
    public bool AddressMismatch => Addresses.Count > 0 && !Addresses.Contains(ListedAddress, StringComparer.Ordinal);

    public string Note => Backup is null
        ? "No complete backup to read."
        : Settings.Count == 0
            ? "No network variables recognised in the listings - Phase 0 will say where this controller keeps them."
            : AddressMismatch
                ? $"The backup does not mention {ListedAddress}. Swapped controller, re-addressed port, or an old robot list?"
                : string.Empty;
}
