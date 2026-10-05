namespace RobControl.Core.Transports.Http;

/// <summary>The web server could not be reached, or answered with something other than the file.</summary>
public sealed class ControllerHttpException : RobControlException
{
    public ControllerHttpException(string message, int? statusCode = null, Exception? inner = null)
        : base(message, inner)
    {
        StatusCode = statusCode;
    }

    public int? StatusCode { get; }
}
