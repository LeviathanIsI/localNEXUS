using System.Windows;
using System.Windows.Input;

namespace LocalNEXUS.App.Infrastructure.Behaviors;

/// <summary>
/// Makes the minimise, maximise, restore and close commands work on a window that draws its own
/// title bar.
/// </summary>
/// <remarks>
/// WPF ships the commands but not their handlers, so a window with custom chrome normally grows a
/// code behind file with four click handlers in it. Attaching them here keeps the rule that code
/// behind is a call to InitializeComponent and nothing else, and it is the same shape as the auto
/// scroll behaviour already in this folder.
///
/// These are window operations rather than application decisions, so they belong to the view. A
/// view model has no business knowing whether it is in a window at all.
/// </remarks>
public static class WindowCommandsBehavior
{
    /// <summary>Set to true on a window to give it working caption buttons.</summary>
    public static readonly DependencyProperty IsEnabledProperty = DependencyProperty.RegisterAttached(
        "IsEnabled",
        typeof(bool),
        typeof(WindowCommandsBehavior),
        new PropertyMetadata(false, OnIsEnabledChanged));

    public static void SetIsEnabled(DependencyObject element, bool value)
        => element.SetValue(IsEnabledProperty, value);

    public static bool GetIsEnabled(DependencyObject element)
        => (bool)element.GetValue(IsEnabledProperty);

    private static void OnIsEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not Window window || e.NewValue is not true)
        {
            return;
        }

        window.CommandBindings.Add(new CommandBinding(
            SystemCommands.MinimizeWindowCommand,
            (_, _) => SystemCommands.MinimizeWindow(window)));

        window.CommandBindings.Add(new CommandBinding(
            SystemCommands.MaximizeWindowCommand,
            (_, _) => SystemCommands.MaximizeWindow(window)));

        window.CommandBindings.Add(new CommandBinding(
            SystemCommands.RestoreWindowCommand,
            (_, _) => SystemCommands.RestoreWindow(window)));

        window.CommandBindings.Add(new CommandBinding(
            SystemCommands.CloseWindowCommand,
            (_, _) => SystemCommands.CloseWindow(window)));
    }
}
