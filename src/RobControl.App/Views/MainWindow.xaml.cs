// UseWPF drops System.IO from the implicit usings; the Help menu touches Directory and IOException.
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;
using RobControl.App.Appearance;
using RobControl.App.Composition;
using RobControl.App.Diagnostics;
using RobControl.App.ViewModels;

namespace RobControl.App.Views;

/// <summary>
/// The shell. Almost nothing here: dialogs the view model asks for through delegates, and turning a
/// double-click into a call. Everything else is in <see cref="MainViewModel"/>.
/// </summary>
public partial class MainWindow : Window
{
    private const string SiteFileFilter = "RobControl site (*.robcontrol-site.json)|*.robcontrol-site.json|JSON (*.json)|*.json|All files (*.*)|*.*";

    private const string ImportFilter = "RobControl site or bundle (*.robcontrol-site.json;*.robcontrol-bundle.zip)|*.robcontrol-site.json;*.robcontrol-bundle.zip|"
        + "Site bundle (*.zip)|*.zip|JSON (*.json)|*.json|All files (*.*)|*.*";

    private bool _checkingForUpdates;
    private TrayIcon? _tray;
    private bool _exiting;
    private bool _toldAboutTray;
    private DocumentWindow? _readme;
    private DocumentWindow? _whatsNew;

    public MainWindow()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;

        // Logging off or shutting Windows down ends RobControl, tray or not.
        Application.Current.SessionEnding += (_, _) => _exiting = true;
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.F1)
            {
                OnReadme(this, e);
                e.Handled = true;
            }
        };
    }

    private MainViewModel? ViewModel => DataContext as MainViewModel;

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is MainViewModel old)
        {
            old.CompareOpened -= OnCompareOpened;
            old.BackgroundNotice -= OnBackgroundNotice;
            old.EditRobotDialog = null;
            old.Confirm = null;
            old.ShowMessage = null;
            old.PickFolder = null;
            old.OpenFolder = null;
            old.EditSiteDialog = null;
            old.EditSystemDialog = null;
            old.PickSystemDialog = null;
            old.ChooseBackupDialog = null;
            old.PickSiteFileToOpen = null;
            old.PickSiteFileToSave = null;
            old.Trend.PickSaveFile = null;
            old.Trend.BrowseDialog = null;
            old.Trend.ShowMessage = null;
            old.Fleet.PickSaveFile = null;
            old.Fleet.OpenFile = null;
            old.Fleet.ShowMessage = null;
            old.Fleet.Confirm = null;
            old.PickSaveFile = null;
            old.Alarms.PickSaveFile = null;
            old.Alarms.ShowMessage = null;
            old.Search.PickSaveFile = null;
            old.Search.ShowMessage = null;
            old.Fleet.RemoveFolder = null;
        }

        if (e.NewValue is MainViewModel viewModel)
        {
            viewModel.CompareOpened += OnCompareOpened;
            viewModel.BackgroundNotice += OnBackgroundNotice;
            viewModel.EditRobotDialog = ShowRobotDialog;
            viewModel.Confirm = question =>
                MessageBox.Show(this, question, "RobControl", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No)
                    == MessageBoxResult.Yes;
            viewModel.ShowMessage = message =>
                MessageBox.Show(this, message, "RobControl", MessageBoxButton.OK, MessageBoxImage.Information);
            viewModel.PickFolder = PickFolder;
            viewModel.OpenFolder = folder => Shell.Open(this, folder);
            viewModel.EditSiteDialog = ShowSiteDialog;
            viewModel.EditSystemDialog = ShowSystemDialog;
            viewModel.PickSystemDialog = ShowSystemPicker;
            viewModel.ChooseBackupDialog = choice => new BackupChoiceWindow(choice) { Owner = this }.ShowDialog() == true;
            viewModel.PickSiteFileToOpen = PickSiteFileToOpen;
            viewModel.PickSiteFileToSave = PickSiteFileToSave;
            viewModel.Trend.PickSaveFile = PickCsvFile;
            viewModel.Trend.BrowseDialog = browser => new SignalBrowserWindow(browser) { Owner = this }.ShowDialog() == true;
            viewModel.Trend.ShowMessage = viewModel.ShowMessage;
            viewModel.Fleet.PickSaveFile = PickSaveFile;
            viewModel.Fleet.OpenFile = path => Shell.Open(this, path);
            viewModel.Fleet.ShowMessage = viewModel.ShowMessage;
            viewModel.Fleet.Confirm = viewModel.Confirm;
            viewModel.PickSaveFile = PickSaveFile;
            viewModel.Alarms.PickSaveFile = PickSaveFile;
            viewModel.Alarms.ShowMessage = viewModel.ShowMessage;
            viewModel.Search.PickSaveFile = PickSaveFile;
            viewModel.Search.ShowMessage = viewModel.ShowMessage;

            // To the Recycle Bin, not gone: a prune of the wrong site can be put back from Explorer.
            viewModel.Fleet.RemoveFolder = folder => Microsoft.VisualBasic.FileIO.FileSystem.DeleteDirectory(
                folder,
                Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs,
                Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
        }
    }

    private RobotDraft? ShowRobotDialog(RobotDraft draft)
    {
        var dialog = new RobotWindow(draft) { Owner = this };
        return dialog.ShowDialog() == true ? dialog.Result : null;
    }

    private SiteDraft? ShowSiteDialog(SiteDraft draft, bool isNew)
    {
        var dialog = new SiteWindow(draft, isNew) { Owner = this };
        return dialog.ShowDialog() == true ? dialog.Result : null;
    }

    private SystemDraft? ShowSystemDialog(SystemDraft draft)
    {
        var dialog = new SystemWindow(draft) { Owner = this };
        return dialog.ShowDialog() == true ? dialog.Result : null;
    }

    private SystemChoice? ShowSystemPicker(SystemPickerViewModel picker)
    {
        var dialog = new SystemPickerWindow(picker) { Owner = this, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        return dialog.ShowDialog() == true ? dialog.Choice : null;
    }

    private string? PickSiteFileToOpen()
    {
        var dialog = new OpenFileDialog { Title = "Import site", Filter = ImportFilter, CheckFileExists = true };
        return dialog.ShowDialog(this) == true ? dialog.FileName : null;
    }

    private string? PickSiteFileToSave(string suggested)
    {
        var dialog = new SaveFileDialog
        {
            Title = "Export site",
            FileName = suggested,
            Filter = SiteFileFilter,
            DefaultExt = ".json",
            AddExtension = true,
        };

        return dialog.ShowDialog(this) == true ? dialog.FileName : null;
    }

    private string? PickFolder(string current)
    {
        var dialog = new OpenFolderDialog { Title = "Archive folder for robot backups", Multiselect = false };
        if (Directory.Exists(current))
        {
            dialog.InitialDirectory = current;
        }

        return dialog.ShowDialog(this) == true ? dialog.FolderName : null;
    }

    private string? PickSaveFile(string title, string suggested, string filter)
    {
        var dialog = new SaveFileDialog { Title = title, FileName = suggested, Filter = filter, AddExtension = true };
        return dialog.ShowDialog(this) == true ? dialog.FileName : null;
    }

    private string? PickCsvFile(string suggested)
    {
        var dialog = new SaveFileDialog
        {
            Title = "Export trend",
            FileName = suggested,
            Filter = "CSV (*.csv)|*.csv|All files (*.*)|*.*",
            DefaultExt = ".csv",
        };

        return dialog.ShowDialog(this) == true ? dialog.FileName : null;
    }

    private void OnCompareOpened(object? sender, EventArgs e) => ChangesTab.IsSelected = true;

    /// <summary>
    /// The list allows Ctrl/Shift multi-select. SelectedItems cannot be bound, so the view hands the
    /// selection over here; Probe, Back up and the Trends tab then act on every selected robot.
    /// </summary>
    private void OnRobotSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is DataGrid grid && ViewModel is { } viewModel)
        {
            viewModel.SetSelection(grid.SelectedItems.OfType<RobotRowViewModel>());
        }
    }

    private void OnRobotDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (ViewModel?.EditRobotCommand.CanExecute(null) == true && IsOnRow(e))
        {
            ViewModel.EditRobotCommand.Execute(null);
        }
    }

    private void OnBackupDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is DataGrid { SelectedItem: BackupRowViewModel row } && IsOnRow(e))
        {
            ViewModel?.OpenBackupFolderCommand.Execute(row);
        }
    }

    /// <summary>A double-click on a header or the empty area below the rows is not a request.</summary>
    private static bool IsOnRow(MouseButtonEventArgs e)
    {
        DependencyObject? node = e.OriginalSource as DependencyObject;
        while (node is not null and not DataGridRow)
        {
            node = System.Windows.Media.VisualTreeHelper.GetParent(node);
        }

        return node is DataGridRow;
    }

    /// <summary>Help > Read me (F1). Not modal: people read it beside the window it describes.</summary>
    private void OnReadme(object sender, RoutedEventArgs e) =>
        ShowDocument(ref _readme, "RobControl - Read me", HelpDocuments.Readme, HelpDocuments.ReadmeOnline, appSectionOnly: true);

    private void OnWhatsNew(object sender, RoutedEventArgs e) =>
        ShowDocument(ref _whatsNew, "RobControl - What's new", HelpDocuments.Changelog, HelpDocuments.ChangelogOnline, appSectionOnly: false);

    /// <summary>One window per document: asking again brings the open one forward rather than stacking copies.</summary>
    private void ShowDocument(ref DocumentWindow? window, string title, string markdown, string online, bool appSectionOnly)
    {
        if (window is { IsLoaded: true })
        {
            window.Activate();
            return;
        }

        window = new DocumentWindow(title, markdown, online, appSectionOnly) { Owner = this };
        window.Show();
    }

    private void OnAppearance(object sender, RoutedEventArgs e) =>
        new AppearanceWindow { Owner = this }.ShowDialog();

    private async void OnCheckForUpdates(object sender, RoutedEventArgs e)
    {
        if (_checkingForUpdates)
        {
            return;
        }

        _checkingForUpdates = true;
        try
        {
            AppSettings settings = AppSettings.Load(AppPaths.SettingsFile);
            UpdateResult result = await UpdateCheck.RunAsync(settings, BuildInfo.Version);

            if (ViewModel is { } viewModel)
            {
                viewModel.UpdateStatus = result.IsUpdateAvailable ? result.StatusText : null;
            }

            if (result.IsUpdateAvailable)
            {
                ShowUpdateDialog(result);
                return;
            }

            string message = result.Availability switch
            {
                UpdateAvailability.Current => $"{BuildInfo.Version} is the newest published build.",
                UpdateAvailability.TurnedOff =>
                    "The update check is switched off on this machine, so nothing was contacted."
                        + Environment.NewLine + Environment.NewLine
                        + $"It is \"checkForUpdates\": false in {AppPaths.SettingsFile}.",
                _ => $"Could not check: {result.Problem}"
                        + Environment.NewLine + Environment.NewLine
                        + "That is the usual answer on a network with no route out, and says nothing about whether a newer build exists.",
            };

            MessageBox.Show(this, message, "Check for updates", MessageBoxButton.OK,
                result.Availability == UpdateAvailability.Failed ? MessageBoxImage.Warning : MessageBoxImage.Information);
        }
        finally
        {
            _checkingForUpdates = false;
        }
    }

    private void OnUpdateStatusClicked(object sender, MouseButtonEventArgs e)
    {
        OnCheckForUpdates(sender, e);
        e.Handled = true;
    }

    private void ShowUpdateDialog(UpdateResult result)
    {
        var dialog = new UpdateWindow(result, InstallLocation.Describe(BuildInfo.ExecutablePath), BlockedReason())
        {
            Owner = this,
        };

        dialog.ShowDialog();

        if (dialog.StatusAfterClose is { } status && ViewModel is { } viewModel)
        {
            viewModel.UpdateStatus = status;
        }

        if (dialog.ShouldShutdown)
        {
            _exiting = true;
            Application.Current.Shutdown();
        }
    }

    /// <summary>
    /// Why this is not a moment to replace the application. A backup in flight would be cut off and
    /// left marked INCOMPLETE - not dangerous, but not something to do to somebody by surprise.
    /// </summary>
    private string? BlockedReason() =>
        ViewModel is { IsBusy: true }
            ? "A probe or backup is running. Updating closes RobControl, and anything cut short is left marked "
                + "INCOMPLETE. Let it finish, or press Stop first."
            : null;

    private void OnOpenReleases(object sender, RoutedEventArgs e) => Shell.Open(this, BuildInfo.ReleasesPage);

    private void OnOpenLogFolder(object sender, RoutedEventArgs e)
    {
        try
        {
            Directory.CreateDirectory(AppPaths.Logs);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            MessageBox.Show(this, $"{AppPaths.Logs} could not be opened: {ex.Message}", "Diagnostic log",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        Shell.Open(this, AppPaths.Logs);
    }

    private void OnAbout(object sender, RoutedEventArgs e) =>
        MessageBox.Show(
            this,
            BuildInfo.Describe()
                + Environment.NewLine + Environment.NewLine
                + (BuildInfo.ExecutablePath is { } exe ? exe + Environment.NewLine : string.Empty)
                + AppPaths.Data
                + Environment.NewLine + Environment.NewLine
                + BuildInfo.ReleasesPage
                + Environment.NewLine + Environment.NewLine
                + "Read-only by design: RobControl copies files off FANUC controllers and reads their state. "
                + "It sends no command that writes, moves or starts anything.",
            "About RobControl",
            MessageBoxButton.OK,
            MessageBoxImage.Information);

    private void OnExit(object sender, RoutedEventArgs e) => ExitForReal();

    // ------------------------------------------------------------------ notification area

    /// <summary>
    /// Closing the window while RobControl is doing something on its own - a schedule, trend recording,
    /// a backup in progress - hides it to the notification area instead of ending the process, which
    /// would quietly stop all of them. Robots &gt; Exit, or Exit on the icon's menu, really exits.
    /// </summary>
    protected override void OnClosing(CancelEventArgs e)
    {
        base.OnClosing(e);
        if (e.Cancel || _exiting || ViewModel?.BackgroundWork is not { } work)
        {
            return;
        }

        try
        {
            _tray ??= CreateTray();
        }
        catch (InvalidOperationException)
        {
            // No notification area to go to (a locked-down shell): close as before.
            return;
        }

        e.Cancel = true;
        _tray.Text = "RobControl - " + work;
        _tray.Show();
        Hide();
        if (!_toldAboutTray)
        {
            _toldAboutTray = true;
            _tray.Notify("RobControl is still running", work + " Double-click the icon to open it; right-click > Exit to stop.");
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        _tray?.Dispose();
        _tray = null;
        base.OnClosed(e);
    }

    private void OnBackgroundNotice(object? sender, string message)
    {
        if (_tray is { IsVisible: true } tray)
        {
            tray.Notify("RobControl", message);
            if (ViewModel?.BackgroundWork is { } work)
            {
                tray.Text = "RobControl - " + work;
            }
        }
    }

    private TrayIcon CreateTray()
    {
        var tray = new TrayIcon("RobControl");
        tray.OpenRequested += (_, _) => RestoreFromTray();
        tray.ExitRequested += (_, _) => ExitForReal();
        return tray;
    }

    private void RestoreFromTray()
    {
        _tray?.Hide();
        Show();
        if (WindowState == WindowState.Minimized)
        {
            WindowState = WindowState.Normal;
        }

        Activate();
    }

    private void ExitForReal()
    {
        _exiting = true;
        Close();
    }
}
