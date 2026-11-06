using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using RobControl.Core.Backup;
using RobControl.Core.Robots;
using RobControl.Core.Systems;

namespace RobControl.App.ViewModels;

/// <summary>One device in the backup dialog: a tick box and a sentence about what is on it.</summary>
public sealed class DeviceChoice(string device, string description, bool isChecked) : ObservableObject
{
    private bool _isChecked = isChecked;

    public string Device { get; } = device;

    public string Description { get; } = description;

    public bool IsChecked
    {
        get => _isChecked;
        set => SetProperty(ref _isChecked, value);
    }
}

/// <summary>
/// The dialog that opens on every Back up: which devices this time, and whether to go into folders.
/// Ticked from what the robots' system (and the robot itself) say, so pressing Enter does what the
/// settings say; nothing chosen here is saved - that is what System settings and the robot dialog are for.
///
/// <para>Free of WPF types, like every view model here, so what it decides is testable.</para>
/// </summary>
public sealed class BackupChoiceViewModel : ObservableObject
{
    /// <summary>The devices offered with a tick box, and what is on each. Others can be typed.</summary>
    public static IReadOnlyList<(string Device, string Description)> KnownDevices { get; } =
    [
        ("MD:", "Programs, system variables and I/O config, with the readable .LS / .VA / .DG listings the controller writes as they are read. The one that matters."),
        ("FR:", "F-ROM: files kept on the controller - KAREL programs, user files, logs. Can be large."),
        ("FRA:", "The controller's own Auto Backup area: one folder per copy it has made. Large."),
        ("MC:", "Memory card in the controller - only if one is in."),
        ("UD1:", "USB stick in the controller cabinet - only if one is in."),
        ("UT1:", "USB stick in the teach pendant - only if one is in."),
        ("RD:", "RAM disk."),
    ];

    private readonly IReadOnlyList<(Robot Robot, BackupOptions Options)> _targets;
    private bool _includeSubfolders;
    private bool _useEachRobotsOwn;
    private string _otherDevices = string.Empty;
    private string? _problem;

    /// <param name="targets">Each robot to back up, with the options its settings resolve to.</param>
    public BackupChoiceViewModel(IReadOnlyList<(Robot Robot, BackupOptions Options)> targets)
    {
        ArgumentNullException.ThrowIfNull(targets);
        if (targets.Count == 0)
        {
            throw new ArgumentException("Nothing to back up.", nameof(targets));
        }

        _targets = targets;
        BackupOptions first = targets[0].Options;
        RobotsDiffer = targets.Any(t => !t.Options.Devices.SequenceEqual(first.Devices, StringComparer.OrdinalIgnoreCase)
            || t.Options.IncludeSubfolders != first.IncludeSubfolders);
        _useEachRobotsOwn = RobotsDiffer;

        // Ticked: what the first robot would take - or, when they differ, what any of them would.
        var ticked = new HashSet<string>(RobotsDiffer ? targets.SelectMany(t => t.Options.Devices) : first.Devices, StringComparer.OrdinalIgnoreCase);
        foreach ((string device, string description) in KnownDevices)
        {
            Devices.Add(new DeviceChoice(device, description, ticked.Contains(device)));
        }

        _otherDevices = string.Join(", ", ticked.Where(d => !KnownDevices.Any(k => string.Equals(k.Device, d, StringComparison.OrdinalIgnoreCase))));
        _includeSubfolders = targets.Any(t => t.Options.IncludeSubfolders);

        Heading = targets.Count == 1
            ? $"Back up {targets[0].Robot.Describe()}"
            : string.Create(CultureInfo.CurrentCulture, $"Back up {targets.Count} robots");
    }

    public string Heading { get; }

    public ObservableCollection<DeviceChoice> Devices { get; } = [];

    /// <summary>Devices not in the list, typed: "FRH:, MC2:".</summary>
    public string OtherDevices
    {
        get => _otherDevices;
        set => SetProperty(ref _otherDevices, value ?? string.Empty);
    }

    public bool IncludeSubfolders
    {
        get => _includeSubfolders;
        set => SetProperty(ref _includeSubfolders, value);
    }

    /// <summary>True when the selected robots' settings ask for different devices.</summary>
    public bool RobotsDiffer { get; }

    /// <summary>Back each robot up as its own settings say, ignoring the ticks. Offered only when they differ.</summary>
    public bool UseEachRobotsOwn
    {
        get => _useEachRobotsOwn;
        set
        {
            if (SetProperty(ref _useEachRobotsOwn, value))
            {
                OnPropertyChanged(nameof(CanChoose));
            }
        }
    }

    /// <summary>Whether the tick boxes count - false while "each robot's own" is on.</summary>
    public bool CanChoose => !UseEachRobotsOwn;

    public string? Problem
    {
        get => _problem;
        private set => SetProperty(ref _problem, value);
    }

    /// <summary>
    /// The options for each robot, or false with <see cref="Problem"/> saying why not. Excludes, pauses
    /// and retries always come from the robot's settings; only devices and folders are chosen here.
    /// </summary>
    public bool TryBuild(out IReadOnlyDictionary<Robot, BackupOptions>? options)
    {
        options = null;
        if (UseEachRobotsOwn)
        {
            Problem = null;
            options = _targets.ToDictionary(t => t.Robot, t => t.Options);
            return true;
        }

        var devices = Devices.Where(d => d.IsChecked).Select(d => d.Device).ToList();
        foreach (string typed in OtherDevices.Split([',', ';', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!BackupProfile.TryNormaliseDevice(typed, out string? device))
            {
                Problem = $"'{typed}' is not a controller device. Devices look like MD:, FR:, FRA:, MC:, UD1:.";
                return false;
            }

            devices.Add(device!);
        }

        IReadOnlyList<string> chosen = BackupProfile.NormaliseDevices(devices);
        if (chosen.Count == 0)
        {
            Problem = "Tick at least one device. MD: is the one that matters for a file backup.";
            return false;
        }

        Problem = null;
        options = _targets.ToDictionary(t => t.Robot, t => t.Options with { Devices = chosen, IncludeSubfolders = IncludeSubfolders });
        return true;
    }
}
