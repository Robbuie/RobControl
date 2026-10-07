using System.Windows;
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

    /// <param name="markdown">The document.</param>
    /// <param name="online">The same document on GitHub, for "Open on GitHub".</param>
    /// <param name="appSectionOnly">True for the README: stop where the part for developers starts.</param>
    public DocumentWindow(string title, string markdown, string online, bool appSectionOnly)
    {
        InitializeComponent();
        _online = online;
        Title = title;
        SourceText.Text = $"From RobControl {BuildInfo.Version}";

        IReadOnlyList<MarkdownBlock> blocks = MarkdownLite.Parse(markdown, appSectionOnly);
        Viewer.Document = MarkdownRenderer.Render(
            blocks,
            url => MarkdownLite.ResolveLink(url, HelpDocuments.RepositoryBase),
            uri => Shell.Open(this, uri.AbsoluteUri));
    }

    private void OnOpenOnline(object sender, RoutedEventArgs e) => Shell.Open(this, _online);

    private void OnClose(object sender, RoutedEventArgs e) => Close();
}
