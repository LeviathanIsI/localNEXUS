using System.Windows;
using System.Windows.Input;
using LocalNEXUS.App.ViewModels;
using Nodify;

namespace LocalNEXUS.App.Infrastructure.Behaviors;

/// <summary>
/// Opens the node search where the canvas was double clicked, at the point that was clicked.
/// </summary>
/// <remarks>
/// Attached behaviour rather than code behind, which stays a call to InitializeComponent. What it
/// needs is the two things a view model cannot see: that a double click happened, and where on the
/// canvas it happened in the coordinates nodes are positioned in rather than in screen pixels.
///
/// Only a double click on the canvas itself opens it. One on a node is the node's, and the test for
/// that is whether the editor was the thing under the cursor rather than a search up the tree for a
/// node container, because a node is made of many elements and any of them can be the source.
/// </remarks>
public static class CanvasSearchBehavior
{
    public static readonly DependencyProperty SearchProperty = DependencyProperty.RegisterAttached(
        "Search",
        typeof(NodeSearchViewModel),
        typeof(CanvasSearchBehavior),
        new PropertyMetadata(null, OnSearchChanged));

    public static void SetSearch(DependencyObject element, NodeSearchViewModel? value)
        => element.SetValue(SearchProperty, value);

    public static NodeSearchViewModel? GetSearch(DependencyObject element)
        => (NodeSearchViewModel?)element.GetValue(SearchProperty);

    private static void OnSearchChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not NodifyEditor editor)
        {
            return;
        }

        editor.MouseDoubleClick -= OnDoubleClick;
        editor.PreviewMouseUp -= OnMouseUp;

        if (e.NewValue is NodeSearchViewModel)
        {
            editor.MouseDoubleClick += OnDoubleClick;
            editor.PreviewMouseUp += OnMouseUp;
        }
    }

    /// <summary>
    /// Remembers where the pointer was, so a wire released over nothing knows where it landed.
    /// </summary>
    /// <remarks>
    /// The released wire reaches the view model as a pin and nothing else, because that is what the
    /// canvas hands to the completion command. Rather than change what a pending connection carries
    /// so a position could ride along with it, the position is written here, on the way up, from
    /// the only object that knows it.
    /// </remarks>
    private static void OnMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is NodifyEditor editor
            && editor.DataContext is MainViewModel main)
        {
            main.LastCanvasPoint = editor.MouseLocation;
        }
    }

    private static void OnDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is not NodifyEditor editor
            || e.ChangedButton != MouseButton.Left
            || GetSearch(editor) is not { } search)
        {
            return;
        }

        // A double click that landed on a node belongs to the node. The editor reports the point
        // in its own space, which is the space node locations are in, so nothing has to be
        // converted for the placed node to appear under the cursor.
        if (!ReferenceEquals(e.OriginalSource, editor))
        {
            return;
        }

        var point = editor.MouseLocation;

        search.Open(point.X, point.Y);
        e.Handled = true;
    }
}
