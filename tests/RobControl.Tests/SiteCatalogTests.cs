using System.Net;
using System.Text.Json;
using RobControl.Core.Robots;
using RobControl.Core.Sites;
using RobControl.Core.Transports.Ftp;
using Xunit;

namespace RobControl.Tests;

public sealed class SiteCatalogTests : IDisposable
{
    private readonly TempFolder _temp = new();

    private SiteCatalog NewCatalog() =>
        new(Path.Combine(_temp.Path, "sites"), Path.Combine(_temp.Path, "Backups"));

    public void Dispose() => _temp.Dispose();

    [Fact]
    public void A_new_site_has_its_own_folder_settings_file_and_archive()
    {
        SiteCatalog catalog = NewCatalog();

        Site site = catalog.Create(new SiteSettings { Name = "Lansing Delta" });

        Assert.Equal("Lansing Delta", site.Key);
        Assert.True(File.Exists(site.SettingsPath));
        Assert.Equal(Path.Combine(_temp.Path, "Backups", "Lansing Delta"), site.Settings.ArchiveRoot);
        Assert.Equal(Path.Combine(site.Folder, "robcontrol.db"), site.DatabasePath);

        Site listed = Assert.Single(catalog.List());
        Assert.Equal(site.Settings, listed.Settings);
    }

    [Fact]
    public void The_settings_file_uses_names_a_person_can_edit()
    {
        Site site = NewCatalog().Create(new SiteSettings { Name = "Plant 3", ScheduleHours = 8, DefaultFtpUser = "robot" });

        using JsonDocument json = JsonDocument.Parse(File.ReadAllText(site.SettingsPath));
        Assert.Equal("Plant 3", json.RootElement.GetProperty("name").GetString());
        Assert.Equal(8, json.RootElement.GetProperty("scheduleHours").GetInt32());
        Assert.Equal("robot", json.RootElement.GetProperty("defaultFtpUser").GetString());
    }

    [Fact]
    public void A_hand_edit_takes_effect_and_out_of_range_values_are_clamped()
    {
        SiteCatalog catalog = NewCatalog();
        Site site = catalog.Create(new SiteSettings { Name = "Plant 3" });

        File.WriteAllText(site.SettingsPath, """
            {
              // comments and trailing commas are forgiven
              "name": "Plant 3",
              "archiveRoot": "\\\\server\\robots\\plant3",
              "concurrency": 50,
              "scheduleHours": -4,
              "trendRetentionDays": 0,
            }
            """);

        Site reloaded = catalog.Load(site.Folder);
        Assert.Equal(@"\\server\robots\plant3", reloaded.Settings.ArchiveRoot);
        Assert.Equal(8, reloaded.Settings.Concurrency);
        Assert.Equal(0, reloaded.Settings.ScheduleHours);
        Assert.Equal(1, reloaded.Settings.TrendRetentionDays);
    }

    [Fact]
    public void A_broken_settings_file_still_lists_the_site_under_its_folder_name()
    {
        SiteCatalog catalog = NewCatalog();
        Site site = catalog.Create(new SiteSettings { Name = "Plant 3" });
        File.WriteAllText(site.SettingsPath, "{ not json");

        Site listed = Assert.Single(catalog.List());
        Assert.Equal("Plant 3", listed.Name);
        Assert.Equal(2, listed.Settings.Concurrency);
    }

    [Fact]
    public void Two_sites_cannot_share_a_name_whatever_the_case()
    {
        SiteCatalog catalog = NewCatalog();
        catalog.Create(new SiteSettings { Name = "Plant 3" });

        Assert.Throws<SiteException>(() => catalog.Create(new SiteSettings { Name = "PLANT 3" }));
        Assert.Throws<SiteException>(() => catalog.Create(new SiteSettings { Name = "   " }));
    }

    [Fact]
    public void Renaming_keeps_the_folder_and_frees_the_old_name()
    {
        SiteCatalog catalog = NewCatalog();
        Site site = catalog.Create(new SiteSettings { Name = "Plant 3" });

        Site renamed = catalog.Save(site, site.Settings with { Name = "Plant 3 body shop" });
        Assert.Equal(site.Folder, renamed.Folder);
        Assert.Equal("Plant 3 body shop", catalog.Find("plant 3 BODY shop")!.Name);
        Assert.Equal("Plant 3 body shop", catalog.Find("Plant 3")!.Name); // the key still finds it

        // The old name is free again; its folder name is not, so the new site gets the next one.
        Site again = catalog.Create(new SiteSettings { Name = "Plant 3" });
        Assert.Equal("Plant 3-2", again.Key);
        Assert.Equal(2, catalog.List().Count);
    }

    [Fact]
    public void A_name_Windows_will_not_take_still_makes_a_usable_folder()
    {
        Site site = NewCatalog().Create(new SiteSettings { Name = "Plant 3: body/paint" });

        Assert.Equal("Plant 3_ body_paint", site.Key);
        Assert.Equal("Plant 3: body/paint", site.Name);
        Assert.True(Directory.Exists(site.Folder));
    }

    [Fact]
    public void Adopting_the_old_robot_list_moves_the_database_rather_than_copying_it()
    {
        SiteCatalog catalog = NewCatalog();
        string legacy = Path.Combine(_temp.Path, "robcontrol.db");
        File.WriteAllText(legacy, "db");
        File.WriteAllText(legacy + "-wal", "wal");

        Site site = catalog.Adopt(legacy, new SiteSettings { Name = "My robots", ArchiveRoot = @"D:\Old backups" });

        Assert.False(File.Exists(legacy));
        Assert.False(File.Exists(legacy + "-wal"));
        Assert.Equal("db", File.ReadAllText(site.DatabasePath));
        Assert.Equal("wal", File.ReadAllText(site.DatabasePath + "-wal"));
        Assert.Equal(@"D:\Old backups", site.Settings.ArchiveRoot);
    }

    [Fact]
    public void An_export_carries_settings_and_robots_and_imports_as_a_new_site()
    {
        SiteCatalog catalog = NewCatalog();
        Site site = catalog.Create(new SiteSettings { Name = "Plant 3", ScheduleHours = 12, Notes = "Ask for Dave" });
        Robot robot = new("R1-01", IPAddress.Parse("10.20.1.11"))
        {
            Line = "Line 1",
            Ftp = new FtpCredentials("robot", "secret"),
            HttpPort = 8080,
        };
        string file = Path.Combine(_temp.Path, "plant3.robcontrol-site.json");

        SiteCatalog.Export(site, [robot], file, "RobControl test");
        SiteFile read = SiteCatalog.ReadExport(file);

        Assert.Equal(12, read.Settings.ScheduleHours);
        SiteRobot entry = Assert.Single(read.Robots);
        Assert.True(entry.TryBuild(out Robot? rebuilt, out _));
        Assert.Equal(robot.Address, rebuilt!.Address);
        Assert.Equal("secret", rebuilt.Ftp.Password);
        Assert.Equal(8080, rebuilt.HttpPort);
        Assert.Equal("Line 1", rebuilt.Line);

        // Importing next to the original: a new site, never a merge.
        Site imported = catalog.Import(read);
        Assert.Equal("Plant 3 (2)", imported.Name);
        Assert.NotEqual(site.Folder, imported.Folder);
        Assert.Equal("Ask for Dave", imported.Settings.Notes);
    }

    [Fact]
    public void An_imported_archive_folder_that_is_not_on_this_PC_falls_back_to_the_default()
    {
        SiteCatalog catalog = NewCatalog();
        var file = new SiteFile { Settings = new SiteSettings { Name = "Plant 9", ArchiveRoot = Path.Combine(_temp.Path, "nowhere") } };

        Site imported = catalog.Import(file);

        Assert.Equal(catalog.DefaultArchiveRoot("Plant 9"), imported.Settings.ArchiveRoot);
    }

    [Fact]
    public void A_file_that_is_not_a_site_export_is_refused_with_a_sentence()
    {
        string other = Path.Combine(_temp.Path, "other.json");
        File.WriteAllText(other, """{ "checkForUpdates": false }""");
        string newer = Path.Combine(_temp.Path, "newer.json");
        File.WriteAllText(newer, """{ "format": "robcontrol-site", "version": 99 }""");
        string junk = Path.Combine(_temp.Path, "junk.json");
        File.WriteAllText(junk, "<html>");

        Assert.Throws<SiteException>(() => SiteCatalog.ReadExport(other));
        Assert.Contains("newer", Assert.Throws<SiteException>(() => SiteCatalog.ReadExport(newer)).Message, StringComparison.Ordinal);
        Assert.Throws<SiteException>(() => SiteCatalog.ReadExport(junk));
    }

    [Theory]
    [InlineData("255.255.255.255")]
    [InlineData("0.0.0.0")]
    [InlineData("224.0.0.1")]
    [InlineData("10.20.1")]
    [InlineData("robot-1")]
    public void A_site_file_cannot_aim_RobControl_at_anything_but_one_controller(string address)
    {
        var entry = new SiteRobot { Name = "R1-01", Address = address };

        Assert.False(entry.TryBuild(out Robot? robot, out string? problem));
        Assert.Null(robot);
        Assert.NotNull(problem);
    }
}
