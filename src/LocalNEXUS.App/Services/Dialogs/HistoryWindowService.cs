using System.Windows;
using LocalNEXUS.App.Views;

namespace LocalNEXUS.App.Services.Dialogs;

/// <summary>Something that can put the run history in front of somebody.</summary>
public interface IHistoryWindow
{
    /// <summary>Opens the window, or brings the open one forward.</summary>
    void Show(object viewModel);

    /// <summary>Closes it, if it is open.</summary>
    void Close();
}

/// <summary>
/// Owns the one history window and where it sits.
/// </summary>
/// <remarks>
/// The same shape as the extensions window and for the same reasons. One window, reused. Not
/// modal, so the record can be read while the graph stays workable, which is the whole use for it:
/// looking up what went wrong last time while setting up the next attempt.
///
/// Where it sits is remembered for the session and not written to disk, because it belongs to a
/// piece of work rather than to the application.
/// </remarks>
public sealed class HistoryWindowService : IHistoryWindow
{
    private HistoryWindow? _window;

    private double? _left;
    private double? _top;
    private double _width = 1180d;
    private double _height = 720d;

    /// <inheritdoc />
    public void Show(object viewModel)
    {
        if (_window is not null)
        {
            if (_window.WindowState == WindowState.Minimized)
            {
                _window.WindowState = WindowState.Normal;
            }

            _window.Activate();
            return;
        }

        var window = new HistoryWindow
        {
            DataContext = viewModel,
            Owner = Application.Current?.MainWindow,
            Width = _width,
            Height = _height
        };

        if (_left is { } left && _top is { } top)
        {
            window.WindowStartupLocation = WindowStartupLocation.Manual;
            window.Left = left;
            window.Top = top;
        }

        window.Closing += (_, _) =>
        {
            // Read the placement back before it goes, because a closed window reports nothing.
            if (window.WindowState == WindowState.Normal)
            {
                _left = window.Left;
                _top = window.Top;
                _width = window.Width;
                _height = window.Height;
            }

            _window = null;
        };

        _window = window;
        window.Show();
    }

    /// <inheritdoc />
    public void Close()
    {
        _window?.Close();
        _window = null;
    }
}
