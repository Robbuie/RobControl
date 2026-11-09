namespace RobControl.Core.Capture;

/// <summary>What a capture takes, and how gently.</summary>
public sealed record CaptureOptions
{
    /// <summary>"RobControl 0.7.2+abc" - written into the report and capture.json.</summary>
    public string Tool { get; init; } = "RobControl";

    /// <summary>Log in over FTP, list MD: and copy a few representative files.</summary>
    public bool IncludeFtp { get; init; } = true;

    /// <summary>Two KCL reads (<c>SHOW VAR $VERSION</c>, <c>SHOW CLOCK</c>), through the classifier.</summary>
    public bool IncludeKcl { get; init; } = true;

    /// <summary>
    /// Most diagnostic pages fetched in one capture. The index on an R-30iB V8.30 listed a few dozen;
    /// the cap is there so a controller with an unexpectedly long index is not read end to end.
    /// </summary>
    public int PageLimit { get; init; } = 80;

    /// <summary>Pause between two requests. Controller web servers are small.</summary>
    public TimeSpan Pause { get; init; } = TimeSpan.FromMilliseconds(250);

    /// <summary>A complete backup folder to copy in, for a support bundle. Null leaves it out.</summary>
    public string? BackupFolder { get; init; }

    /// <summary>The robot's event log as CSV text, for a support bundle. Null leaves it out.</summary>
    public string? EventLogCsv { get; init; }

    /// <summary>Zip the folder when done (the folder is kept as well).</summary>
    public bool Zip { get; init; } = true;
}
