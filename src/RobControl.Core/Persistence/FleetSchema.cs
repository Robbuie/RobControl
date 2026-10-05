namespace RobControl.Core.Persistence;

/// <summary>
/// The fleet database schema, as ordered steps - the same rule as NetControl's SchemaMigrations:
/// append a step, never edit one that has shipped, and bump nothing else.
/// </summary>
internal static class FleetSchema
{
    public static IReadOnlyList<string> Steps { get; } = [V1, V2];

    public static int CurrentVersion => Steps.Count;

    /// <summary>
    /// Robots, their last probe, and the append-only event log.
    ///
    /// <para>The FTP password column is plain text. A controller FTP password protects a file
    /// listing on an isolated plant network, the file sits in the user's own profile, and encrypting
    /// it properly needs DPAPI and a package. If a site needs more, that is the change to make.</para>
    /// </summary>
    private const string V1 = """
        CREATE TABLE Robot (
            Id            INTEGER PRIMARY KEY,
            Name          TEXT NOT NULL UNIQUE COLLATE NOCASE,
            Address       TEXT NOT NULL,
            Line          TEXT,
            Notes         TEXT,
            FtpUser       TEXT NOT NULL DEFAULT 'anonymous',
            FtpPassword   TEXT NOT NULL DEFAULT '',
            FtpPort       INTEGER NOT NULL DEFAULT 21,
            HttpPort      INTEGER NOT NULL DEFAULT 80,
            IdentityJson  TEXT,
            ProbeUtc      TEXT,
            ProbeSummary  TEXT,
            CreatedUtc    TEXT NOT NULL
        );

        CREATE TABLE Event (
            Id        INTEGER PRIMARY KEY,
            Utc       TEXT NOT NULL,
            Severity  TEXT NOT NULL,
            Category  TEXT NOT NULL,
            Robot     TEXT,
            Message   TEXT NOT NULL,
            Detail    TEXT
        );

        CREATE INDEX IX_Event_Utc ON Event(Utc);
        CREATE INDEX IX_Event_Robot ON Event(Robot);

        -- The record of what was done to the robots. Enforced by the database, not by everyone
        -- remembering: there is no API that would trip these, they exist to catch a future mistake.
        CREATE TRIGGER TR_Event_NoUpdate BEFORE UPDATE ON Event
        BEGIN
            SELECT RAISE(ABORT, 'The Event table is append-only: rows may not be updated.');
        END;

        CREATE TRIGGER TR_Event_NoDelete BEFORE DELETE ON Event
        BEGIN
            SELECT RAISE(ABORT, 'The Event table is append-only: rows may not be deleted.');
        END;
        """;

    /// <summary>
    /// Trending: the signals watched on each robot, and their stored samples.
    ///
    /// <para>Samples are <b>not</b> append-only, unlike Event: retention prunes them, and that is a
    /// deliberate difference - a trend is measurement, not a record of what was done to a robot. The
    /// record of recording (who started it, on what, for how long) is in Event, which stays
    /// append-only.</para>
    ///
    /// <para>Time is integer Unix milliseconds rather than the round-trip text Event uses: there will
    /// be millions of rows, and a range scan on an integer index is what keeps a chart quick.</para>
    /// </summary>
    private const string V2 = """
        CREATE TABLE TrendSignal (
            Id          INTEGER PRIMARY KEY,
            RobotId     INTEGER NOT NULL REFERENCES Robot(Id) ON DELETE CASCADE,
            Address     TEXT NOT NULL,
            Label       TEXT,
            CreatedUtc  TEXT NOT NULL,
            UNIQUE (RobotId, Address)
        );

        CREATE TABLE TrendSample (
            SignalId  INTEGER NOT NULL REFERENCES TrendSignal(Id) ON DELETE CASCADE,
            UtcMs     INTEGER NOT NULL,
            Value     REAL NOT NULL
        );

        CREATE INDEX IX_TrendSample_Signal_Utc ON TrendSample(SignalId, UtcMs);
        CREATE INDEX IX_TrendSample_Utc ON TrendSample(UtcMs);
        """;
}
