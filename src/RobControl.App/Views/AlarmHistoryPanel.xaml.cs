using System.Windows.Controls;

namespace RobControl.App.Views;

/// <summary>The alarmhistory view, shared by the main window and the window on backups from a folder. All logic is in <see cref="ViewModels.AlarmsViewModel"/>.</summary>
public partial class AlarmHistoryPanel : UserControl
{
    public AlarmHistoryPanel() => InitializeComponent();
}
