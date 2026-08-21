using CommunityToolkit.Mvvm.ComponentModel;
using LocalNEXUS.Installer.Models;

namespace LocalNEXUS.Installer.ViewModels;

/// <summary>One row of the step rail.</summary>
public sealed partial class StepItemViewModel : ObservableObject
{
    /// <summary>Done, active or upcoming, which is what the circle is drawn from.</summary>
    [ObservableProperty]
    private StepState _state = StepState.Upcoming;

    public StepItemViewModel(WizardStep step, int number, string label)
    {
        Step = step;
        Number = number;
        Label = label;
    }

    /// <summary>Which step this row is.</summary>
    public WizardStep Step { get; }

    /// <summary>Its position, which is what an upcoming row shows instead of a check.</summary>
    public int Number { get; }

    /// <summary>Its name.</summary>
    public string Label { get; }
}

/// <summary>One row of the component list.</summary>
public sealed partial class ComponentItemViewModel : ObservableObject
{
    /// <summary>Whether it will be installed.</summary>
    [ObservableProperty]
    private bool _isSelected = true;

    /// <summary>What the size column reads, which for llama.cpp depends on the build chosen next.</summary>
    [ObservableProperty]
    private string _sizeText;

    public ComponentItemViewModel(
        EngineComponent? component,
        string name,
        string description,
        string sizeText,
        bool isRequired = false)
    {
        Component = component;
        Name = name;
        Description = description;
        _sizeText = sizeText;
        IsRequired = isRequired;
    }

    /// <summary>Which engine, or null for the application itself.</summary>
    public EngineComponent? Component { get; }

    /// <summary>What it is called.</summary>
    public string Name { get; }

    /// <summary>One line saying what it does for the person, not how it works.</summary>
    public string Description { get; }

    /// <summary>True for the application, which cannot be unticked.</summary>
    public bool IsRequired { get; }

    /// <summary>True when the row responds to being clicked.</summary>
    public bool CanToggle => !IsRequired;
}

/// <summary>One of the four llama.cpp builds.</summary>
public sealed partial class BuildOptionViewModel : ObservableObject
{
    /// <summary>Whether this is the chosen build.</summary>
    [ObservableProperty]
    private bool _isSelected;

    public BuildOptionViewModel(LlamaFlavour flavour, string name, string description, long bytes)
    {
        Flavour = flavour;
        Name = name;
        Description = description;
        SizeText = $"{(bytes + 524_288L) / 1_048_576L} MB";
    }

    /// <summary>Which build.</summary>
    public LlamaFlavour Flavour { get; }

    /// <summary>What it is called.</summary>
    public string Name { get; }

    /// <summary>Who it is for.</summary>
    public string Description { get; }

    /// <summary>What it costs to download, including the CUDA runtime where there is one.</summary>
    public string SizeText { get; }
}

/// <summary>One line of the fetch list on the Ready page.</summary>
/// <param name="Label">What it is called.</param>
/// <param name="SizeText">What it weighs.</param>
public sealed record FetchItem(string Label, string SizeText);
