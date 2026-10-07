// UseWPF drops System.IO from the implicit usings; Browse checks Directory.
using System.IO;
using System.Windows;
using Microsoft.Win32;
using RobControl.App.ViewModels;
using RobControl.Core.Sites;

namespace RobControl.App.Views;

/// <summary>
/// New site, or the open site's settings. Validation is <see cref="SiteDraft.TryBuild"/>, so the
/// dialog and the view model cannot disagree; whether the name is free is the catalog's to say,
/// after OK.
/// </summary>
public partial class SiteWindow : Window
{
    public SiteWindow(SiteDraft draft, bool isNew)
    {
        ArgumentNullException.ThrowIfNull(draft);
        InitializeComponent();
        Title = isNew ? "New site" : $"Site settings - {draft.Name}";
        OkButton.Content = isNew ? "Create and switch" : "OK";
        NameBox.Text = draft.Name;
        ArchiveBox.Text = draft.ArchiveRoot;
        ConcurrencyBox.Text = draft.Concurrency;
        StaleBox.Text = draft.StaleAfterDays;
        RetriesBox.Text = draft.ScheduleRetries;
        KeepBox.Text = draft.KeepBackups;
        UserBox.Text = draft.DefaultFtpUser;
        PasswordInput.Password = draft.DefaultFtpPassword;
        NotesBox.Text = draft.Notes;
        Loaded += (_, _) => NameBox.Focus();
    }

    public SiteDraft? Result { get; private set; }

    private void OnBrowse(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "Archive folder for this site's backups", Multiselect = false };
        if (Directory.Exists(ArchiveBox.Text.Trim()))
        {
            dialog.InitialDirectory = ArchiveBox.Text.Trim();
        }

        if (dialog.ShowDialog(this) == true)
        {
            ArchiveBox.Text = dialog.FolderName;
        }
    }

    private void OnOk(object sender, RoutedEventArgs e)
    {
        var draft = new SiteDraft
        {
            Name = NameBox.Text,
            ArchiveRoot = ArchiveBox.Text,
            Concurrency = ConcurrencyBox.Text,
            StaleAfterDays = StaleBox.Text,
            ScheduleRetries = RetriesBox.Text,
            KeepBackups = KeepBox.Text,
            DefaultFtpUser = UserBox.Text,
            DefaultFtpPassword = PasswordInput.Password,
            Notes = NotesBox.Text,
        };

        if (!draft.TryBuild(new SiteSettings(), out _, out string? problem))
        {
            ProblemText.Text = problem;
            ProblemText.Visibility = Visibility.Visible;
            return;
        }

        Result = draft;
        DialogResult = true;
    }
}
