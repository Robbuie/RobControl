// UseWPF drops System.IO from the implicit usings; the Help menu touches Directory and IOException.
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

    private bool _checkingForUpdates;
    private DocumentWindow? _readme;
    private DocumentWindow? _whatsNew;

    public MainWindow()
    {
        InitializeComponent();
        Chrome.SetTitleContent(this, Resources["TitleMenu"]);
        DataContextChanged += OnDataContextChanged;
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
            old.EditRobotDialog = null;
            old.Confirm = null;
            old.ShowMessage = null;
            old.PickFolder = null;
            old.OpenFolder = null;
            old.EditSiteDialog = null;
            old.PickSiteFileToOpen = null;
            old.PickSiteFileToSave = null;
            old.Trend.PickSaveFile = null;
            old.Trend.ShowMessage = null;
            old.Fleet.PickSaveFile = null;
            old.Fleet.OpenFile = null;
            old.Fleet.ShowMessage = null;
        }

        if (e.NewValue is MainViewModel viewModel)
        {
            viewModel.CompareOpened += OnCompareOpened;
            viewModel.EditRobotDialog = ShowRobotDialog;
            viewModel.Confirm = question =>
                MessageBox.Show(this, question, "RobControl", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No)
                    == MessageBoxResult.Yes;
            viewModel.ShowMessage = message =>
                MessageBox.Show(this, message, "RobControl", MessageBoxButton.OK, MessageBoxImage.Information);
            viewModel.PickFolder = PickFolder;
            viewModel.OpenFolder = folder => Shell.Open(this, folder);
            viewModel.EditSiteDialog = ShowSiteDialog;
            viewModel.PickSiteFileToOpen = PickSiteFileToOpen;
            viewModel.PickSiteFileToSave = PickSiteFileToSave;
            viewModel.Trend.PickSaveFile = PickCsvFile;
            viewModel.Trend.ShowMessage = viewModel.ShowMessage;
            viewModel.Fleet.PickSaveFile = PickSaveFile;
            viewModel.Fleet.OpenFile = path => Shell.Open(this, path);
            viewModel.Fleet.ShowMessage = viewModel.ShowMessage;
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

    private string? PickSiteFileToOpen()
    {
        var dialog = new OpenFileDialog { Title = "Import site", Filter = SiteFileFilter, CheckFileExists = true };
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

    private void OnExit(object sender, RoutedEventArgs e) => Close();
}
