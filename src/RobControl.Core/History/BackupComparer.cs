using System.Text;
using RobControl.Core.Backup;

namespace RobControl.Core.History;

/// <summary>
/// What changed between two backups of a robot: which files, and for text files, which lines.
/// Equality is by SHA-256 from the manifests - no file is opened to decide whether it changed.
/// </summary>
public static class BackupComparer
{
    public static IReadOnlyList<FileComparison> Compare(BackupSet older, BackupSet newer)
    {
        ArgumentNullException.ThrowIfNull(older);
        ArgumentNullException.ThrowIfNull(newer);

        Dictionary<string, BackupFileRecord> a = older.Manifest.Files.ToDictionary(f => f.RelativePath, StringComparer.OrdinalIgnoreCase);
        Dictionary<string, BackupFileRecord> b = newer.Manifest.Files.ToDictionary(f => f.RelativePath, StringComparer.OrdinalIgnoreCase);

        var result = new List<FileComparison>();
        foreach (string path in a.Keys.Union(b.Keys, StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase))
        {
            a.TryGetValue(path, out BackupFileRecord? oldFile);
            b.TryGetValue(path, out BackupFileRecord? newFile);

            FileChange change = (oldFile, newFile) switch
            {
                (null, _) => FileChange.Added,
                (_, null) => FileChange.Removed,
                _ when string.Equals(oldFile.Sha256, newFile.Sha256, StringComparison.OrdinalIgnoreCase) => FileChange.Same,
                _ => FileChange.Changed,
            };

            bool canDiff = change == FileChange.Changed
                && TextSniffer.LooksLikeText(older.PathOf(oldFile!))
                && TextSniffer.LooksLikeText(newer.PathOf(newFile!));

            result.Add(new FileComparison(path, change, oldFile, newFile) { CanDiff = canDiff });
        }

        return result;
    }

    /// <summary>The line diff of one file present in both backups.</summary>
    public static IReadOnlyList<DiffLine> DiffFile(BackupSet older, BackupSet newer, FileComparison file)
    {
        ArgumentNullException.ThrowIfNull(older);
        ArgumentNullException.ThrowIfNull(newer);
        ArgumentNullException.ThrowIfNull(file);

        IReadOnlyList<string> a = file.Old is null ? [] : ReadLines(older.PathOf(file.Old));
        IReadOnlyList<string> b = file.New is null ? [] : ReadLines(newer.PathOf(file.New));
        return LineDiff.Compute(a, b);
    }

    /// <summary>Latin-1, for the reason the transports use it: every byte is a character, nothing fails.</summary>
    public static IReadOnlyList<string> ReadLines(string path)
    {
        string text = File.ReadAllText(path, Encoding.Latin1);
        string[] lines = text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        return lines.Length > 0 && lines[^1].Length == 0 ? lines[..^1] : lines;
    }
}
