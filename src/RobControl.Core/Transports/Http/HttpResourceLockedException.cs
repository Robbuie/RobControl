namespace RobControl.Core.Transports.Http;

/// <summary>
/// The web server answered and refused: the resource is locked or wants a password.
///
/// <para>RobControl never changes a controller's Host Comm settings to get round this. It says where
/// the setting is, and somebody who is allowed to decides.</para>
/// </summary>
public sealed class HttpResourceLockedException : RobControlException
{
    public HttpResourceLockedException(HttpResourceKind resource, int statusCode, string message)
        : base(message)
    {
        Resource = resource;
        StatusCode = statusCode;
        Remediation = $"On the pendant: Menu > Setup > Host Comm > HTTP. The {Name(resource)} resource is locked "
            + "or password-protected on this controller. Changing it is a site decision - RobControl will not.";
    }

    public HttpResourceKind Resource { get; }

    public int StatusCode { get; }

    internal static string Name(HttpResourceKind resource) => resource switch
    {
        HttpResourceKind.Kcl => "KCL",
        HttpResourceKind.Karel => "KAREL",
        _ => "diagnostic files",
    };
}
