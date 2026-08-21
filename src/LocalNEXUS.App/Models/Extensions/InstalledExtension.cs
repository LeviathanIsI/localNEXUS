using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace LocalNEXUS.App.Models.Extensions;

/// <summary>
/// One extension as this project knows it: what it declared, where it came from, and how it is
/// behaving.
/// </summary>
/// <remarks>
/// Observable because the panel binds straight to it, following <c>ModelCatalog</c> and the
/// source registry rather than projecting into a parallel view model that then has to be kept in
/// step.
/// <para>
/// The manifest is separate from the state on purpose. What an extension claims about itself is
/// fixed at install time and is what the details pane shows; how it is behaving changes every
/// time it is started. Merging them would mean a failed start could quietly rewrite what the
/// extension says it contributes.
/// </para>
/// </remarks>
public sealed partial class InstalledExtension : ObservableObject
{
    /// <summary>How it is behaving right now.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsUsable))]
    [NotifyPropertyChangedFor(nameof(StateText))]
    private ExtensionState _state = ExtensionState.NotInstalled;

    /// <summary>Why it is in that state, when the state is one that needs explaining.</summary>
    [ObservableProperty]
    private string? _stateDetail;

    /// <summary>False when the user has switched it off. A disabled extension is never started.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsUsable))]
    private bool _isEnabled = true;

    /// <summary>Tools the running server actually reported, which outrank the manifest's copy.</summary>
    public ObservableCollection<ToolContribution> DiscoveredTools { get; } = new();

    public InstalledExtension(ExtensionManifest manifest, ExtensionOrigin origin, string originDetail)
    {
        Manifest = manifest;
        Origin = origin;
        OriginDetail = originDetail;
    }

    /// <summary>What the extension declares about itself.</summary>
    public ExtensionManifest Manifest { get; }

    /// <summary>Which of the five ways it was added.</summary>
    public ExtensionOrigin Origin { get; }

    /// <summary>The package name, url or folder it came from, shown verbatim in the details pane.</summary>
    public string OriginDetail { get; }

    /// <summary>Where this extension's stderr is written, so the panel can link to it.</summary>
    public string? LogPath { get; set; }

    /// <summary>True when it may be started and used.</summary>
    public bool IsUsable => IsEnabled && State is not (ExtensionState.Failed or ExtensionState.NotInstalled);

    /// <summary>The state as the list shows it.</summary>
    public string StateText => State switch
    {
        ExtensionState.NotInstalled => "not installed",
        ExtensionState.Installing => "installing",
        ExtensionState.Starting => "starting",
        ExtensionState.Running => "running",
        ExtensionState.Unreachable => "unreachable",
        _ => "failed"
    };

    /// <summary>Records a failure and the reason for it, which are never set apart from each other.</summary>
    public void Fail(string reason)
    {
        State = ExtensionState.Failed;
        StateDetail = reason;
    }

    public override string ToString() => $"{Manifest.Id} ({StateText})";
}
