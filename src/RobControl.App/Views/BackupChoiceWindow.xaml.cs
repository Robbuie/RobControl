using System.Windows;
using RobControl.App.ViewModels;

namespace RobControl.App.Views;

/// <summary>Which devices this backup copies. The checking is <see cref="BackupChoiceViewModel.TryBuild"/>.</summary>
public partial class BackupChoiceWindow : Window
{
    private readonly BackupChoiceViewModel _viewModel;

    public BackupChoiceWindow(BackupChoiceViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;
    }

    private void OnOk(object sender, RoutedEventArgs e)
    {
        if (_viewModel.TryBuild(out _))
        {
            DialogResult = true;
        }
    }
}
