using CommunityToolkit.Mvvm.ComponentModel;
using LocalNEXUS.App.Services.Theming;

namespace LocalNEXUS.App.ViewModels;

/// <summary>
/// One theme in the picker: what it is called, and whether it is the one in force.
/// </summary>
/// <remarks>
/// Selecting is what applies it, rather than a command hanging off the radio button. A radio
/// button already has a notion of being chosen, and expressing the choice as that notion means the
/// keyboard, the narrator and anything else that drives the control all work without being
/// thought about.
/// </remarks>
public sealed class ThemeChoiceViewModel : ObservableObject
{
    private readonly Action<ThemeChoiceViewModel> _apply;

    private bool _isSelected;

    public ThemeChoiceViewModel(ThemeDefinition definition, Action<ThemeChoiceViewModel> apply, bool isSelected)
    {
        Definition = definition;
        _apply = apply;
        _isSelected = isSelected;
    }

    /// <summary>The theme itself.</summary>
    public ThemeDefinition Definition { get; }

    /// <summary>
    /// What the picker calls it, with the default one said out loud.
    /// </summary>
    /// <remarks>
    /// The marker is put on here rather than into the theme's own name, because the name is what
    /// the theme is called and which one happens to be the default is a fact about this build.
    /// </remarks>
    public string DisplayName => IsDefault
        ? $"{Definition.DisplayName} (Default)"
        : Definition.DisplayName;

    /// <summary>True when this is the theme a fresh install starts on.</summary>
    public bool IsDefault => Definition.Theme == ThemeService.DefaultTheme;

    /// <summary>One line explaining when somebody would pick it.</summary>
    public string Description => Definition.Description;

    /// <summary>True while this is the theme the window is wearing. Setting it applies the theme.</summary>
    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (!SetProperty(ref _isSelected, value) || !value)
            {
                return;
            }

            _apply(this);
        }
    }

    /// <summary>Marks this choice as chosen or not without applying anything.</summary>
    internal void SetSelectedQuietly(bool selected) => SetProperty(ref _isSelected, selected, nameof(IsSelected));
}
