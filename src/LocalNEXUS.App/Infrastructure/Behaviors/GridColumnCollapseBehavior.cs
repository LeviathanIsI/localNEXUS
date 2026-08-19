using System.Windows;
using System.Windows.Controls;

namespace LocalNEXUS.App.Infrastructure.Behaviors;

/// <summary>
/// Collapses a grid column to zero width and restores it later, remembering the width the user
/// dragged it to in between.
/// </summary>
/// <remarks>
/// Attached behaviour because a <see cref="ColumnDefinition"/>'s width cannot be toggled from
/// a style, and a plain binding on Width would be overwritten the first time a splitter drags
/// the column. The peer panel uses this to fold away without losing its size.
/// </remarks>
public static class GridColumnCollapseBehavior
{
    public static readonly DependencyProperty IsCollapsedProperty = DependencyProperty.RegisterAttached(
        "IsCollapsed",
        typeof(bool),
        typeof(GridColumnCollapseBehavior),
        new PropertyMetadata(false, OnIsCollapsedChanged));

    /// <summary>The width restored when a column expands for the first time.</summary>
    public static readonly DependencyProperty ExpandedWidthProperty = DependencyProperty.RegisterAttached(
        "ExpandedWidth",
        typeof(GridLength),
        typeof(GridColumnCollapseBehavior),
        new PropertyMetadata(new GridLength(340d)));

    private static readonly DependencyProperty StoredWidthProperty = DependencyProperty.RegisterAttached(
        "StoredWidth",
        typeof(GridLength?),
        typeof(GridColumnCollapseBehavior),
        new PropertyMetadata(null));

    public static void SetIsCollapsed(DependencyObject element, bool value)
        => element.SetValue(IsCollapsedProperty, value);

    public static bool GetIsCollapsed(DependencyObject element)
        => (bool)element.GetValue(IsCollapsedProperty);

    public static void SetExpandedWidth(DependencyObject element, GridLength value)
        => element.SetValue(ExpandedWidthProperty, value);

    public static GridLength GetExpandedWidth(DependencyObject element)
        => (GridLength)element.GetValue(ExpandedWidthProperty);

    private static void OnIsCollapsedChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not ColumnDefinition column)
        {
            return;
        }

        if (e.NewValue is true)
        {
            column.SetValue(StoredWidthProperty, column.Width);
            column.MinWidth = 0d;
            column.Width = new GridLength(0d);
        }
        else
        {
            var stored = (GridLength?)column.GetValue(StoredWidthProperty);
            column.Width = stored is { Value: > 0d } width ? width : GetExpandedWidth(column);
        }
    }
}
