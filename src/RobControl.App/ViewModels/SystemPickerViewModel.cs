using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RobControl.App.Composition;
using RobControl.Core;
using RobControl.Core.Sites;
using RobControl.Core.Systems;

namespace RobControl.App.ViewModels;

/// <summary>One site in the picker's left-hand list.</summary>
public sealed class SitePickRow(SitePick site)
{
    public SitePick Site { get; } = site;

    public string Key => Site.Key;

    public string Name => Site.Name;

    public string Detail => Site.Systems.Count switch
    {
        0 => "No systems yet - opening it makes \"Main\" with its robots",
        1 => "1 system",
        int n => string.Create(CultureInfo.CurrentCulture, $"{n} systems"),
    };
}

/// <summary>One system in the picker's right-hand list.</summary>
public sealed class SystemPickRow(SystemPick system)
{
    public SystemPick System { get; } = system;

    public string Key => System.Key;

    public string Name => System.Name;

    public string Detail => (System.RobotCount == 1 ? "1 robot" : string.Create(CultureInfo.CurrentCulture, $"{System.RobotCount} robots"))
        + (string.IsNullOrWhiteSpace(System.Notes) ? string.Empty : " - " + System.Notes.Split('\n')[0].Trim());
}

/// <summary>
/// The window RobControl opens with: pick a site, pick a system in it - or make either - and open it.
/// Also behind System > Switch system. Free of WPF types; the window sets <see cref="Close"/>.
/// </summary>
public sealed class SystemPickerViewModel : ObservableObject
{
    private readonly ISystemDirectory _directory;
    private SitePickRow? _site;
    private SystemPickRow? _system;
    private string _newSystemName = string.Empty;
    private string _newSiteName = string.Empty;
    private string? _problem;

    /// <param name="current">What to start on: the system open now, or the one used last.</param>
    public SystemPickerViewModel(ISystemDirectory directory, SystemChoice? current, bool isStartup)
    {
        _directory = directory ?? throw new ArgumentNullException(nameof(directory));
        IsStartup = isStartup;
        OpenCommand = new RelayCommand(Open, () => SelectedSite is not null);
        CreateSystemCommand = new RelayCommand(CreateSystem, () => SelectedSite is not null && NewSystemName.Trim().Length > 0);
        CreateSiteCommand = new RelayCommand(CreateSite, () => NewSiteName.Trim().Length > 0);
        Reload(current);
    }

    /// <summary>Called with the choice when the person opens one; with null when they cancel.</summary>
    public Action<SystemChoice?>? Close { get; set; }

    /// <summary>The first window of the session: Cancel quits rather than going back.</summary>
    public bool IsStartup { get; }

    public string CancelText => IsStartup ? "Quit" : "Cancel";

    public string ConfigFolder => _directory.SystemsFolder;

    public ObservableCollection<SitePickRow> Sites { get; } = [];

    public ObservableCollection<SystemPickRow> Systems { get; } = [];

    public SitePickRow? SelectedSite
    {
        get => _site;
        set
        {
            if (SetProperty(ref _site, value))
            {
                FillSystems(null);
                RaiseCanExecute();
            }
        }
    }

    public SystemPickRow? SelectedSystem
    {
        get => _system;
        set => SetProperty(ref _system, value);
    }

    public string NewSystemName
    {
        get => _newSystemName;
        set
        {
            if (SetProperty(ref _newSystemName, value ?? string.Empty))
            {
                RaiseCanExecute();
            }
        }
    }

    public string NewSiteName
    {
        get => _newSiteName;
        set
        {
            if (SetProperty(ref _newSiteName, value ?? string.Empty))
            {
                RaiseCanExecute();
            }
        }
    }

    public string? Problem
    {
        get => _problem;
        private set => SetProperty(ref _problem, value);
    }

    public IRelayCommand OpenCommand { get; }

    public IRelayCommand CreateSystemCommand { get; }

    public IRelayCommand CreateSiteCommand { get; }

    /// <summary>What Open would open now.</summary>
    public SystemChoice? Choice => SelectedSite is null ? null : new SystemChoice(SelectedSite.Key, SelectedSystem?.Key);

    public void Cancel() => Close?.Invoke(null);

    private void Open()
    {
        if (Choice is { } choice)
        {
            Close?.Invoke(choice);
        }
    }

    private void CreateSystem()
    {
        if (SelectedSite is not { } site)
        {
            return;
        }

        try
        {
            RobotSystem made = _directory.CreateSystem(site.Key, new SystemSettings { Name = NewSystemName.Trim() });
            NewSystemName = string.Empty;
            Problem = null;
            Reload(new SystemChoice(site.Key, made.Key));
        }
        catch (RobControlException ex)
        {
            Problem = ex.Message;
        }
    }

    private void CreateSite()
    {
        try
        {
            Site made = _directory.Create(new SiteSettings { Name = NewSiteName.Trim() });
            NewSiteName = string.Empty;
            Problem = null;
            Reload(new SystemChoice(made.Key, null));
        }
        catch (RobControlException ex)
        {
            Problem = ex.Message;
        }
    }

    private void Reload(SystemChoice? select)
    {
        Sites.Clear();
        foreach (SitePick site in _directory.Directory())
        {
            Sites.Add(new SitePickRow(site));
        }

        _site = Sites.FirstOrDefault(s => string.Equals(s.Key, select?.SiteKey, StringComparison.OrdinalIgnoreCase)) ?? Sites.FirstOrDefault();
        OnPropertyChanged(nameof(SelectedSite));
        FillSystems(select?.SystemKey);
        RaiseCanExecute();
    }

    private void FillSystems(string? select)
    {
        Systems.Clear();
        foreach (SystemPick system in _site?.Site.Systems ?? [])
        {
            Systems.Add(new SystemPickRow(system));
        }

        SelectedSystem = Systems.FirstOrDefault(s => string.Equals(s.Key, select, StringComparison.OrdinalIgnoreCase)) ?? Systems.FirstOrDefault();
    }

    private void RaiseCanExecute()
    {
        OpenCommand.NotifyCanExecuteChanged();
        CreateSystemCommand.NotifyCanExecuteChanged();
        CreateSiteCommand.NotifyCanExecuteChanged();
    }
}
