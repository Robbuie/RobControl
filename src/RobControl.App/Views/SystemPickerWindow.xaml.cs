using System.Windows;
using System.Windows.Input;
using RobControl.App.Composition;
using RobControl.App.Diagnostics;
using RobControl.App.ViewModels;

namespace RobControl.App.Views;

/// <summary>
/// The first window: pick or make a site and a system. Everything it decides is in
/// <see cref="SystemPickerViewModel"/>; this only closes the window when the view model says so.
/// </summary>
public partial class SystemPickerWindow : Window
{
    private readonly SystemPickerViewModel _viewModel;

    public SystemPickerWindow(SystemPickerViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;
        viewModel.Close = choice =>
        {
            Choice = choice;
            DialogResult = choice is not null;
        };
    }

    /// <summary>What was chosen; null when cancelled.</summary>
    public SystemChoice? Choice { get; private set; }

    private void OnCancel(object sender, RoutedEventArgs e) => _viewModel.Cancel();

    /// <summary>Backups from a folder, with no site: modal here, so the picker is still there when it closes.</summary>
    private void OnOpenLoose(object sender, RoutedEventArgs e) => LooseWindow.Open(this, BuildInfo.Stamp())?.ShowDialog();

    private void OnSystemDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (_viewModel.OpenCommand.CanExecute(null))
        {
            _viewModel.OpenCommand.Execute(null);
        }
    }
}
