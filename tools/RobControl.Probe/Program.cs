using System.Globalization;
using System.Net;
using RobControl.Core.Capture;
using RobControl.Core.Robots;
using RobControl.Core.Transports.Ftp;

// robcontrol-probe <address> [--user anonymous] [--password ""] [--ftp-port 21] [--http-port 80] [--out folder]
//
// Read-only. The same capture as Robots > Capture for support in the app (ControllerCapture): FTP
// USER/PASS/SYST/PWD/CWD/PASV/NLST/LIST/RETR/QUIT, HTTP GET of the controller's own index and the
// diagnostic files it lists, and two KCL SHOW commands through the classifier. Nothing that writes.

if (args.Length == 0 || args[0] is "-h" or "--help")
{
    Console.WriteLine("robcontrol-probe <address> [--user anonymous] [--password \"\"] [--ftp-port 21] [--http-port 80] [--out folder]");
    Console.WriteLine();
    Console.WriteLine("Records what one controller says, read-only, into a profile folder for tests/Fixtures.");
    return 1;
}

IPAddress address = IPAddress.Parse(args[0]);
string user = "anonymous";
string password = string.Empty;
int ftpPort = 21;
int httpPort = 80;
string outDir = Environment.CurrentDirectory;

for (int i = 1; i < args.Length - 1; i++)
{
    switch (args[i])
    {
        case "--user": user = args[++i]; break;
        case "--password": password = args[++i]; break;
        case "--ftp-port": ftpPort = int.Parse(args[++i], CultureInfo.InvariantCulture); break;
        case "--http-port": httpPort = int.Parse(args[++i], CultureInfo.InvariantCulture); break;
        case "--out": outDir = args[++i]; break;
    }
}

var robot = new Robot("capture-" + address.ToString().Replace('.', '-'), address)
{
    Ftp = new FtpCredentials(user, password),
    FtpPort = ftpPort,
    HttpPort = httpPort,
};

var progress = new Progress<string>(Console.WriteLine);
CaptureResult result = await ControllerCapture.RunAsync(robot, outDir, new CaptureOptions { Tool = "robcontrol-probe" }, progress);

// Progress<T> posts to the thread pool; let the last lines out before the summary.
await Task.Delay(200);
Console.WriteLine();
Console.WriteLine(result.Summary);
Console.WriteLine($"Folder: {result.Folder}");
if (result.ZipPath is not null)
{
    Console.WriteLine($"Zip:    {result.ZipPath}");
}

return 0;
