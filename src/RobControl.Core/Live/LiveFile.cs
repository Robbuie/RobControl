namespace RobControl.Core.Live;

/// <summary>How one diagnostic file came back.</summary>
public enum LiveFileStatus
{
    /// <summary>Read.</summary>
    Ok,

    /// <summary>The controller has no such file (404) - not every version or application makes every page.</summary>
    NotAvailable,

    /// <summary>The diagnostic-file resource is locked (401/403).</summary>
    Locked,

    /// <summary>Could not reach the web server, or it answered with something else.</summary>
    Failed,
}

/// <summary>One diagnostic file read for the Live tab, kept verbatim: layouts are not confirmed, so the text is always there to look at.</summary>
public sealed record LiveFile(string Title, string FileName, LiveFileStatus Status, string Text, string? Message)
{
    public bool IsOk => Status == LiveFileStatus.Ok;
}
