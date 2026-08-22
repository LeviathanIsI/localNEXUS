namespace LocalNEXUS.App.Services.Dialogs;

/// <summary>
/// Shows the extensions window.
/// </summary>
/// <remarks>
/// Behind a service for the same reason the file pickers are: a view model that reached for a
/// Window would be holding a window handle, and nothing else in this application does.
/// </remarks>
public interface IExtensionsWindow
{
    /// <summary>
    /// Opens the window, or brings it forward when it is already open.
    /// </summary>
    /// <param name="viewModel">The extensions view model the window binds to.</param>
    void Show(object viewModel);

    /// <summary>Closes it if it is open. Called when the application shuts down.</summary>
    void Close();
}

/// <summary>Which of the four ways of adding an extension a dialog is collecting for.</summary>
public enum AddExtensionMethod
{
    /// <summary>An npm package name, which is what most MCP servers are.</summary>
    Npm,

    /// <summary>A git repository, cloned and started per the manifest it carries.</summary>
    Git,

    /// <summary>A command line given directly.</summary>
    Command
}

/// <summary>
/// What an add dialog collected, or null when it was cancelled.
/// </summary>
/// <param name="Method">Which method was used.</param>
/// <param name="Name">A display name, used only by the command method.</param>
/// <param name="Value">The package name, the url, or the command.</param>
/// <param name="Arguments">Arguments, used only by the command method.</param>
/// <param name="WorkingDirectory">Working directory, used only by the command method.</param>
/// <param name="Environment">Extra environment variables, one per line as NAME=value.</param>
/// <param name="SpeaksMcp">Whether the command speaks MCP.</param>
/// <param name="SpeaksNode">Whether the command contributes node types.</param>
public sealed record AddExtensionRequest(
    AddExtensionMethod Method,
    string Name,
    string Value,
    string Arguments,
    string WorkingDirectory,
    string Environment,
    bool SpeaksMcp,
    bool SpeaksNode,
    bool SpeaksSpec = false);

/// <summary>Collects what is needed to add an extension, one small dialog per method.</summary>
public interface IAddExtensionDialog
{
    /// <summary>Asks for the details. Returns null when the user cancels.</summary>
    AddExtensionRequest? Ask(AddExtensionMethod method);
}
