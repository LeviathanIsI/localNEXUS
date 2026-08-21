using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace LocalNEXUS.App.ViewModels;

/// <summary>Which edge of the window a panel is attached to.</summary>
/// <remarks>
/// The only thing that differs between the two, and it decides the chevron direction rather than
/// being read anywhere else. A left panel points away from the middle when it is open and back
/// towards it when it is shut, and a right panel is the mirror of that.
/// </remarks>
public enum PanelSide
{
    /// <summary>Attached to the left edge.</summary>
    Left,

    /// <summary>Attached to the right edge.</summary>
    Right
}

/// <summary>
/// One side panel that can be collapsed to a strip and brought back.
/// </summary>
/// <remarks>
/// There are four of these, one per side per tab, because the two tabs put different things in the
/// same two slots and shutting the filters on the Network is not a statement about the Explorer on
/// the Workspace. The window resolves which pair is in force from the section that is showing, so
/// the layout is written once and never asks which tab it is drawing.
///
/// Collapsed leaves a strip rather than nothing. A panel that disappears completely takes its own
/// way back with it, which reads as the panel having broken rather than as having been put away.
///
/// For the session and not beyond it. Nothing here is written to the configuration file, which
/// matches the extensions window and is the deliberate answer: a panel shut to get at something
/// once should not still be shut tomorrow.
/// </remarks>
public sealed partial class CollapsiblePanelViewModel : ObservableObject
{
    /// <summary>True while the panel is showing at its full width.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsCollapsed))]
    [NotifyPropertyChangedFor(nameof(Chevron))]
    [NotifyPropertyChangedFor(nameof(ToggleTip))]
    private bool _isExpanded = true;

    public CollapsiblePanelViewModel(PanelSide side, string name)
    {
        Side = side;
        Name = name;
    }

    /// <summary>Which edge this panel is attached to.</summary>
    public PanelSide Side { get; }

    /// <summary>What this panel is called, for the tool tip and for a screen reader.</summary>
    public string Name { get; }

    /// <summary>True while the panel is collapsed to its strip.</summary>
    public bool IsCollapsed => !IsExpanded;

    /// <summary>
    /// The chevron, pointing the way the panel is about to move.
    /// </summary>
    /// <remarks>
    /// Segoe Fluent Icons E76B and E76C, which are the chevrons the shell uses everywhere else. An
    /// open left panel points left because pressing it sends the panel left; collapsed, it points
    /// right because that is where the panel comes back from. The right panel is the mirror, which
    /// is what makes the pair read without being explained.
    /// </remarks>
    public string Chevron => Side switch
    {
        PanelSide.Left => IsExpanded ? ChevronLeft : ChevronRight,
        _ => IsExpanded ? ChevronRight : ChevronLeft
    };

    private const string ChevronLeft = "\uE76B";

    private const string ChevronRight = "\uE76C";

    /// <summary>What the chevron says when it is hovered.</summary>
    public string ToggleTip => IsExpanded ? $"Collapse {Name}" : $"Show {Name}";

    /// <summary>Collapses the panel to its strip, or brings it back.</summary>
    [RelayCommand]
    public void Toggle() => IsExpanded = !IsExpanded;
}
