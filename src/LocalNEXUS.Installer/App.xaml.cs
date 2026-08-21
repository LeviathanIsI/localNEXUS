using System.Windows;
using LocalNEXUS.Installer.Services;
using LocalNEXUS.Installer.ViewModels;
using LocalNEXUS.Installer.Views;

namespace LocalNEXUS.Installer;

/// <summary>
/// One executable, three jobs: install, modify, and uninstall.
/// </summary>
/// <remarks>
/// The same file does all three because Add or remove programs needs something to run and
/// shipping a second executable to do it would mean two programs to keep in step. Which job it
/// does is decided by the command line, and by whether an install is already there.
/// </remarks>
public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var arguments = e.Args.Select(a => a.Trim()).ToArray();

        if (arguments.Any(a => string.Equals(a, UninstallRegistrar.UninstallSwitch, StringComparison.OrdinalIgnoreCase)))
        {
            RunUninstall(arguments.Any(a => string.Equals(a, "--silent", StringComparison.OrdinalIgnoreCase)));
            return;
        }

        var window = new SetupWindow { DataContext = new SetupViewModel() };
        MainWindow = window;
        window.Show();
    }

    private void RunUninstall(bool silent)
    {
        if (silent)
        {
            // Windows asked for this without a person watching, so the safe reading of "remove
            // it" is the one that keeps their saved work.
            SetupRunner.Uninstall(removeUserData: false, _ => { });
            Shutdown();
            return;
        }

        var window = new UninstallWindow();
        MainWindow = window;
        window.Show();
    }
}
