using System.Windows;
using RobControl.App.ViewModels;

namespace RobControl.App.Views;

/// <summary>
/// Add or edit one robot. Validation is <see cref="RobotDraft.TryBuild"/>, so the dialog and the
/// view model cannot disagree about what a valid robot is; the dialog only shows the sentence.
/// </summary>
public partial class RobotWindow : Window
{
    private readonly RobotDraft _draft;

    /// <param name="draft">A draft with no Id adds a robot - pre-filled with the site's FTP login; one with an Id edits it.</param>
    public RobotWindow(RobotDraft draft)
    {
        ArgumentNullException.ThrowIfNull(draft);
        InitializeComponent();
        _draft = draft;
        bool adding = draft.Id == 0;
        Title = adding ? "Add robot" : $"Edit {draft.Name}";
        NameBox.Text = draft.Name;
        AddressBox.Text = draft.Address;
        LineBox.Text = draft.Line;
        NotesBox.Text = draft.Notes;
        UserBox.Text = draft.FtpUser;
        PasswordInput.Password = draft.FtpPassword;
        FtpPortBox.Text = draft.FtpPort;
        HttpPortBox.Text = draft.HttpPort;
        DevicesBox.Text = draft.Backup.Devices;
        SubfoldersBox.IsChecked = draft.Backup.IncludeSubfolders;
        ExcludeBox.Text = draft.Backup.Exclude.Replace(Environment.NewLine, ", ", StringComparison.Ordinal);
        PauseBox.Text = draft.Backup.PauseMs;
        BackupHint.Text = draft.SystemName is null
            ? "This robot is not in a system, so it is backed up with the defaults (MD:). Move it into a system to give it settings of its own."
            : $"Anything left blank or unticked uses system '{draft.SystemName}': {draft.SystemDevices}. Back up still asks which devices each time.";
        bool inSystem = draft.SystemName is not null;
        DevicesBox.IsEnabled = inSystem;
        SubfoldersBox.IsEnabled = inSystem;
        ExcludeBox.IsEnabled = inSystem;
        PauseBox.IsEnabled = inSystem;
        Loaded += (_, _) => (adding ? NameBox : AddressBox).Focus();
    }

    public RobotDraft? Result { get; private set; }

    private void OnOk(object sender, RoutedEventArgs e)
    {
        RobotDraft draft = _draft with
        {
            Name = NameBox.Text,
            Address = AddressBox.Text,
            Line = LineBox.Text,
            Notes = NotesBox.Text,
            FtpUser = UserBox.Text,
            FtpPassword = PasswordInput.Password,
            FtpPort = FtpPortBox.Text,
            HttpPort = HttpPortBox.Text,
            Backup = _draft.Backup with
            {
                Devices = DevicesBox.Text,
                IncludeSubfolders = SubfoldersBox.IsChecked,
                Exclude = ExcludeBox.Text,
                PauseMs = PauseBox.Text,
            },
        };

        if (!draft.TryBuild(out _, out string? problem) || !draft.Backup.TryBuild(out _, out problem))
        {
            ProblemText.Text = problem;
            ProblemText.Visibility = Visibility.Visible;
            return;
        }

        Result = draft;
        DialogResult = true;
    }
}
