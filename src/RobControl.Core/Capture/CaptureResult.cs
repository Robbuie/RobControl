using RobControl.Core.Controllers;

namespace RobControl.Core.Capture;

/// <summary>What a capture left on disk and what it found.</summary>
public sealed record CaptureResult(
    string Folder,
    string? ZipPath,
    ControllerIdentity Identity,
    IReadOnlyList<CapturePage> Pages,
    string FtpSummary,
    string Report)
{
    /// <summary>"31 pages read, 3 not on this controller; FTP: ..." - the status line and the event row.</summary>
    public string Summary
    {
        get
        {
            int ok = Pages.Count(p => p.IsOk);
            int missing = Pages.Count(p => p.StatusCode == 404);
            int failed = Pages.Count - ok - missing;
            string rest = (missing > 0 ? $", {missing} not on this controller" : string.Empty)
                + (failed > 0 ? $", {failed} failed" : string.Empty);
            return $"{ok} pages read{rest}; FTP: {FtpSummary}";
        }
    }
}
