namespace RobControl.Core.Events;

/// <summary>What kind of thing an event row is about. Stored as the lower-cased member name.</summary>
public enum EventCategory
{
    /// <summary>The tool itself: started, settings changed, a robot added or removed.</summary>
    App,

    /// <summary>A capability probe: who answered, on what, and what they said they are.</summary>
    Probe,

    /// <summary>A backup started, finished, or failed.</summary>
    Backup,

    /// <summary>A KCL command sent, or refused before sending.</summary>
    Kcl,

    /// <summary>A diagnostic file fetched over HTTP.</summary>
    Http,
}
