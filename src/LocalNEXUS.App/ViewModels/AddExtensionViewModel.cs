using CommunityToolkit.Mvvm.ComponentModel;
using LocalNEXUS.App.Services.Dialogs;

namespace LocalNEXUS.App.ViewModels;

/// <summary>
/// What one add dialog is collecting.
/// </summary>
/// <remarks>
/// One view model for all three dialogs rather than three, because they differ by which fields
/// are shown and not by what they do. The method decides the title, the prompt and which rows
/// are visible; nothing else varies.
/// </remarks>
public sealed partial class AddExtensionViewModel : ObservableObject
{
    /// <summary>The package name, the repository url, or the command to run.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanAccept))]
    private string _value = string.Empty;

    /// <summary>A name for the extension. Only the command method asks for one.</summary>
    [ObservableProperty]
    private string _name = string.Empty;

    /// <summary>Arguments, split on spaces when it is used.</summary>
    [ObservableProperty]
    private string _arguments = string.Empty;

    /// <summary>Working directory, or blank to use the extension folder.</summary>
    [ObservableProperty]
    private string _workingDirectory = string.Empty;

    /// <summary>Extra environment variables, one per line as NAME=value.</summary>
    [ObservableProperty]
    private string _environment = string.Empty;

    /// <summary>Whether the command speaks MCP and so contributes tools.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanAccept))]
    private bool _speaksMcp = true;

    /// <summary>Whether the command contributes node types.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanAccept))]
    private bool _speaksNode;

    /// <summary>
    /// Whether the command bridges a spec driven planning tool, and so brings a tab.
    /// </summary>
    /// <remarks>
    /// Offered here because the bridge that speaks this contract is not published, and a command
    /// or a folder is how somebody runs one before it is. The same escape hatch the other two
    /// contracts already had.
    /// </remarks>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanAccept))]
    private bool _speaksSpec;

    public AddExtensionViewModel(AddExtensionMethod method) => Method = method;

    /// <summary>Which method this dialog is for.</summary>
    public AddExtensionMethod Method { get; }

    /// <summary>The dialog title.</summary>
    public string Title => Method switch
    {
        AddExtensionMethod.Npm => "Add from npm",
        AddExtensionMethod.Git => "Add from git",
        _ => "Add by command"
    };

    /// <summary>One line saying what this method is for.</summary>
    public string Explanation => Method switch
    {
        AddExtensionMethod.Npm =>
            "Most MCP servers are npm packages. The version resolves each time it runs, so nothing is added to this machine.",
        AddExtensionMethod.Git =>
            "The repository is cloned and started the way its manifest says. It needs a manifest for that to work.",
        _ =>
            "Anything that speaks one of the contracts works here, whether or not it fits the other three."
    };

    /// <summary>The label above the main field.</summary>
    public string ValueLabel => Method switch
    {
        AddExtensionMethod.Npm => "Package name",
        AddExtensionMethod.Git => "Repository url",
        _ => "Command"
    };

    /// <summary>An example, shown under the main field.</summary>
    public string ValueHint => Method switch
    {
        AddExtensionMethod.Npm => "for example anklebreaker-unity-mcp@latest",
        AddExtensionMethod.Git => "for example https://github.com/someone/their-server.git",
        _ => "the executable to run, such as node or a full path"
    };

    /// <summary>True when the extra command fields are shown.</summary>
    public bool IsCommand => Method == AddExtensionMethod.Command;

    /// <summary>
    /// True when there is enough to go on.
    /// </summary>
    /// <remarks>
    /// A command with neither contract ticked is refused here rather than after it has been
    /// added, because the host would have no way to talk to it and the extension would sit in the
    /// list doing nothing.
    /// </remarks>
    public bool CanAccept
    {
        get
        {
            if (string.IsNullOrWhiteSpace(Value))
            {
                return false;
            }

            return !IsCommand || SpeaksMcp || SpeaksNode || SpeaksSpec;
        }
    }

    /// <summary>What the dialog collected.</summary>
    public AddExtensionRequest ToRequest() => new(
        Method,
        Name.Trim(),
        Value.Trim(),
        Arguments.Trim(),
        WorkingDirectory.Trim(),
        Environment.Trim(),
        SpeaksMcp,
        SpeaksNode,
        SpeaksSpec);
}
