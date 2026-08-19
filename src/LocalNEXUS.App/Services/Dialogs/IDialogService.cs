namespace LocalNEXUS.App.Services.Dialogs;

/// <summary>
/// Shows the operating system dialogs the application needs.
/// </summary>
/// <remarks>
/// View models call this instead of touching <c>Microsoft.Win32</c> or <c>MessageBox</c>
/// directly, which keeps them free of window handles and testable in isolation.
/// </remarks>
public interface IDialogService
{
    /// <summary>Asks the user to pick a folder. Returns null when they cancel.</summary>
    string? PickFolder(string title, string? initialDirectory = null);

    /// <summary>Asks the user where to save a file. Returns null when they cancel.</summary>
    string? PickSaveFile(string title, string defaultFileName, string filter, string? initialDirectory = null);

    /// <summary>Asks the user which file to open. Returns null when they cancel.</summary>
    string? PickOpenFile(string title, string filter, string? initialDirectory = null);

    /// <summary>Reports a problem the user needs to see even if they are not watching the feed.</summary>
    void ShowError(string title, string message);

    /// <summary>Opens a folder in Explorer. Does nothing when the folder is missing.</summary>
    void OpenFolderInExplorer(string folder);
}
