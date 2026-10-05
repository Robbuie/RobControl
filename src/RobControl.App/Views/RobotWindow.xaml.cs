using System.Windows;
using RobControl.App.ViewModels;

namespace RobControl.App.Views;

/// <summary>
/// Add or edit one robot. Validation is <see cref="RobotDraft.TryBuild"/>, so the dialog and the
/// view model cannot disagree about what a valid robot is; the dialog only shows the sentence.
/// </summary>
public partial class RobotWindow : Window
{
    private readonly long _id;

    public RobotWindow(RobotDraft? existing)
    {
        InitializeComponent();
        RobotDraft draft = existing ?? new RobotDraft();
        _id = draft.Id;
        Title = existing is null ? "Add robot" : $"Edit {existing.Name}";
        NameBox.Text = draft.Name;
        AddressBox.Text = draft.Address;
        LineBox.Text = draft.Line;
        NotesBox.Text = draft.Notes;
        UserBox.Text = draft.FtpUser;
        PasswordInput.Password = draft.FtpPassword;
        FtpPortBox.Text = draft.FtpPort;
        HttpPortBox.Text = draft.HttpPort;
        Loaded += (_, _) => (existing is null ? NameBox : AddressBox).Focus();
    }

    public RobotDraft? Result { get; private set; }

    private void OnOk(object sender, RoutedEventArgs e)
    {
        var draft = new RobotDraft
        {
            Id = _id,
            Name = NameBox.Text,
            Address = AddressBox.Text,
            Line = LineBox.Text,
            Notes = NotesBox.Text,
            FtpUser = UserBox.Text,
            FtpPassword = PasswordInput.Password,
            FtpPort = FtpPortBox.Text,
            HttpPort = HttpPortBox.Text,
        };

        if (!draft.TryBuild(out _, out string? problem))
        {
            ProblemText.Text = problem;
            ProblemText.Visibility = Visibility.Visible;
            return;
        }

        Result = draft;
        DialogResult = true;
    }
}
