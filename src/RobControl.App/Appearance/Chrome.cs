using System.Windows;

namespace RobControl.App.Appearance;

/// <summary>
/// One attached property for the shared title bar: something to put in it beside the title. Kept
/// for parity with NetControl; RobControl's main window no longer uses it - its menu has its own
/// row under the title bar (0.6.0), and every window gets a plain title.
/// </summary>
public static class Chrome
{
    public static readonly DependencyProperty TitleContentProperty = DependencyProperty.RegisterAttached(
        "TitleContent", typeof(object), typeof(Chrome), new FrameworkPropertyMetadata(null));

    public static object? GetTitleContent(DependencyObject element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return element.GetValue(TitleContentProperty);
    }

    public static void SetTitleContent(DependencyObject element, object? value)
    {
        ArgumentNullException.ThrowIfNull(element);
        element.SetValue(TitleContentProperty, value);
    }
}
