namespace RobControl.Core.Net;

/// <summary>
/// An address that is not one host - broadcast, multicast, 0.0.0.0 - refused before anything was
/// sent. See <see cref="UnicastTarget"/>.
/// </summary>
public sealed class RobotAddressException : RobControlException
{
    public RobotAddressException(string message) : base(message) { }
}
