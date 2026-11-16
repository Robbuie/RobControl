using System.Windows.Controls;

namespace RobControl.App.Views;

/// <summary>The search view, shared by the main window and the window on backups from a folder. All logic is in <see cref="ViewModels.SearchViewModel"/>.</summary>
public partial class SearchPanel : UserControl
{
    public SearchPanel() => InitializeComponent();
}
