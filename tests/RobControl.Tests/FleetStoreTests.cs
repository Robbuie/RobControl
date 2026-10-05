using System.Net;
using Microsoft.Data.Sqlite;
using RobControl.Core.Controllers;
using RobControl.Core.Events;
using RobControl.Core.Persistence;
using RobControl.Core.Robots;
using Xunit;

namespace RobControl.Tests;

public class FleetStoreTests
{
    [Fact]
    public void RobotsRoundTrip()
    {
        using FleetStore store = FleetStore.OpenInMemory();
        Robot saved = store.Save(new Robot("R2-14", IPAddress.Parse("10.20.1.54")) { Line = "Body shop", FtpPort = 2121 });
        Assert.NotEqual(0, saved.Id);

        Robot loaded = Assert.Single(store.Robots());
        Assert.Equal("R2-14", loaded.Name);
        Assert.Equal(IPAddress.Parse("10.20.1.54"), loaded.Address);
        Assert.Equal(2121, loaded.FtpPort);
        Assert.Equal("anonymous", loaded.Ftp.User);
    }

    [Fact]
    public void NamesAreUniqueIgnoringCase()
    {
        using FleetStore store = FleetStore.OpenInMemory();
        store.Save(new Robot("R2-14", IPAddress.Parse("10.20.1.54")));
        Assert.Throws<PersistenceException>(() => store.Save(new Robot("r2-14", IPAddress.Parse("10.20.1.55"))));
    }

    [Fact]
    public void ProbeIdentityRoundTrips()
    {
        using FleetStore store = FleetStore.OpenInMemory();
        Robot robot = store.Save(new Robot("R2-14", IPAddress.Parse("10.20.1.54")));
        var identity = new ControllerIdentity { Generation = ControllerGeneration.R30iBPlus, SoftwareVersion = "V9.40P/23", Application = "SpotTool+" };
        store.SaveProbe(new ProbeReport(robot, DateTimeOffset.UtcNow, identity, []));

        (ControllerIdentity? loaded, DateTimeOffset? utc, _) = store.LastProbe(robot);
        Assert.Equal(identity, loaded);
        Assert.NotNull(utc);
    }

    [Fact]
    public void TheEventLogIsAppendOnlyAtTheDatabase()
    {
        using var temp = new TempFolder();
        string path = Path.Combine(temp.Path, "fleet.db");
        using (FleetStore store = FleetStore.Open(path))
        {
            store.Info(EventCategory.App, null, "Started.");
            Assert.Single(store.RecentEvents());
        }

        using var raw = new SqliteConnection($"Data Source={path};Pooling=False");
        raw.Open();
        using SqliteCommand update = raw.CreateCommand();
        update.CommandText = "UPDATE Event SET Message = 'rewritten'";
        Assert.Throws<SqliteException>(() => update.ExecuteNonQuery());
        using SqliteCommand delete = raw.CreateCommand();
        delete.CommandText = "DELETE FROM Event";
        Assert.Throws<SqliteException>(() => delete.ExecuteNonQuery());
    }
}
