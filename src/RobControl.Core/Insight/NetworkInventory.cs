using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using RobControl.Core.Backup;
using RobControl.Core.History;
using RobControl.Core.Robots;

namespace RobControl.Core.Insight;

/// <summary>
/// Hostname, IP addresses, subnet mask, router and MAC of each robot, read from the variable
/// listings (<c>.VA</c>) in its newest complete backup - nothing is sent to a robot.
///
/// <para><b>Searches rather than parses</b>, like <see cref="SettingsWatch"/>: which system variables
/// hold the Host Comm TCP/IP settings, and in which listing, is for Phase 0 to confirm. A line counts
/// when the variable it belongs to has an address-like name and its value looks like what that name
/// promises - a dotted IPv4 address for an address, six hex pairs for a MAC. So a guess that is wrong
/// finds nothing rather than something misleading, and every value carries the variable and file it
/// came from.</para>
/// </summary>
public static partial class NetworkInventory
{
    public const string HostnameKind = "Hostname";
    public const string AddressKind = "IP address";
    public const string SubnetKind = "Subnet mask";
    public const string RouterKind = "Router";
    public const string MacKind = "MAC address";

    public static IReadOnlyList<NetworkRow> Build(BackupArchive archive, IEnumerable<Robot> robots, CancellationToken cancellation = default)
    {
        ArgumentNullException.ThrowIfNull(archive);
        ArgumentNullException.ThrowIfNull(robots);
        var rows = new List<NetworkRow>();
        foreach (Robot robot in robots)
        {
            cancellation.ThrowIfCancellationRequested();
            BackupSet? set;
            try
            {
                set = archive.LatestComplete(robot);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                set = null;
            }

            IReadOnlyList<NetworkSetting> found = set is null ? [] : Read(robot.Name, set);
            rows.Add(new NetworkRow(
                robot.Name,
                robot.Address.ToString(),
                set?.FolderName,
                First(found, HostnameKind),
                [.. found.Where(f => f.Kind == AddressKind).Select(f => f.Value).Distinct(StringComparer.Ordinal)],
                First(found, SubnetKind),
                First(found, RouterKind),
                First(found, MacKind),
                found));
        }

        return rows;
    }

    /// <summary>Every network value recognised in one backup's variable listings.</summary>
    public static IReadOnlyList<NetworkSetting> Read(string robot, BackupSet set)
    {
        ArgumentNullException.ThrowIfNull(set);
        var found = new List<NetworkSetting>();
        foreach (BackupFileRecord file in set.Manifest.Files.Where(f => Path.GetExtension(f.Name).Equals(".VA", StringComparison.OrdinalIgnoreCase)))
        {
            IReadOnlyList<string> lines;
            try
            {
                lines = BackupComparer.ReadLines(set.PathOf(file));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or BackupArchiveException)
            {
                continue;
            }

            found.AddRange(Scan(robot, file.RelativePath, lines));
        }

        // One value per variable: a listing can show a variable's value twice (header and field line).
        return [.. found.DistinctBy(f => (f.Kind, f.Variable.ToUpperInvariant(), f.Value))];
    }

    internal static IEnumerable<NetworkSetting> Scan(string robot, string file, IReadOnlyList<string> lines)
    {
        string?[] owners = SettingsWatch.Attribute(lines);
        for (int i = 0; i < lines.Count; i++)
        {
            string line = lines[i];
            int equals = line.LastIndexOf('=');
            if (equals < 0)
            {
                continue;
            }

            // The variable named on the line itself if there is one ("  Field: $HOSTENT[1].$H_ADDR ... = '...'"),
            // otherwise the header it sits under.
            Match onLine = VariableOnLine().Match(line[..equals]);
            string? variable = onLine.Success ? onLine.Value.ToUpperInvariant() : owners[i];
            if (variable is null || Kind(variable) is not { } kind)
            {
                continue;
            }

            string value = Unquote(line[(equals + 1)..].Trim());
            if (Normalise(kind, value) is { } clean)
            {
                yield return new NetworkSetting(robot, kind, variable, clean, file);
            }
        }
    }

    /// <summary>What a variable name suggests it holds, or null. Ordered: a MAC is checked before "address".</summary>
    internal static string? Kind(string variable)
    {
        // The last component names the field: $HOSTENT[1].$H_ADDR is an address, $HOSTENT[1].$H_NAME a name.
        string last = variable[(variable.LastIndexOf('$') + 1)..];
        return last switch
        {
            _ when MacName().IsMatch(last) => MacKind,
            _ when SubnetName().IsMatch(last) => SubnetKind,
            _ when RouterName().IsMatch(last) => RouterKind,
            _ when AddressName().IsMatch(last) => AddressKind,
            _ when HostName().IsMatch(last) => HostnameKind,
            _ => null,
        };
    }

    /// <summary>The value in a standard form when it is the kind of thing the name promised; otherwise null.</summary>
    internal static string? Normalise(string kind, string value)
    {
        switch (kind)
        {
            case AddressKind or SubnetKind or RouterKind:
                if (Ipv4().IsMatch(value) && IPAddress.TryParse(value, out IPAddress? ip) && !ip.Equals(IPAddress.Any))
                {
                    return ip.ToString();
                }

                return null;

            case MacKind:
                string hex = new([.. value.Where(Uri.IsHexDigit)]);
                return hex.Length == 12 && MacShape().IsMatch(value)
                    ? string.Join(":", Enumerable.Range(0, 6).Select(i => hex.Substring(i * 2, 2).ToUpperInvariant()))
                    : null;

            default:
                // A hostname: printable, no spaces, not a placeholder.
                return value.Length is > 0 and <= 64 && value.All(c => c is > ' ' and < (char)127) && value != "*" ? value : null;
        }
    }

    public static string ToCsv(IEnumerable<NetworkRow> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);
        var csv = new StringBuilder();
        csv.AppendLine("Robot,Address in robot list,Hostname,Addresses in backup,Subnet mask,Router,MAC,Backup,Note");
        foreach (NetworkRow r in rows)
        {
            csv.AppendLine(Csv.Row(r.Robot, r.ListedAddress, r.Hostname, r.AddressText, r.SubnetMask, r.Router, r.Mac, r.Backup, r.Note));
        }

        csv.AppendLine();
        csv.AppendLine("Robot,Kind,Variable,Value,File");
        foreach (NetworkSetting s in rows.SelectMany(r => r.Settings))
        {
            csv.AppendLine(Csv.Row(s.Robot, s.Kind, s.Variable, s.Value, s.File));
        }

        return csv.ToString();
    }

    private static string? First(IEnumerable<NetworkSetting> found, string kind) => found.FirstOrDefault(f => f.Kind == kind)?.Value;

    private static string Unquote(string value) =>
        value.Length >= 2 && value[0] is '\'' or '"' && value[^1] == value[0] ? value[1..^1].Trim() : value;

    [GeneratedRegex(@"\$[A-Za-z0-9_]+(?:\[[^\]]*\])?(?:\.\$[A-Za-z0-9_]+(?:\[[^\]]*\])?)*", RegexOptions.CultureInvariant)]
    private static partial Regex VariableOnLine();

    [GeneratedRegex(@"MAC|ETHER|ETH_ADDR|HW_ADDR", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex MacName();

    [GeneratedRegex(@"SUBNET|SNMASK|NETMASK|SUB_MASK", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SubnetName();

    [GeneratedRegex(@"ROUTER|GATEWAY|GW_ADDR", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex RouterName();

    [GeneratedRegex(@"IP_?ADDR|H_ADDR|HOST_ADDR|^ADDR", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex AddressName();

    [GeneratedRegex(@"HOSTNAME|H_NAME|NODE_NAME", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex HostName();

    [GeneratedRegex(@"^\d{1,3}\.\d{1,3}\.\d{1,3}\.\d{1,3}$", RegexOptions.CultureInvariant)]
    private static partial Regex Ipv4();

    [GeneratedRegex(@"^[0-9A-Fa-f]{2}([:\-\s]?)[0-9A-Fa-f]{2}(\1[0-9A-Fa-f]{2}){4}$", RegexOptions.CultureInvariant)]
    private static partial Regex MacShape();
}
