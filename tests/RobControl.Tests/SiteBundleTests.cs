using System.IO.Compression;
using System.Net;
using RobControl.Core.Backup;
using RobControl.Core.Events;
using RobControl.Core.Persistence;
using RobControl.Core.Robots;
using RobControl.Core.Sites;
using Xunit;

namespace RobControl.Tests;

public sealed class SiteBundleTests : IDisposable
{
    private static readonly DateTimeOffset Day1 = new(2026, 9, 1, 8, 0, 0, TimeSpan.Zero);
    private readonly TempFolder _temp = new();

    public void Dispose() => _temp.Dispose();

    private SiteCatalog Catalog(string pc) => new(Path.Combine(_temp.Path, pc, "sites"), Path.Combine(_temp.Path, pc, "Backups"));

    /// <summary>A site on "laptop A" with two robots, an event, and two backups.</summary>
    private static (Site Site, IReadOnlyList<Robot> Robots) MakeSite(SiteCatalog catalog)
    {
        Site site = catalog.Create(new SiteSettings { Name = "Plant 3", Notes = "VLAN 20", StaleAfterDays = 3 });
        using FleetStore store = FleetStore.Open(site.DatabasePath);
        store.Save(new Robot("R1-01", IPAddress.Parse("10.20.1.11")));
        store.Save(new Robot("R1-02", IPAddress.Parse("10.20.1.12")) { Line = "Body" });
        store.Info(EventCategory.Backup, null, "A thing that happened on laptop A");
        FakeBackup.Write(site.Settings.ArchiveRoot, "R1-01", Day1, ("MAIN.LS", "/PROG MAIN"));
        FakeBackup.Write(site.Settings.ArchiveRoot, "R1-02", Day1, ("MAIN.LS", "/PROG OTHER"));
        return (site, store.Robots());
    }

    private string Bundle(SiteCatalog catalog, bool backups)
    {
        (Site site, IReadOnlyList<Robot> robots) = MakeSite(catalog);
        string zip = Path.Combine(_temp.Path, "plant3" + SiteBundle.Extension);
        using FleetStore store = FleetStore.Open(site.DatabasePath);
        SiteBundleInfo info = SiteBundle.Export(site, robots, store, new BackupArchive(site.Settings.ArchiveRoot), backups, zip, "test");
        Assert.Equal(backups ? 2 : 0, info.BackupFolders);
        Assert.True(info.Database);
        return zip;
    }

    [Fact]
    public void ABundleCarriesTheSiteItsHistoryAndItsBackupsToAnotherPc()
    {
        string zip = Bundle(Catalog("laptopA"), backups: true);
        SiteCatalog laptopB = Catalog("laptopB");

        SiteBundleImportResult result = SiteBundleImport.Import(laptopB, zip);

        Assert.Equal("Plant 3", result.Site.Name);
        Assert.Equal(3, result.Site.Settings.StaleAfterDays);
        Assert.Equal(Path.Combine(_temp.Path, "laptopB", "Backups", "Plant 3"), result.Site.Settings.ArchiveRoot);
        Assert.Equal(2, result.Robots);
        Assert.True(result.History);
        Assert.Equal(2, result.BackupFolders);
        Assert.Empty(result.Skipped);

        using FleetStore store = FleetStore.Open(result.Site.DatabasePath);
        Assert.Contains(store.RecentEvents(), e => e.Message == "A thing that happened on laptop A");
        Robot r1 = Assert.Single(store.Robots(), r => r.Name == "R1-01");

        BackupSet backup = Assert.Single(new BackupArchive(result.Site.Settings.ArchiveRoot).List(r1));
        Assert.True(BackupVerifier.Verify(backup).IsIntact);
    }

    [Fact]
    public void WithoutBackupsOnlyTheSiteAndHistoryTravel()
    {
        string zip = Bundle(Catalog("laptopA"), backups: false);

        SiteBundleImportResult result = SiteBundleImport.Import(Catalog("laptopB"), zip);

        Assert.True(result.History);
        Assert.Equal(0, result.BackupFolders);
        Assert.False(Directory.Exists(Path.Combine(result.Site.Settings.ArchiveRoot, "R1-01")));
    }

    [Fact]
    public void ImportingTwiceMakesASecondSiteNeverAMerge()
    {
        string zip = Bundle(Catalog("laptopA"), backups: true);
        SiteCatalog laptopB = Catalog("laptopB");

        SiteBundleImport.Import(laptopB, zip);
        SiteBundleImportResult second = SiteBundleImport.Import(laptopB, zip);

        Assert.Equal("Plant 3 (2)", second.Site.Name);
        Assert.Equal(2, second.BackupFolders);
        Assert.Equal(2, laptopB.List().Count);
    }

    [Fact]
    public void ADatabaseThatDisagreesWithTheRobotListIsLeftOut()
    {
        string zip = Bundle(Catalog("laptopA"), backups: false);

        // Somebody edits the robot list in the site file to aim a robot elsewhere.
        using (ZipArchive archive = ZipFile.Open(zip, ZipArchiveMode.Update))
        {
            ZipArchiveEntry entry = archive.GetEntry(SiteBundle.SiteEntry)!;
            string json;
            using (var reader = new StreamReader(entry.Open()))
            {
                json = reader.ReadToEnd();
            }

            entry.Delete();
            using var writer = new StreamWriter(archive.CreateEntry(SiteBundle.SiteEntry).Open());
            writer.Write(json.Replace("10.20.1.12", "10.20.1.99", StringComparison.Ordinal));
        }

        SiteBundleImportResult result = SiteBundleImport.Import(Catalog("laptopB"), zip);

        Assert.False(result.History);
        Assert.Contains(result.Skipped, s => s.Contains("does not match", StringComparison.Ordinal));
        using FleetStore store = FleetStore.Open(result.Site.DatabasePath);
        Assert.Contains(store.Robots(), r => r.Address.ToString() == "10.20.1.99");
        Assert.DoesNotContain(store.RecentEvents(), e => e.Message == "A thing that happened on laptop A");
    }

    [Fact]
    public void HostileEntryNamesAreSkippedAndNamed()
    {
        string zip = Bundle(Catalog("laptopA"), backups: true);
        using (ZipArchive archive = ZipFile.Open(zip, ZipArchiveMode.Update))
        {
            foreach (string name in new[] { "backups/R1-01/../../escaped.txt", "backups/R1-01/2026-09-02_000000/MD/CON.LS", "backups/loose.txt" })
            {
                using var writer = new StreamWriter(archive.CreateEntry(name).Open());
                writer.Write("x");
            }
        }

        SiteBundleImportResult result = SiteBundleImport.Import(Catalog("laptopB"), zip);

        Assert.Equal(3, result.Skipped.Count);
        Assert.Empty(Directory.GetFiles(_temp.Path, "escaped.txt", SearchOption.AllDirectories));
        Assert.Equal(2, result.BackupFolders);
    }

    [Fact]
    public void AnExistingBackupFolderIsNeverOverwritten()
    {
        string zip = Bundle(Catalog("laptopA"), backups: true);
        string root = Path.Combine(_temp.Path, "elsewhere");
        string existing = Path.Combine(root, "R1-01", ArchiveNames.Stamp(Day1));
        Directory.CreateDirectory(existing);
        File.WriteAllText(Path.Combine(existing, "mine.txt"), "keep me");

        (int folders, IReadOnlyList<string> skipped) = SiteBundle.ExtractBackups(zip, root);

        Assert.Equal(1, folders);
        Assert.Contains(skipped, s => s.Contains("already in the archive", StringComparison.Ordinal));
        Assert.Equal(["mine.txt"], Directory.GetFiles(existing).Select(Path.GetFileName).ToArray());
    }

    [Fact]
    public void AnythingElseIsNotABundle()
    {
        string notZip = Path.Combine(_temp.Path, "x.zip");
        File.WriteAllText(notZip, "hello");
        Assert.Throws<SiteException>(() => SiteBundle.Read(notZip));

        string otherZip = Path.Combine(_temp.Path, "y.zip");
        using (ZipArchive z = ZipFile.Open(otherZip, ZipArchiveMode.Create))
        {
            z.CreateEntry("readme.txt");
        }

        Assert.Contains("not a RobControl site bundle", Assert.Throws<SiteException>(() => SiteBundle.Read(otherZip)).Message, StringComparison.Ordinal);
    }
}
