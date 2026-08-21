using System.Windows;

namespace LocalNEXUS.Installer.Views;

/// <summary>
/// The wizard shell.
/// </summary>
/// <remarks>
/// The caption buttons are wired here rather than through an attached behaviour, which is the one
/// place this project departs from the application. The application has several windows and a
/// behaviour earns its keep; this has one window and two buttons, and a behaviour would be more
/// machinery than the thing it replaces.
/// </remarks>
public partial class SetupWindow : Window
{
    public SetupWindow()
    {
        InitializeComponent();

        MinimiseButton.Click += (_, _) => WindowState = WindowState.Minimized;
        CloseButton.Click += (_, _) => Close();
    }
}
