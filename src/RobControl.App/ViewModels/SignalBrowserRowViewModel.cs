using CommunityToolkit.Mvvm.ComponentModel;
using RobControl.Core.Trending;

namespace RobControl.App.ViewModels;

/// <summary>One line of the signal browser: a tick box, the signal, its comment and the value it had when listed.</summary>
public sealed class SignalBrowserRowViewModel : ObservableObject
{
    private bool _isChecked;
    private readonly SignalEntry _entry;

    public SignalBrowserRowViewModel(SignalEntry entry, bool alreadyWatched)
    {
        _entry = entry ?? throw new ArgumentNullException(nameof(entry));
        AlreadyWatched = alreadyWatched;
    }

    public SignalEntry Entry => _entry;

    public SignalAddress Address => _entry.Address;

    /// <summary><c>R[2]</c>, <c>DI[1]</c>, <c>$SCR.$NUM_GROUP</c>.</summary>
    public string Signal => _entry.Address.Text;

    /// <summary>Register, Digital in, Group out... - what the filter's category matches.</summary>
    public string Kind => KindOf(_entry.Address);

    public string Comment => _entry.Comment ?? string.Empty;

    public string Value => _entry.Value ?? string.Empty;

    /// <summary>Already on the Trends tab for the robot being browsed; ticking it again adds nothing there.</summary>
    public bool AlreadyWatched { get; }

    public bool IsChecked
    {
        get => _isChecked;
        set => SetProperty(ref _isChecked, value);
    }

    public static string KindOf(SignalAddress address)
    {
        ArgumentNullException.ThrowIfNull(address);
        return address.Kind switch
        {
            SignalKind.NumericRegister => "Register",
            SignalKind.SystemVariable => "System variable",
            _ => address.IoType switch
            {
                "DI" => "Digital in",
                "DO" => "Digital out",
                "RI" => "Robot in",
                "RO" => "Robot out",
                "UI" => "UOP in",
                "UO" => "UOP out",
                "SI" => "SOP in",
                "SO" => "SOP out",
                "WI" => "Weld in",
                "WO" => "Weld out",
                "GI" => "Group in",
                "GO" => "Group out",
                "AI" => "Analog in",
                "AO" => "Analog out",
                "F" => "Flag",
                "M" => "Marker",
                _ => "I/O",
            },
        };
    }
}
