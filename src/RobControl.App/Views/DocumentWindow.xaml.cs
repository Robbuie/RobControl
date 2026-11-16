using System.IO;
using System.Windows;
using Microsoft.Win32;
using RobControl.App.Composition;
using RobControl.App.Diagnostics;
using RobControl.Core.Help;

namespace RobControl.App.Views;

/// <summary>
/// Shows a Markdown document compiled into the build. Links leave the app only through
/// <see cref="MarkdownLite.ResolveLink"/> - http and https, never a file or a program.
/// </summary>
public partial class DocumentWindow : Window
{
    private readonly string _online;
    private readonly string _markdown;

    /// <param name="markdown">The document.</param>
    /// <param name="online">The same document on GitHub, for "Open on GitHub".</param>
    /// <param name="appSectionOnly">True for the README: stop where the part for developers starts.</param>
    public DocumentWindow(string title, string markdown, string online, bool appSectionOnly)
    {
        InitializeComponent();
        _online = online;
        _markdown = markdown;
        Title = title;
        SourceText.Text = $"From RobControl {BuildInfo.Version}";

        IReadOnlyList<MarkdownBlock> blocks = MarkdownLite.Parse(markdown, appSectionOnly);
        Viewer.Document = MarkdownRenderer.Render(
            blocks,
            url => MarkdownLite.ResolveLink(url, HelpDocuments.RepositoryBase),
            uri => Shell.Open(this, uri.AbsoluteUri));
    }

    /// <summary>A report RobControl wrote (What changed, a site visit, an alarm snapshot): no GitHub page, but Save as.</summary>
    public static DocumentWindow Report(string title, string markdown)
    {
        var window = new DocumentWindow(title, markdown, string.Empty, appSectionOnly: false);
        window.OnlineButton.Visibility = Visibility.Collapsed;
        window.SaveButton.Visibility = Visibility.Visible;
        window.SourceText.Text = $"Written by RobControl {BuildInfo.Version} at {DateTime.Now:yyyy-MM-dd HH:mm}";
        return window;
    }

    private void OnOpenOnline(object sender, RoutedEventArgs e) => Shell.Open(this, _online);

    private void OnSave(object sender, RoutedEventArgs e)
    {
        string name = string.Concat(Title.Split(Path.GetInvalidFileNameChars())).Trim();
        var dialog = new SaveFileDialog
        {
            Title = "Save report",
            FileName = (name.Length == 0 ? "RobControl report" : name) + ".md",
            Filter = "Markdown (*.md)|*.md|Text (*.txt)|*.txt",
            AddExtension = true,
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            File.WriteAllText(dialog.FileName, _markdown);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(this, $"Could not save: {ex.Message}", "RobControl", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void OnClose(object sender, RoutedEventArgs e) => Close();
}
