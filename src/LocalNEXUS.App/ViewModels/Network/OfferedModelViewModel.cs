using CommunityToolkit.Mvvm.ComponentModel;
using LocalNEXUS.App.Services.Persistence;

namespace LocalNEXUS.App.ViewModels.Network;

/// <summary>
/// One local model, and whether this machine offers it to the mesh.
/// </summary>
/// <remarks>
/// Offering is opt in per model. Nothing is offered until it is ticked, because what this machine
/// serves to other people is a decision somebody should have made rather than one that happened
/// by default.
/// </remarks>
public sealed partial class OfferedModelViewModel : ObservableObject
{
    private readonly Action _changed;

    /// <summary>True when this model is offered to the mesh.</summary>
    [ObservableProperty]
    private bool _isOffered;

    public OfferedModelViewModel(LocalModelInfo model, bool isOffered, Action changed)
    {
        Model = model;
        _isOffered = isOffered;
        _changed = changed;
    }

    /// <summary>The model on disk.</summary>
    public LocalModelInfo Model { get; }

    /// <summary>Absolute path, which is what the engine is given.</summary>
    public string Path => Model.Path;

    /// <summary>The file or folder name.</summary>
    public string Name => Model.Name;

    /// <summary>The quantization, or a note that the name does not say.</summary>
    public string Quantisation => Model.Descriptor.Quantisation;

    /// <summary>Size on disk.</summary>
    public string SizeLabel => Model.Descriptor.SizeLabel;

    /// <summary>GGUF or safetensors.</summary>
    public string FormatLabel => Model.FormatLabel;

    partial void OnIsOfferedChanged(bool value) => _changed();
}
