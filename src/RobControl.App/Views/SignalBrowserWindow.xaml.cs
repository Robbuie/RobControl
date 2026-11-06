using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using RobControl.App.ViewModels;

namespace RobControl.App.Views;

/// <summary>
/// The Trends tab's signal browser. Everything it decides is in <see cref="SignalBrowserViewModel"/>;
/// here only the keyboard: Space ticks the selected rows, a double-click ticks the row under it.
/// </summary>
public partial class SignalBrowserWindow : Window
{
    private readonly SignalBrowserViewModel _viewModel;

    public SignalBrowserWindow(SignalBrowserViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;
        Loaded += (_, _) => FilterBox.Focus();
    }

    private void OnAdd(object sender, RoutedEventArgs e)
    {
        if (_viewModel.CanAdd)
        {
            DialogResult = true;
        }
    }

    // Space on a selection flips it as a block: all ticked if any was not, otherwise all cleared.
    private void OnGridKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Space || sender is not DataGrid grid || grid.SelectedItems.Count == 0)
        {
            return;
        }

        List<SignalBrowserRowViewModel> rows = [.. grid.SelectedItems.OfType<SignalBrowserRowViewModel>()];
        bool tick = rows.Any(r => !r.IsChecked);
        foreach (SignalBrowserRowViewModel row in rows)
        {
            row.IsChecked = tick;
        }

        e.Handled = true;
    }

    private void OnGridDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is FrameworkElement { DataContext: SignalBrowserRowViewModel row })
        {
            row.IsChecked = !row.IsChecked;
            e.Handled = true;
        }
    }
}
