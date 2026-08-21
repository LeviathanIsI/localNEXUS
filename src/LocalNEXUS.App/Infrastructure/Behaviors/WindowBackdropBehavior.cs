using System.Windows;

namespace LocalNEXUS.App.Infrastructure.Behaviors;

/// <summary>
/// Keeps a window's system backdrop in step with a bound value, so whether the window is
/// translucent is a view model property rather than something the code behind reaches out and
/// does.
/// </summary>
/// <remarks>
/// Same shape and the same reason as the window commands behaviour beside it. Turning acrylic on
/// is a window operation and needs a handle, which a view model has no business holding, so the
/// view model says whether the window should be translucent and this is what knows how.
///
/// The handle does not exist while the XAML is being parsed, so a value that arrives before the
/// window is sourced is remembered and applied once it is. Without that, the first application at
/// startup is the one that silently does nothing.
/// </remarks>
public static class WindowBackdropBehavior
{
    /// <summary>Bind to true to make the window's base layer translucent.</summary>
    public static readonly DependencyProperty IsTranslucentProperty = DependencyProperty.RegisterAttached(
        "IsTranslucent",
        typeof(bool),
        typeof(WindowBackdropBehavior),
        new PropertyMetadata(false, OnIsTranslucentChanged));

    public static void SetIsTranslucent(DependencyObject element, bool value)
        => element.SetValue(IsTranslucentProperty, value);

    public static bool GetIsTranslucent(DependencyObject element)
        => (bool)element.GetValue(IsTranslucentProperty);

    private static void OnIsTranslucentChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not Window window || e.NewValue is not bool translucent)
        {
            return;
        }

        if (WindowBackdrop.Apply(window, translucent))
        {
            return;
        }

        // Either there is no handle yet or this build cannot do it. Waiting costs nothing in the
        // second case and is the whole point in the first.
        window.SourceInitialized -= OnSourceInitialized;
        window.SourceInitialized += OnSourceInitialized;
    }

    private static void OnSourceInitialized(object? sender, EventArgs e)
    {
        if (sender is not Window window)
        {
            return;
        }

        window.SourceInitialized -= OnSourceInitialized;
        WindowBackdrop.Apply(window, GetIsTranslucent(window));
    }
}
