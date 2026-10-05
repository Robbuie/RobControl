using System.Globalization;
using System.Net;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using RobControl.Core.Controllers;
using RobControl.Core.Diagnostics;
using RobControl.Core.Events;
using RobControl.Core.Robots;
using RobControl.Core.Transports.Ftp;

namespace RobControl.Core.Persistence;

/// <summary>
/// The fleet database: the robot list, each robot's last probe, and the append-only event log.
/// One SQLite file in <c>%LOCALAPPDATA%\RobControl</c>.
///
/// <para><b>Backups are not in here.</b> They are folders with manifests (see
/// <see cref="Backup.BackupArchive"/>), so the archive stands on its own and can live on a share.
/// SQLite on a network share is a known way to corrupt a database, which is the other reason.</para>
///
/// <para>Implements <see cref="IEventSink"/> and, per that contract, never throws from
/// <see cref="Record"/>: a backup must not fail because its log row could not be written.</para>
/// </summary>
public sealed class FleetStore : IEventSink, IDisposable
{
    private static readonly JsonSerializerOptions Json = new()
    {
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter<ControllerGeneration>() },
    };

    private readonly object _gate = new();
    private readonly SqliteConnection _connection;
    private readonly ITraceLog _trace;

    private FleetStore(SqliteConnection connection, ITraceLog trace)
    {
        _connection = connection;
        _trace = trace;
    }

    /// <summary>Raised after an event row is written, on the writer's thread.</summary>
    public event EventHandler<RobotEvent>? EventRecorded;

    public static FleetStore Open(string path, ITraceLog? trace = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        return OpenConnection(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString(), trace);
    }

    /// <summary>For tests: a private in-memory database.</summary>
    public static FleetStore OpenInMemory(ITraceLog? trace = null) =>
        OpenConnection("Data Source=:memory:", trace);

    private static FleetStore OpenConnection(string connectionString, ITraceLog? trace)
    {
        var connection = new SqliteConnection(connectionString);
        try
        {
            connection.Open();
            using (SqliteCommand pragma = connection.CreateCommand())
            {
                pragma.CommandText = "PRAGMA foreign_keys = ON; PRAGMA journal_mode = WAL;";
                pragma.ExecuteNonQuery();
            }

            Migrate(connection);
            return new FleetStore(connection, trace ?? NullTraceLog.Instance);
        }
        catch (SqliteException ex)
        {
            connection.Dispose();
            throw new PersistenceException($"The fleet database could not be opened: {ex.Message}", ex)
            {
                Remediation = "Close any other copy of RobControl. If it persists, move robcontrol.db aside and restart - "
                    + "backups are not stored in it and are not affected.",
            };
        }
    }

    public IReadOnlyList<Robot> Robots()
    {
        lock (_gate)
        {
            using SqliteCommand command = _connection.CreateCommand();
            command.CommandText = "SELECT Id, Name, Address, Line, Notes, FtpUser, FtpPassword, FtpPort, HttpPort FROM Robot ORDER BY Line, Name";
            using SqliteDataReader reader = command.ExecuteReader();
            var robots = new List<Robot>();
            while (reader.Read())
            {
                robots.Add(new Robot(reader.GetString(1), IPAddress.Parse(reader.GetString(2)))
                {
                    Id = reader.GetInt64(0),
                    Line = reader.IsDBNull(3) ? null : reader.GetString(3),
                    Notes = reader.IsDBNull(4) ? null : reader.GetString(4),
                    Ftp = new FtpCredentials(reader.GetString(5), reader.GetString(6)),
                    FtpPort = reader.GetInt32(7),
                    HttpPort = reader.GetInt32(8),
                });
            }

            return robots;
        }
    }

    /// <summary>Adds a robot, or updates it when <see cref="Robot.Id"/> is set. Returns it with its id.</summary>
    public Robot Save(Robot robot)
    {
        ArgumentNullException.ThrowIfNull(robot);
        lock (_gate)
        {
            using SqliteCommand command = _connection.CreateCommand();
            command.CommandText = robot.Id == 0
                ? """
                  INSERT INTO Robot (Name, Address, Line, Notes, FtpUser, FtpPassword, FtpPort, HttpPort, CreatedUtc)
                  VALUES ($name, $address, $line, $notes, $user, $password, $ftpPort, $httpPort, $now)
                  RETURNING Id;
                  """
                : """
                  UPDATE Robot SET Name = $name, Address = $address, Line = $line, Notes = $notes, FtpUser = $user,
                      FtpPassword = $password, FtpPort = $ftpPort, HttpPort = $httpPort
                  WHERE Id = $id
                  RETURNING Id;
                  """;
            command.Parameters.AddWithValue("$id", robot.Id);
            command.Parameters.AddWithValue("$name", robot.Name);
            command.Parameters.AddWithValue("$address", robot.Address.ToString());
            command.Parameters.AddWithValue("$line", (object?)robot.Line ?? DBNull.Value);
            command.Parameters.AddWithValue("$notes", (object?)robot.Notes ?? DBNull.Value);
            command.Parameters.AddWithValue("$user", robot.Ftp.User);
            command.Parameters.AddWithValue("$password", robot.Ftp.Password);
            command.Parameters.AddWithValue("$ftpPort", robot.FtpPort);
            command.Parameters.AddWithValue("$httpPort", robot.HttpPort);
            command.Parameters.AddWithValue("$now", SqlTime.ToSql(DateTimeOffset.UtcNow));

            try
            {
                object? id = command.ExecuteScalar();
                return id is null
                    ? throw new PersistenceException($"No robot with id {robot.Id} to update.")
                    : robot with { Id = Convert.ToInt64(id, CultureInfo.InvariantCulture) };
            }
            catch (SqliteException ex) when (ex.SqliteErrorCode == 19)
            {
                throw new PersistenceException($"There is already a robot called '{robot.Name}'.", ex)
                {
                    Remediation = "Robot names are also archive folder names, so they have to be unique.",
                };
            }
        }
    }

    /// <summary>
    /// Removes a robot from the list. Its backups stay on disk, and its event rows stay in the log -
    /// they name the robot as text, so the record survives the robot being removed.
    /// </summary>
    public void Remove(Robot robot)
    {
        ArgumentNullException.ThrowIfNull(robot);
        lock (_gate)
        {
            using SqliteCommand command = _connection.CreateCommand();
            command.CommandText = "DELETE FROM Robot WHERE Id = $id";
            command.Parameters.AddWithValue("$id", robot.Id);
            command.ExecuteNonQuery();
        }
    }

    public void SaveProbe(ProbeReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        lock (_gate)
        {
            using SqliteCommand command = _connection.CreateCommand();
            command.CommandText = "UPDATE Robot SET IdentityJson = $identity, ProbeUtc = $utc, ProbeSummary = $summary WHERE Id = $id";
            command.Parameters.AddWithValue("$identity", JsonSerializer.Serialize(report.Identity, Json));
            command.Parameters.AddWithValue("$utc", SqlTime.ToSql(report.Utc));
            command.Parameters.AddWithValue("$summary", report.Summary);
            command.Parameters.AddWithValue("$id", report.Robot.Id);
            command.ExecuteNonQuery();
        }
    }

    /// <summary>What the last probe found, or nulls if the robot has never been probed.</summary>
    public (ControllerIdentity? Identity, DateTimeOffset? Utc, string? Summary) LastProbe(Robot robot)
    {
        ArgumentNullException.ThrowIfNull(robot);
        lock (_gate)
        {
            using SqliteCommand command = _connection.CreateCommand();
            command.CommandText = "SELECT IdentityJson, ProbeUtc, ProbeSummary FROM Robot WHERE Id = $id";
            command.Parameters.AddWithValue("$id", robot.Id);
            using SqliteDataReader reader = command.ExecuteReader();
            if (!reader.Read() || reader.IsDBNull(0))
            {
                return (null, null, null);
            }

            ControllerIdentity? identity = JsonSerializer.Deserialize<ControllerIdentity>(reader.GetString(0), Json);
            return (identity, reader.IsDBNull(1) ? null : SqlTime.FromSql(reader.GetString(1)), reader.IsDBNull(2) ? null : reader.GetString(2));
        }
    }

    /// <summary>Appends one event row. Never throws - see the class remarks.</summary>
    public void Record(RobotEvent entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        try
        {
            lock (_gate)
            {
                using SqliteCommand command = _connection.CreateCommand();
                command.CommandText = """
                    INSERT INTO Event (Utc, Severity, Category, Robot, Message, Detail)
                    VALUES ($utc, $severity, $category, $robot, $message, $detail)
                    """;
                command.Parameters.AddWithValue("$utc", SqlTime.ToSql(entry.Utc));
                command.Parameters.AddWithValue("$severity", entry.Severity.ToString().ToLowerInvariant());
                command.Parameters.AddWithValue("$category", entry.Category.ToString().ToLowerInvariant());
                command.Parameters.AddWithValue("$robot", (object?)entry.Robot ?? DBNull.Value);
                command.Parameters.AddWithValue("$message", entry.Message);
                command.Parameters.AddWithValue("$detail", (object?)entry.Detail ?? DBNull.Value);
                command.ExecuteNonQuery();
            }
        }
        catch (Exception ex) when (ex is SqliteException or InvalidOperationException or ObjectDisposedException)
        {
            _trace.Error($"Could not write an event row: {entry.Message}", ex);
            return;
        }

        EventRecorded?.Invoke(this, entry);
    }

    /// <summary>The most recent events, newest first, optionally for one robot.</summary>
    public IReadOnlyList<RobotEvent> RecentEvents(int limit = 500, Robot? robot = null)
    {
        lock (_gate)
        {
            using SqliteCommand command = _connection.CreateCommand();
            command.CommandText = robot is null
                ? "SELECT Utc, Severity, Category, Robot, Message, Detail FROM Event ORDER BY Id DESC LIMIT $limit"
                : "SELECT Utc, Severity, Category, Robot, Message, Detail FROM Event WHERE Robot = $robot ORDER BY Id DESC LIMIT $limit";
            command.Parameters.AddWithValue("$limit", limit);
            if (robot is not null)
            {
                command.Parameters.AddWithValue("$robot", robot.Describe());
            }

            using SqliteDataReader reader = command.ExecuteReader();
            var events = new List<RobotEvent>();
            while (reader.Read())
            {
                events.Add(new RobotEvent(
                    SqlTime.FromSql(reader.GetString(0)),
                    Enum.Parse<EventSeverity>(reader.GetString(1), ignoreCase: true),
                    Enum.Parse<EventCategory>(reader.GetString(2), ignoreCase: true),
                    reader.IsDBNull(3) ? null : reader.GetString(3),
                    reader.GetString(4),
                    reader.IsDBNull(5) ? null : reader.GetString(5)));
            }

            return events;
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _connection.Dispose();
        }
    }

    private static void Migrate(SqliteConnection connection)
    {
        using SqliteCommand read = connection.CreateCommand();
        read.CommandText = "PRAGMA user_version";
        int version = Convert.ToInt32(read.ExecuteScalar(), CultureInfo.InvariantCulture);

        if (version > FleetSchema.CurrentVersion)
        {
            throw new PersistenceException(
                $"The fleet database is schema version {version}; this build understands up to {FleetSchema.CurrentVersion}.")
            {
                Remediation = "It was written by a newer RobControl. Update this copy.",
            };
        }

        for (int step = version; step < FleetSchema.CurrentVersion; step++)
        {
            using SqliteTransaction transaction = connection.BeginTransaction();
            using SqliteCommand apply = connection.CreateCommand();
            apply.CommandText = FleetSchema.Steps[step] + $"\nPRAGMA user_version = {step + 1};";
            apply.ExecuteNonQuery();
            transaction.Commit();
        }
    }
}
