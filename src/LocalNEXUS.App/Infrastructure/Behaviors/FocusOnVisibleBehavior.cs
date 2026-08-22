using System.Windows;
using System.Windows.Controls;

namespace LocalNEXUS.App.Infrastructure.Behaviors;

/// <summary>
/// Puts the caret in a box the moment it appears.
/// </summary>
/// <remarks>
/// The node search is opened by a double click or by letting go of a wire, and in both the hands
/// are already on the mouse and the next thing to happen is typing. A search box that has to be
/// clicked before it will accept a letter is a search box people stop using.
///
/// Focus is asked for at input priority rather than immediately, because an element that has only
/// just become visible has not been arranged yet and cannot take focus until it has.
/// </remarks>
public static class FocusOnVisibleBehavior
{
    public static readonly DependencyProperty IsEnabledProperty = DependencyProperty.RegisterAttached(
        "IsEnabled",
        typeof(bool),
        typeof(FocusOnVisibleBehavior),
        new PropertyMetadata(false, OnIsEnabledChanged));

    public static void SetIsEnabled(DependencyObject element, bool value)
        => element.SetValue(IsEnabledProperty, value);

    public static bool GetIsEnabled(DependencyObject element)
        => (bool)element.GetValue(IsEnabledProperty);

    private static void OnIsEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not FrameworkElement element)
        {
            return;
        }

        element.IsVisibleChanged -= OnVisibleChanged;

        if (e.NewValue is true)
        {
            element.IsVisibleChanged += OnVisibleChanged;
        }
    }

    private static void OnVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is not true || sender is not FrameworkElement element)
        {
            return;
        }

        element.Dispatcher.BeginInvoke(
            System.Windows.Threading.DispatcherPriority.Input,
            new Action(() =>
            {
                element.Focus();

                if (element is TextBox box)
                {
                    box.SelectAll();
                }
            }));
    }
}
