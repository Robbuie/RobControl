using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using RobControl.App.Diagnostics;
using RobControl.Core.Trending;

namespace RobControl.App.ViewModels;

/// <summary>One watched signal on one robot, with its latest value.</summary>
public sealed class SignalRowViewModel(TrendSignal signal, RobotRowViewModel robot) : ObservableObject
{
    private string _value = "-";
    private string _readAt = string.Empty;
    private string? _problem;
    private bool _isPlotted = true;

    public TrendSignal Signal { get; } = signal ?? throw new ArgumentNullException(nameof(signal));

    public RobotRowViewModel Robot { get; } = robot ?? throw new ArgumentNullException(nameof(robot));

    public string RobotName => Robot.Name;

    public string Address => Signal.Address.Text;

    public string Label => Signal.Label ?? string.Empty;

    /// <summary>"Robot R[2] Weld count" - the chart legend.</summary>
    public string SeriesName => $"{Robot.Name} {Signal.Describe()}";

    public string Source => Signal.Address.Kind switch
    {
        SignalKind.NumericRegister => "NUMREG.VA",
        SignalKind.Io => "IOSTATE.DG",
        _ => "KCL",
    };

    public string Value
    {
        get => _value;
        private set => SetProperty(ref _value, value);
    }

    public string ReadAt
    {
        get => _readAt;
        private set => SetProperty(ref _readAt, value);
    }

    public string? Problem
    {
        get => _problem;
        private set
        {
            if (SetProperty(ref _problem, value))
            {
                OnPropertyChanged(nameof(State));
            }
        }
    }

    public ReadinessState State => _problem is not null ? ReadinessState.Warning : _readAt.Length == 0 ? ReadinessState.Unknown : ReadinessState.Ready;

    public bool IsPlotted
    {
        get => _isPlotted;
        set => SetProperty(ref _isPlotted, value);
    }

    public void Apply(DateTimeOffset utc, IReadOnlyDictionary<string, double> values, IReadOnlyDictionary<string, string> problems)
    {
        if (values.TryGetValue(Address, out double v))
        {
            Value = Signal.Address.IsDigital ? (v != 0 ? "ON" : "OFF") : v.ToString("G10", CultureInfo.CurrentCulture);
            ReadAt = utc.ToLocalTime().ToString("HH:mm:ss", CultureInfo.CurrentCulture);
            Problem = null;
        }
        else if (problems.TryGetValue(Address, out string? why))
        {
            Problem = why;
        }

        OnPropertyChanged(nameof(State));
    }
}
