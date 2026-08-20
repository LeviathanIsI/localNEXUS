using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using LocalNEXUS.App.Services.Theming;

namespace LocalNEXUS.App.ViewModels;

/// <summary>
/// One theme in the picker: what it is called, whether it is the one in force, and the command
/// that applies it.
/// </summary>
public sealed partial class ThemeChoiceViewModel : ObservableObject
{
    /// <summary>True while this is the theme the window is wearing.</summary>
    [ObservableProperty]
    private bool _isSelected;

    public ThemeChoiceViewModel(ThemeDefinition definition, ICommand pick, bool isSelected)
    {
        Definition = definition;
        Pick = pick;
        _isSelected = isSelected;
    }

    /// <summary>The theme itself.</summary>
    public ThemeDefinition Definition { get; }

    /// <summary>Applies this theme.</summary>
    public ICommand Pick { get; }

    /// <summary>What the picker calls it.</summary>
    public string DisplayName => Definition.DisplayName;

    /// <summary>One line explaining when somebody would pick it.</summary>
    public string Description => Definition.Description;
}
