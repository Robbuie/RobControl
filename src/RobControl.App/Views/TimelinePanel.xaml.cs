using System.Windows.Controls;

namespace RobControl.App.Views;

/// <summary>The cell timeline, shared by the main window and the window on backups from a folder. All logic is in <see cref="ViewModels.AlarmTimelineViewModel"/>.</summary>
public partial class TimelinePanel : UserControl
{
    public TimelinePanel() => InitializeComponent();
}
