using System.Net;
using System.Text.Json;
using RobControl.Core.Backup;
using RobControl.Core.Robots;
using RobControl.Core.Sites;
using RobControl.Core.Systems;
using RobControl.Core.Transports.Ftp;
using Xunit;

namespace RobControl.Tests;

public sealed class SystemCatalogTests : IDisposable
{
    private readonly TempFolder _temp = new();

    private string Data => Path.Combine(_temp.Path, "profile", "sites");

    private string Config => Path.Combine(_temp.Path, "Documents", "RobControl", "Systems");

    private SiteCatalog Sites() => new(Data, Path.Combine(_temp.Path, "Backups"), configRoot: Config);

    private SystemCatalog Systems() => new(Config);

    public void Dispose() => _temp.Dispose();

    private static Robot R(string name, string address, int port = 21) => new(name, IPAddress.Parse(address)) { FtpPort = port, Id = 1 };

    [Fact]
    public void A_site_keeps_its_database_in_the_profile_and_its_settings_in_the_config_folder()
    {
        Site site = Sites().Create(new SiteSettings { Name = "Plant 3" });

        Assert.Equal(Path.Combine(Data, "Plant 3", "robcontrol.db"), site.DatabasePath);
        Assert.Equal(Path.Combine(Config, "Plant 3", "site.json"), site.SettingsPath);
        Assert.True(File.Exists(site.SettingsPath));
        Assert.False(File.Exists(Path.Combine(Data, "Plant 3", "site.json")));
        Assert.Equal("Plant 3", Assert.Single(Sites().List()).Name);
    }

    [Fact]
    public void A_site_json_from_before_systems_is_moved_into_the_config_folder()
    {
        // 0.5.0's layout: site.json beside the database.
        string old = Path.Combine(Data, "lansing");
        Directory.CreateDirectory(old);
        File.WriteAllText(Path.Combine(old, "site.json"), """{ "name": "Lansing Delta", "scheduleHours": 8 }""");

        Site site = Assert.Single(Sites().List());

        Assert.Equal("Lansing Delta", site.Name);
        Assert.Equal(8, site.Settings.ScheduleHours);
        Assert.Equal(Path.Combine(Config, "lansing", "site.json"), site.SettingsPath);
        Assert.True(File.Exists(site.SettingsPath));
        Assert.False(File.Exists(Path.Combine(old, "site.json")));
        Assert.Equal(Path.Combine(old, "robcontrol.db"), site.DatabasePath);
    }

    [Fact]
    public void A_site_with_robots_and_no_systems_gets_Main_holding_all_of_them()
    {
        Site site = Sites().Create(new SiteSettings { Name = "Plant 3" });
        Robot[] robots = [R("R1", "10.0.0.11"), R("R2", "10.0.0.12")];

        RobotSystem main = Assert.Single(Systems().EnsureAny(site.Key, robots));

        Assert.Equal(SystemCatalog.DefaultName, main.Name);
        Assert.Equal(Path.Combine(Config, "Plant 3", "Main", "system.json"), main.SettingsPath);
        Assert.Equal(["10.0.0.11", "10.0.0.12"], main.Settings.Robots.Select(r => r.Address));
        Assert.Equal(["R1", "R2"], main.Settings.Robots.Select(r => r.Name));

        // Only the first time: a second call finds it and makes nothing.
        Assert.Single(Systems().EnsureAny(site.Key, robots));
        Assert.Single(Systems().List(site.Key));
    }

    [Fact]
    public void Systems_are_separate_and_a_robot_shows_in_the_one_that_lists_it()
    {
        SystemCatalog catalog = Systems();
        RobotSystem framer = catalog.Create("plant", new SystemSettings { Name = "Line 4 framer" });
        RobotSystem respot = catalog.Create("plant", new SystemSettings { Name = "Respot" });
        Robot a = R("A", "10.0.0.1");
        Robot b = R("B", "10.0.0.2");
        Robot loose = R("Loose", "10.0.0.9");
        framer = catalog.Save(framer, framer.Settings.With(a));
        respot = catalog.Save(respot, respot.Settings.With(b));

        IReadOnlyList<RobotSystem> all = catalog.List("plant");
        Robot[] fleet = [a, b, loose];

        Assert.Equal([a], SystemMembership.Members(framer, fleet, all));
        Assert.Equal([b], SystemMembership.Members(respot, fleet, all));
        Assert.Equal([loose], SystemMembership.Members(RobotSystem.Unassigned("plant"), fleet, all));
        Assert.Equal("Line 4 framer", catalog.SystemOf("plant", a)?.Name);
    }

    [Fact]
    public void Moving_a_robot_takes_its_own_backup_settings_with_it()
    {
        SystemCatalog catalog = Systems();
        RobotSystem from = catalog.Create("plant", new SystemSettings { Name = "From" });
        RobotSystem to = catalog.Create("plant", new SystemSettings { Name = "To" });
        Robot robot = R("R1", "10.0.0.1");
        catalog.Save(from, from.Settings.With(robot, new BackupProfile { Devices = ["FR:"] }));

        RobotSystem moved = catalog.Move(robot, to);

        Assert.Equal(["FR:"], moved.Settings.EntryFor(robot)?.Backup?.Devices);
        Assert.False(catalog.Find("plant", "From")!.Settings.Contains(robot));
        Assert.Equal("To", catalog.SystemOf("plant", robot)?.Name);
    }

    [Fact]
    public void Robot_settings_override_the_system_which_overrides_the_defaults()
    {
        var system = new BackupProfile { Devices = ["md:", "fr:"], Exclude = ["*.DG"], IncludeSubfolders = false };
        var robot = new BackupProfile { Devices = ["MD:", "FRA:"], PauseBetweenFilesMs = 250 };

        BackupOptions resolved = BackupProfile.Resolve(system, robot);

        Assert.Equal(["MD:", "FRA:"], resolved.Devices);
        Assert.Equal(["*.DG"], resolved.Exclude);
        Assert.False(resolved.IncludeSubfolders);
        Assert.Equal(TimeSpan.FromMilliseconds(250), resolved.PauseBetweenFiles);
        Assert.Equal(2, resolved.AttemptsPerFile);

        BackupOptions bare = BackupProfile.Resolve(null, null);
        Assert.Equal(["MD:"], bare.Devices);
        Assert.True(bare.IncludeSubfolders);
        Assert.Equal(["MD:", "FR:"], BackupProfile.Resolve(new BackupProfile(), null, siteIncludesFrom: true).Devices);
    }

    [Theory]
    [InlineData("md:", "MD:")]
    [InlineData(" fr ", "FR:")]
    [InlineData("UD1:", "UD1:")]
    [InlineData("fra:", "FRA:")]
    [InlineData("C:\\temp", null)]
    [InlineData("md:/x", null)]
    [InlineData("*", null)]
    [InlineData("1MD", null)]
    [InlineData("TOOLONGNAME", null)]
    [InlineData("", null)]
    public void Only_plain_device_names_are_accepted(string raw, string? expected)
    {
        BackupProfile.TryNormaliseDevice(raw, out string? device);
        Assert.Equal(expected, device);
    }

    [Fact]
    public void A_hand_edited_system_file_is_read_with_comments_and_bad_values_dropped()
    {
        SystemCatalog catalog = Systems();
        RobotSystem made = catalog.Create("plant", new SystemSettings { Name = "Cell 2" });
        File.WriteAllText(made.SettingsPath, """
            {
              // a robot typed in by hand, one with a broken address, and a duplicate
              "name": "Cell 2",
              "backup": { "devices": [ "md", "C:\\bad", "fr:" ], "attemptsPerFile": 99 },
              "robots": [
                { "address": "10.1.1.5", "name": "R5", "backup": { "exclude": [ "*.DG" ] } },
                { "address": "10.1.1.5", "name": "R5 again" },
                { "address": "not an address", "name": "Typo" },
                { "address": "255.255.255.255", "name": "Broadcast" },
              ],
            }
            """);

        RobotSystem read = catalog.Find("plant", "Cell 2")!;
        Assert.Equal(3, read.Settings.Robots.Count);

        BackupOptions options = BackupProfile.Resolve(read.Settings.Backup, read.Settings.Robots[0].Backup);
        Assert.Equal(["MD:", "FR:"], options.Devices);
        Assert.Equal(5, options.AttemptsPerFile);
        Assert.Equal(["*.DG"], options.Exclude);

        IReadOnlyList<Robot> adopt = SystemMembership.Adoptable(read, [], FtpCredentials.Default, out IReadOnlyList<string> problems);
        Robot r5 = Assert.Single(adopt);
        Assert.Equal("R5", r5.Name);
        Assert.Equal(IPAddress.Parse("10.1.1.5"), r5.Address);
        Assert.Equal(2, problems.Count);
    }

    [Fact]
    public void A_robot_already_in_the_database_is_not_adopted_twice_and_a_taken_name_is_refused()
    {
        var system = new RobotSystem("plant", "c", Config, new SystemSettings
        {
            Name = "C",
            Robots = [new SystemRobot { Address = "10.0.0.1", Name = "R1" }, new SystemRobot { Address = "10.0.0.2", Name = "R1" }],
        });

        IReadOnlyList<Robot> adopt = SystemMembership.Adoptable(system, [R("R1", "10.0.0.1")], FtpCredentials.Default, out IReadOnlyList<string> problems);

        Assert.Empty(adopt);
        Assert.Contains("already called 'R1'", Assert.Single(problems), StringComparison.Ordinal);
    }

    [Fact]
    public void An_edited_address_moves_the_entry_and_keeps_its_settings()
    {
        Robot before = R("R1", "10.0.0.1");
        Robot after = R("R1-renamed", "10.0.0.7");
        SystemSettings settings = new SystemSettings { Name = "S" }.With(before, new BackupProfile { Devices = ["FR:"] });

        SystemSettings renamed = settings.Renamed(before, after);

        SystemRobot entry = Assert.Single(renamed.Robots);
        Assert.Equal("10.0.0.7", entry.Address);
        Assert.Equal("R1-renamed", entry.Name);
        Assert.Equal(["FR:"], entry.Backup?.Devices);
    }

    [Fact]
    public void The_system_file_uses_names_a_person_can_edit()
    {
        RobotSystem made = Systems().Create("plant", new SystemSettings
        {
            Name = "Line 4",
            Backup = new BackupProfile { Devices = ["MD:", "FR:"] },
        }.With(R("R1", "10.0.0.1")));

        using JsonDocument json = JsonDocument.Parse(File.ReadAllText(made.SettingsPath));
        JsonElement root = json.RootElement;
        Assert.Equal("Line 4", root.GetProperty("name").GetString());
        Assert.Equal("FR:", root.GetProperty("backup").GetProperty("devices")[1].GetString());
        Assert.False(root.GetProperty("backup").TryGetProperty("exclude", out _));
        JsonElement robot = root.GetProperty("robots")[0];
        Assert.Equal("10.0.0.1", robot.GetProperty("address").GetString());
        Assert.False(robot.TryGetProperty("backup", out _));
    }

    [Fact]
    public void Two_systems_in_one_site_cannot_share_a_name()
    {
        SystemCatalog catalog = Systems();
        catalog.Create("plant", new SystemSettings { Name = "Line 4" });

        Assert.Throws<RobotSystemException>(() => catalog.Create("plant", new SystemSettings { Name = "line 4" }));
        catalog.Create("other plant", new SystemSettings { Name = "Line 4" });
    }
}
