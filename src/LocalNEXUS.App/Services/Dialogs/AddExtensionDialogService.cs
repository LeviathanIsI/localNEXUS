using System.Windows;
using LocalNEXUS.App.ViewModels;
using LocalNEXUS.App.Views;

namespace LocalNEXUS.App.Services.Dialogs;

/// <summary>
/// Shows the add dialog and hands back what it collected.
/// </summary>
/// <remarks>
/// Modal, unlike the extensions window itself. Adding something is one decision with an answer,
/// and leaving a half filled form open behind the list would invite exactly the confusion that
/// moving these out of the settings column was meant to remove.
/// </remarks>
public sealed class AddExtensionDialogService : IAddExtensionDialog
{
    /// <inheritdoc />
    public AddExtensionRequest? Ask(AddExtensionMethod method)
    {
        var viewModel = new AddExtensionViewModel(method);

        var window = new AddExtensionWindow
        {
            DataContext = viewModel,
            Owner = ActiveOwner()
        };

        var accepted = false;

        window.AddButton.Click += (_, _) =>
        {
            accepted = true;
            window.Close();
        };

        window.CancelButton.Click += (_, _) => window.Close();
        window.Loaded += (_, _) => window.ValueBox.Focus();

        window.ShowDialog();

        return accepted ? viewModel.ToRequest() : null;
    }

    /// <summary>
    /// The window this dialog should sit over.
    /// </summary>
    /// <remarks>
    /// The extensions window when it is the one open, so the dialog appears over what the person
    /// is actually looking at rather than behind it on the main window.
    /// </remarks>
    private static Window? ActiveOwner()
    {
        if (Application.Current is not { } application)
        {
            return null;
        }

        foreach (Window window in application.Windows)
        {
            if (window.IsActive)
            {
                return window;
            }
        }

        return application.MainWindow;
    }
}
