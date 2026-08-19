using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LocalNEXUS.App.Models;

namespace LocalNEXUS.App.ViewModels;

/// <summary>
/// The wire currently being dragged between two pins.
/// </summary>
/// <remarks>
/// The canvas reports the pin under the cursor while the drag is in progress, which is validated
/// immediately so the wire can turn red and explain itself before the user lets go. The same
/// validator decides whether the drop is accepted, so what the preview promises is what happens.
/// </remarks>
public sealed partial class PendingConnectionViewModel : ObservableObject
{
    private readonly GraphModel _graph;
    private readonly Action<string> _onRejected;

    /// <summary>The pin the drag started from.</summary>
    [ObservableProperty]
    private Pin? _source;

    /// <summary>The pin currently under the cursor, reported by the canvas.</summary>
    [ObservableProperty]
    private Pin? _previewTarget;

    /// <summary>True while a wire is being dragged.</summary>
    [ObservableProperty]
    private bool _isVisible;

    /// <summary>True when releasing over the current preview target would create a connection.</summary>
    [ObservableProperty]
    private bool _isValid = true;

    /// <summary>The label drawn on the pending wire, either the pin type or the reason for refusal.</summary>
    [ObservableProperty]
    private string _text = string.Empty;

    /// <param name="graph">The graph the connection would be added to.</param>
    /// <param name="onRejected">Called with an explanation when a drop is refused.</param>
    public PendingConnectionViewModel(GraphModel graph, Action<string> onRejected)
    {
        _graph = graph;
        _onRejected = onRejected;
    }

    /// <summary>Begins a drag from the given pin.</summary>
    [RelayCommand]
    private void Start(Pin? source)
    {
        Source = source;
        PreviewTarget = null;
        IsVisible = source is not null;
        IsValid = true;
        Text = source is null ? string.Empty : source.PinType.ToString();
    }

    /// <summary>Ends a drag, creating the connection when the drop is valid.</summary>
    [RelayCommand]
    private void Complete(Pin? target)
    {
        var source = Source;
        Reset();

        if (source is null || target is null)
        {
            return;
        }

        var validation = ConnectionValidator.Validate(_graph, source, target);
        if (!validation.IsValid)
        {
            _onRejected($"Connection refused: {validation.Reason}.");
            return;
        }

        // The user may have dragged from the input end, so put the pins the right way round.
        var (output, input) = source.Direction == PinDirection.Output ? (source, target) : (target, source);

        if (!_graph.TryConnect(output, input, out var reason))
        {
            _onRejected($"Connection refused: {reason}.");
        }
    }

    private void Reset()
    {
        IsVisible = false;
        Source = null;
        PreviewTarget = null;
        IsValid = true;
        Text = string.Empty;
    }

    partial void OnPreviewTargetChanged(Pin? value)
    {
        if (Source is null)
        {
            return;
        }

        if (value is null)
        {
            IsValid = true;
            Text = Source.PinType.ToString();
            return;
        }

        var validation = ConnectionValidator.Validate(_graph, Source, value);
        IsValid = validation.IsValid;
        Text = validation.Reason;
    }
}
