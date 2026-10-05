using System.Globalization;
using System.Net;
using RobControl.RobotSim;

// robotsim <profile folder> [--ftp-port 2121] [--http-port 8080] [--address 127.0.0.1]
//
// Starts a fake controller for working on the app with no robot. Add a robot in RobControl at the
// address and ports this prints.

if (args.Length == 0 || args[0] is "-h" or "--help")
{
    Console.WriteLine("robotsim <profile folder> [--ftp-port 2121] [--http-port 8080] [--address 127.0.0.1]");
    Console.WriteLine("A profile folder is what RobControl's probe tool captures, or tests/Fixtures/<name>.");
    return 1;
}

string folder = args[0];
int ftpPort = 2121;
int httpPort = 8080;
IPAddress address = IPAddress.Loopback;

for (int i = 1; i < args.Length - 1; i++)
{
    switch (args[i])
    {
        case "--ftp-port": ftpPort = int.Parse(args[++i], CultureInfo.InvariantCulture); break;
        case "--http-port": httpPort = int.Parse(args[++i], CultureInfo.InvariantCulture); break;
        case "--address": address = IPAddress.Parse(args[++i]); break;
    }
}

SimProfile profile = SimProfile.Load(folder);
await using var sim = new SimController(profile, address);
sim.Start(ftpPort, httpPort);

Console.WriteLine($"Simulated controller from {profile.Folder}");
Console.WriteLine($"  FTP  {address}:{sim.FtpPort}");
Console.WriteLine($"  HTTP {address}:{sim.HttpPort}");
Console.WriteLine("Ctrl+C to stop. Anything that is not a read is refused and printed.");

using var stop = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; stop.Cancel(); };

while (!stop.IsCancellationRequested)
{
    while (sim.RefusedCommands.TryDequeue(out string? refused))
    {
        Console.WriteLine("REFUSED: " + refused);
    }

    try
    {
        await Task.Delay(500, stop.Token);
    }
    catch (OperationCanceledException)
    {
    }
}

return 0;
