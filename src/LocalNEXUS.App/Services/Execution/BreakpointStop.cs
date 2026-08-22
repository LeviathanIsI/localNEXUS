using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LocalNEXUS.App.Models;

namespace LocalNEXUS.App.Services.Execution;

/// <summary>
/// One run held at a breakpoint, and the value it is holding.
/// </summary>
/// <remarks>
/// Everything the surface needs is here rather than spread between a service and a view model,
/// because a stop is a short lived thing with one question to answer: this is what is about to
/// cross the wire, do you want it changed.
///
/// Editing is offered for text and refused for anything else, and the refusal is stated. A plan is
/// a list of file tasks, and a text box containing a rendering of one is not a thing that can be
/// turned back into the list; offering it would be offering an edit that silently does nothing.
/// </remarks>
public sealed partial class BreakpointStop : ObservableObject
{
    private readonly TaskCompletionSource<object?> _completion =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private readonly object? _original;

    /// <summary>The value as it will be released, which is what the box is bound to.</summary>
    [ObservableProperty]
    private string _text;

    /// <summary>Builds a stop for one wire and the value about to cross it.</summary>
    public BreakpointStop(Connection connection, object? value)
    {
        Connection = connection;
        _original = value;

        IsEditable = value is null or string;
        _text = Describe(value);
    }

    /// <summary>The wire the run is held on.</summary>
    public Connection Connection { get; }

    /// <summary>Where the value came from and where it is going.</summary>
    public string Where => $"{Connection.Source.Owner.Title}.{Connection.Source.Name} to "
                           + $"{Connection.Target.Owner.Title}.{Connection.Target.Name}";

    /// <summary>What kind of value is passing, in the graph's own terms.</summary>
    public string Kind => Connection.PinType.ToString();

    /// <summary>True when the value is text and can therefore be replaced with other text.</summary>
    public bool IsEditable { get; }

    /// <summary>Why the value cannot be edited, or null when it can.</summary>
    public string? ReadOnlyReason => IsEditable
        ? null
        : $"This wire is carrying {DescribeType(_original)}, which is shown as it is rather than as "
          + "text that could be typed back into it. Release it, or stop the run.";

    /// <summary>Completes with the value to release, once somebody has released it.</summary>
    internal Task<object?> Released => _completion.Task;

    /// <summary>True until the stop has been released, which is what disables the button after.</summary>
    public bool IsHeld => !_completion.Task.IsCompleted;

    /// <summary>Releases the run, with whatever the box now holds.</summary>
    [RelayCommand(CanExecute = nameof(IsHeld))]
    private void Continue()
    {
        Release(IsEditable ? Text : _original);
    }

    /// <summary>Releases the run with the value untouched, whatever the box now holds.</summary>
    [RelayCommand(CanExecute = nameof(IsHeld))]
    private void Discard()
    {
        Text = Describe(_original);
        Release(_original);
    }

    /// <summary>Releases without anybody asking, which is what a cancelled run does.</summary>
    internal void Abandon() => Release(_original);

    private void Release(object? value)
    {
        if (!_completion.TrySetResult(value))
        {
            return;
        }

        OnPropertyChanged(nameof(IsHeld));
        ContinueCommand.NotifyCanExecuteChanged();
        DiscardCommand.NotifyCanExecuteChanged();
    }

    /// <summary>What to show in the box, which for a non text value is a rendering, not the value.</summary>
    private static string Describe(object? value) => value switch
    {
        null => string.Empty,
        string text => text,
        System.Collections.IEnumerable items => string.Join(
            Environment.NewLine,
            items.Cast<object?>().Select(item => item?.ToString() ?? "(nothing)")),
        _ => value.ToString() ?? string.Empty
    };

    private static string DescribeType(object? value) => value switch
    {
        null => "nothing",
        string => "text",
        System.Collections.ICollection collection => $"a list of {collection.Count} item(s)",
        _ => value.GetType().Name
    };
}
