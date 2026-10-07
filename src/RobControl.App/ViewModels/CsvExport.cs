// UseWPF drops System.IO from the implicit usings.
using System.IO;
using RobControl.Core.Backup;
using RobControl.Core.Insight;

namespace RobControl.App.ViewModels;

/// <summary>Asks where, writes the CSV, and says what happened - the same way for every tab.</summary>
internal static class CsvExport
{
    /// <summary>
    /// Writes <paramref name="csv"/> where the person picks. Returns the status line to show, or null
    /// when they cancelled. A file that cannot be written goes to <paramref name="showMessage"/>.
    /// </summary>
    public static string? Save(
        Func<string, string, string, string?>? pickSaveFile,
        Action<string>? showMessage,
        string title,
        string siteName,
        string what,
        Func<string> csv,
        string done)
    {
        string suggested = $"{ArchiveNames.RobotFolder(siteName)} {what} {DateTime.Now:yyyy-MM-dd}.csv";
        if (pickSaveFile?.Invoke(title, suggested, "CSV (*.csv)|*.csv") is not { } path)
        {
            return null;
        }

        try
        {
            File.WriteAllText(path, csv(), Csv.Encoding);
            return $"{done} {path}.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            showMessage?.Invoke($"{path} could not be written: {ex.Message}");
            return null;
        }
    }
}
