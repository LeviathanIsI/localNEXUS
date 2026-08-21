using System.Windows;
using LocalNEXUS.App.Views;

namespace LocalNEXUS.App.Services.Dialogs;

/// <summary>
/// Owns the one extensions window and where it sits.
/// </summary>
/// <remarks>
/// One window, reused. Opening it twice brings the existing one forward rather than stacking a
/// second copy of the same list.
///
/// It is not modal, so the graph stays workable while it is open. That is the point of moving it
/// out of settings: somebody wiring a node that needs an extension should be able to look at both.
///
/// Where it sits is remembered for the session and not written to disk. Reopening it puts it back
/// where it was, and a restart starts it centred again, which is the behaviour of a window that
/// belongs to a piece of work rather than to the application.
/// </remarks>
public sealed class ExtensionsWindowService : IExtensionsWindow
{
    private ExtensionsWindow? _window;

    private double? _left;
    private double? _top;
    private double _width = 1080d;
    private double _height = 680d;

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

        var window = new ExtensionsWindow
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
