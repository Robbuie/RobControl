using System.Windows.Controls;

namespace RobControl.App.Views;

/// <summary>The changes view, shared by the main window and the window on backups from a folder. All logic is in <see cref="ViewModels.CompareViewModel"/>.</summary>
public partial class ChangesPanel : UserControl
{
    public ChangesPanel() => InitializeComponent();
}
