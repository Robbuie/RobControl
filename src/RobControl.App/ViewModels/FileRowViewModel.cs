using RobControl.Core.History;

namespace RobControl.App.ViewModels;

/// <summary>One file in a comparison.</summary>
public sealed class FileRowViewModel(FileComparison comparison)
{
    public FileComparison Comparison { get; } = comparison ?? throw new ArgumentNullException(nameof(comparison));

    public string Path => Comparison.RelativePath;

    public string Change => Comparison.Change.ToString();

    public string Note => Comparison.IsLiveData
        ? "live data"
        : Comparison.Change == FileChange.Changed && !Comparison.CanDiff ? "binary" : string.Empty;

    public string Size => Comparison.New is { } n ? Bytes.Describe(n.Bytes) : Comparison.Old is { } o ? Bytes.Describe(o.Bytes) : string.Empty;
}
