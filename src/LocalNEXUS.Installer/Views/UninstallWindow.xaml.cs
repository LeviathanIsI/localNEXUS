using System.Collections.ObjectModel;
using System.Windows;
using LocalNEXUS.Installer.Services;

namespace LocalNEXUS.Installer.Views;

/// <summary>
/// The window Add or remove programs opens.
/// </summary>
/// <remarks>
/// Deliberately one small window rather than a second wizard. Removing something is one decision,
/// and the only one worth asking about is whether the person's saved work goes with it.
/// </remarks>
public partial class UninstallWindow : Window
{
    private readonly ObservableCollection<string> _log = new();

    private bool _removeUserData;
    private bool _running;
    private bool _finished;

    public UninstallWindow()
    {
        InitializeComponent();

        LogList.ItemsSource = _log;

        DataToggle.Click += (_, _) =>
        {
            _removeUserData = !_removeUserData;
            DataTick.Visibility = _removeUserData ? Visibility.Visible : Visibility.Collapsed;
        };

        CancelButton.Click += (_, _) => Close();

        // One handler that changes what it does once the work is finished. Detaching a lambda
        // is not possible, and a flag is clearer than keeping a delegate around to remove.
        RemoveButton.Click += (_, _) =>
        {
            if (_finished)
            {
                Close();
                return;
            }

            Remove();
        };
    }

    private void Remove()
    {
        if (_running)
        {
            return;
        }

        _running = true;
        RemoveButton.IsEnabled = false;
        CancelButton.IsEnabled = false;
        DataToggle.IsEnabled = false;

        try
        {
            SetupRunner.Uninstall(_removeUserData, line => _log.Add(line));
            RemoveButton.Content = "Done";
        }
        catch (Exception ex)
        {
            _log.Add("Failed. " + ex.Message);
            RemoveButton.Content = "Close";
        }
        finally
        {
            _finished = true;
            _running = false;
            RemoveButton.IsEnabled = true;
        }
    }
}
