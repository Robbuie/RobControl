using CommunityToolkit.Mvvm.Input;

namespace RobControl.App.ViewModels;

/// <summary>
/// One line of Site > Switch to. It carries its own command so the menu item binds to it directly,
/// rather than reaching up the tree for the window's view model.
/// </summary>
public sealed class SiteChoiceViewModel(string key, string name, bool isCurrent, IRelayCommand command)
{
    public string Key { get; } = key;

    public string Name { get; } = name;

    public bool IsCurrent { get; } = isCurrent;

    public IRelayCommand Command { get; } = command;
}
