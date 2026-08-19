using System.Windows;
using System.Windows.Controls;

namespace LocalNEXUS.App.Infrastructure.Behaviors;

/// <summary>
/// Keeps a scroll viewer pinned to the bottom while content grows, which is what the activity
/// feed needs while tokens stream in.
/// </summary>
/// <remarks>
/// Attached behaviour rather than code behind so the view stays declarative. Scrolling only
/// follows the content while the user is already at the bottom: if they scroll up to read an
/// earlier entry, new output no longer yanks the view away from them.
/// </remarks>
public static class AutoScrollBehavior
{
    private const double BottomThreshold = 24d;

    public static readonly DependencyProperty IsEnabledProperty = DependencyProperty.RegisterAttached(
        "IsEnabled",
        typeof(bool),
        typeof(AutoScrollBehavior),
        new PropertyMetadata(false, OnIsEnabledChanged));

    private static readonly DependencyProperty IsPinnedToBottomProperty = DependencyProperty.RegisterAttached(
        "IsPinnedToBottom",
        typeof(bool),
        typeof(AutoScrollBehavior),
        new PropertyMetadata(true));

    public static void SetIsEnabled(DependencyObject element, bool value)
        => element.SetValue(IsEnabledProperty, value);

    public static bool GetIsEnabled(DependencyObject element)
        => (bool)element.GetValue(IsEnabledProperty);

    private static void OnIsEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not ScrollViewer scrollViewer)
        {
            return;
        }

        scrollViewer.ScrollChanged -= OnScrollChanged;

        if (e.NewValue is true)
        {
            scrollViewer.ScrollChanged += OnScrollChanged;
        }
    }

    private static void OnScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (sender is not ScrollViewer scrollViewer)
        {
            return;
        }

        // A vertical change with no extent change means the user moved the thumb. Record where
        // they left it so that later content growth knows whether it may follow.
        if (e.ExtentHeightChange == 0d)
        {
            var distanceFromBottom = scrollViewer.ExtentHeight - scrollViewer.VerticalOffset - scrollViewer.ViewportHeight;
            scrollViewer.SetValue(IsPinnedToBottomProperty, distanceFromBottom <= BottomThreshold);
            return;
        }

        if ((bool)scrollViewer.GetValue(IsPinnedToBottomProperty))
        {
            scrollViewer.ScrollToEnd();
        }
    }
}
