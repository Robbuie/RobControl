using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;
using RobControl.App.ViewModels;

namespace RobControl.App.Views;

/// <summary>
/// Backups opened from a folder, with no site. Everything it does is in <see cref="LooseViewModel"/>;
/// this wires the dialogs and brings the Changes tab forward after a compare.
/// </summary>
public partial class LooseWindow : Window
{
    private readonly LooseViewModel _viewModel;

    public LooseWindow(LooseViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;
        viewModel.ShowMessage = message => MessageBox.Show(this, message, "RobControl", MessageBoxButton.OK, MessageBoxImage.Information);
        viewModel.OpenFolder = folder => Shell.Open(this, folder);
        viewModel.ShowReport = (title, markdown) =>
        {
            DocumentWindow window = DocumentWindow.Report(title, markdown);
            window.Owner = this;
            window.Show();
        };
        viewModel.Search.PickSaveFile = PickSaveFile;
        viewModel.Search.ShowMessage = viewModel.ShowMessage;
        viewModel.Alarms.PickSaveFile = PickSaveFile;
        viewModel.Alarms.ShowMessage = viewModel.ShowMessage;
        viewModel.CompareOpened += (_, _) => ChangesTab.IsSelected = true;
        Closed += (_, _) =>
        {
            viewModel.Search.Cancel();
            viewModel.Alarms.Cancel();
            viewModel.Timeline.Cancel();
        };
    }

    /// <summary>Asks for a folder and opens it in a window of its own; a message and null when it holds no backups.</summary>
    public static LooseWindow? Open(Window? owner, string tool, string? startIn = null)
    {
        var dialog = new OpenFolderDialog { Title = "Open backups from a folder", Multiselect = false };
        if (startIn is not null && System.IO.Directory.Exists(startIn))
        {
            dialog.InitialDirectory = startIn;
        }

        bool? picked = owner is null ? dialog.ShowDialog() : dialog.ShowDialog(owner);
        if (picked != true)
        {
            return null;
        }

        try
        {
            var window = new LooseWindow(new LooseViewModel(Core.Backup.LooseArchive.Open(dialog.FolderName), tool));
            if (owner is not null)
            {
                window.Owner = owner;
            }

            return window;
        }
        catch (Exception ex) when (ex is Core.Backup.BackupArchiveException or System.IO.IOException or UnauthorizedAccessException)
        {
            if (owner is null)
            {
                MessageBox.Show(ex.Message, "RobControl", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                MessageBox.Show(owner, ex.Message, "RobControl", MessageBoxButton.OK, MessageBoxImage.Information);
            }

            return null;
        }
    }

    private string? PickSaveFile(string title, string suggested, string filter)
    {
        var dialog = new SaveFileDialog { Title = title, FileName = suggested, Filter = filter, AddExtension = true };
        return dialog.ShowDialog(this) == true ? dialog.FileName : null;
    }

    private void OnBackupDoubleClick(object sender, MouseButtonEventArgs e)
    {
        DependencyObject? node = e.OriginalSource as DependencyObject;
        while (node is not null and not DataGridRow)
        {
            node = System.Windows.Media.VisualTreeHelper.GetParent(node);
        }

        if (node is DataGridRow && sender is DataGrid { SelectedItem: BackupRowViewModel row })
        {
            _viewModel.OpenBackupFolderCommand.Execute(row);
        }
    }
}
