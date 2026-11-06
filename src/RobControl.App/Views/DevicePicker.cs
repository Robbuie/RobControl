using System.Windows;
using System.Windows.Controls;
using RobControl.App.ViewModels;
using RobControl.Core.Systems;

namespace RobControl.App.Views;

/// <summary>
/// The devices a backup takes, as tick boxes, plus a box for anything else (FRH:, MC2:). Used by
/// System settings and the robot dialog in place of a typed list.
///
/// <para>It speaks the same comma-separated text the typed box did (<see cref="Text"/>), so the
/// dialogs and <see cref="BackupProfileDraft"/> - which still validates - did not change. Nothing
/// ticked and nothing typed is blank, which keeps its meaning: "not set here".</para>
///
/// <para>Built in code rather than XAML: it is a row of tick boxes from
/// <see cref="BackupChoiceViewModel.KnownDevices"/>, the same list the Back up dialog offers, so the
/// two can never list different devices.</para>
/// </summary>
public sealed class DevicePicker : UserControl
{
    private readonly List<(string Device, CheckBox Box)> _boxes = [];
    private readonly TextBox _other;

    public DevicePicker()
    {
        var ticks = new WrapPanel { Orientation = Orientation.Horizontal };
        foreach ((string device, string description) in BackupChoiceViewModel.KnownDevices)
        {
            var box = new CheckBox
            {
                Content = device,
                ToolTip = description,
                Margin = new Thickness(0, 3, 14, 3),
                VerticalAlignment = VerticalAlignment.Center,
            };
            box.SetResourceReference(FontFamilyProperty, "mono");
            _boxes.Add((device, box));
            ticks.Children.Add(box);
        }

        var label = new TextBlock { Text = "Other", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
        _other = new TextBox
        {
            ToolTip = "Any other device, comma separated: FRH:, MC2:. A device the controller does not have fails on its own; the rest still arrive.",
        };
        _other.SetResourceReference(FontFamilyProperty, "mono");

        var otherRow = new DockPanel { Margin = new Thickness(0, 2, 0, 0) };
        DockPanel.SetDock(label, Dock.Left);
        otherRow.Children.Add(label);
        otherRow.Children.Add(_other);

        var stack = new StackPanel { Margin = new Thickness(0, 3, 0, 3) };
        stack.Children.Add(ticks);
        stack.Children.Add(otherRow);
        Content = stack;
    }

    /// <summary>
    /// "MD:, FR:, FRH:" - ticked devices in the offered order, then whatever is typed under Other.
    /// Setting it ticks the known devices and puts the rest, valid or not, under Other, so a bad name
    /// from a hand-edited file stays visible and is reported by <see cref="BackupProfileDraft.TryBuild"/>.
    /// </summary>
    public string Text
    {
        get
        {
            IEnumerable<string> ticked = _boxes.Where(b => b.Box.IsChecked == true).Select(b => b.Device);
            string other = _other.Text.Trim();
            return string.Join(", ", other.Length > 0 ? ticked.Append(other) : ticked);
        }

        set
        {
            var rest = new List<string>();
            var wanted = new HashSet<string>(StringComparer.Ordinal);
            foreach (string raw in (value ?? string.Empty).Split([',', ';', ' ', '\t', '\r', '\n'],
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (BackupProfile.TryNormaliseDevice(raw, out string? device)
                    && _boxes.Any(b => string.Equals(b.Device, device, StringComparison.Ordinal)))
                {
                    wanted.Add(device!);
                }
                else
                {
                    rest.Add(raw);
                }
            }

            foreach ((string device, CheckBox box) in _boxes)
            {
                box.IsChecked = wanted.Contains(device);
            }

            _other.Text = string.Join(", ", rest);
        }
    }
}
