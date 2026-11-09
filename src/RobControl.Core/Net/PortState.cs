namespace RobControl.Core.Net;

/// <summary>What a TCP connect to one port of one host did.</summary>
public enum PortState
{
    /// <summary>The connection opened: something is listening.</summary>
    Open,

    /// <summary>The host answered with a reset: it is there, nothing listens on that port.</summary>
    Refused,

    /// <summary>Nothing came back in time - no host, a host that drops, or a filter on the way.</summary>
    NoAnswer,
}
