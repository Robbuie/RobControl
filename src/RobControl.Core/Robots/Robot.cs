using System.Net;
using RobControl.Core.Net;

namespace RobControl.Core.Robots;

/// <summary>
/// One controller, as the person described it. Identity (generation, version, application) is not
/// here: that is what the controller says about itself, and it lives in
/// <see cref="Controllers.ControllerIdentity"/>, re-read on every probe and every backup.
/// </summary>
public sealed record Robot
{
    public Robot(string name, IPAddress address)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        UnicastTarget.Ensure(address);

        Name = name.Trim();
        Address = address;
    }

    /// <summary>Row id in the fleet database. Zero until saved.</summary>
    public long Id { get; init; }

    /// <summary>What the plant calls it: "R2-14", "Spot cell 3 robot 2". Also the archive folder name.</summary>
    public string Name { get; }

    public IPAddress Address { get; }

    /// <summary>Line, cell, station - free text, for sorting and for the person.</summary>
    public string? Line { get; init; }

    public string? Notes { get; init; }

    /// <summary>
    /// FTP credentials. FANUC controllers accept an anonymous login out of the box unless Host Comm
    /// has been set up with users, so <c>anonymous</c> with an empty password is the default - see
    /// <see cref="Transports.Ftp.FtpCredentials.Default"/>.
    /// </summary>
    public Transports.Ftp.FtpCredentials Ftp { get; init; } = Transports.Ftp.FtpCredentials.Default;

    public int FtpPort { get; init; } = 21;

    public int HttpPort { get; init; } = 80;

    /// <summary>"R2-14 (10.20.1.54)" - how every message names a robot.</summary>
    public string Describe() => $"{Name} ({Address})";

    public override string ToString() => Describe();
}
