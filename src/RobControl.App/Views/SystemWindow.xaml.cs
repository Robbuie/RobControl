using System.Windows;
using RobControl.App.ViewModels;
using RobControl.Core.Systems;

namespace RobControl.App.Views;

/// <summary>
/// The open system's name, notes and backup settings. Validation is <see cref="SystemDraft.TryBuild"/>,
/// so the dialog and the view model cannot disagree; whether the name is free is the catalog's to say.
/// </summary>
public partial class SystemWindow : Window
{
    private readonly SystemDraft _draft;

    public SystemWindow(SystemDraft draft)
    {
        ArgumentNullException.ThrowIfNull(draft);
        InitializeComponent();
        _draft = draft;
        Title = $"System settings - {draft.Name}";
        NameBox.Text = draft.Name;
        NotesBox.Text = draft.Notes;
        DevicesBox.Text = draft.Backup.Devices;
        SubfoldersBox.IsChecked = draft.Backup.IncludeSubfolders ?? true;
        ExcludeBox.Text = draft.Backup.Exclude;
        PauseBox.Text = draft.Backup.PauseMs;
        AttemptsBox.Text = draft.Backup.Attempts;
        PathText.Text = draft.SettingsPath;
        Loaded += (_, _) => NameBox.Focus();
    }

    public SystemDraft? Result { get; private set; }

    private void OnOk(object sender, RoutedEventArgs e)
    {
        SystemDraft draft = _draft with
        {
            Name = NameBox.Text,
            Notes = NotesBox.Text,
            Backup = new BackupProfileDraft
            {
                Devices = DevicesBox.Text,
                IncludeSubfolders = SubfoldersBox.IsChecked == true,
                Exclude = ExcludeBox.Text,
                PauseMs = PauseBox.Text,
                Attempts = AttemptsBox.Text,
            },
        };

        if (!draft.TryBuild(new SystemSettings(), out _, out string? problem))
        {
            ProblemText.Text = problem;
            ProblemText.Visibility = Visibility.Visible;
            return;
        }

        Result = draft;
        DialogResult = true;
    }
}
