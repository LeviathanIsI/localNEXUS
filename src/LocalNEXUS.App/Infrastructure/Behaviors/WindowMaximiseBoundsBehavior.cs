using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace LocalNEXUS.App.Infrastructure.Behaviors;

/// <summary>
/// Keeps a maximised window inside the work area, so it does not cover the taskbar.
/// </summary>
/// <remarks>
/// A window with an ordinary frame gets this from the operating system. This one does not have
/// one: it is <c>WindowStyle.None</c> with <c>AllowsTransparency</c>, which is what lets a theme
/// be seen through, and a window in that shape maximises to the whole monitor rather than to the
/// space left over by the shell. Answering WM_GETMINMAXINFO is the documented way to say
/// otherwise, and it has to be answered per monitor because the taskbar is only on one of them.
///
/// This replaces an inset that used to be applied to the root element when maximised. That inset
/// was compensating for the overhang a framed window has, and there is no overhang left to
/// compensate for once the bounds are the work area exactly, so keeping both would leave a strip
/// of desktop showing down every edge.
/// </remarks>
public static class WindowMaximiseBoundsBehavior
{
    private const int WmGetMinMaxInfo = 0x0024;

    private const int MonitorDefaultToNearest = 0x00000002;

    /// <summary>Set to true on a window to keep it out of the taskbar when maximised.</summary>
    public static readonly DependencyProperty IsEnabledProperty = DependencyProperty.RegisterAttached(
        "IsEnabled",
        typeof(bool),
        typeof(WindowMaximiseBoundsBehavior),
        new PropertyMetadata(false, OnIsEnabledChanged));

    public static void SetIsEnabled(DependencyObject element, bool value)
        => element.SetValue(IsEnabledProperty, value);

    public static bool GetIsEnabled(DependencyObject element)
        => (bool)element.GetValue(IsEnabledProperty);

    private static void OnIsEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not Window window || e.NewValue is not true)
        {
            return;
        }

        if (new WindowInteropHelper(window).Handle != IntPtr.Zero)
        {
            Hook(window);
            return;
        }

        window.SourceInitialized += OnSourceInitialized;
    }

    private static void OnSourceInitialized(object? sender, EventArgs e)
    {
        if (sender is not Window window)
        {
            return;
        }

        window.SourceInitialized -= OnSourceInitialized;
        Hook(window);
    }

    private static void Hook(Window window)
    {
        var handle = new WindowInteropHelper(window).Handle;
        HwndSource.FromHwnd(handle)?.AddHook(WindowProc);
    }

    private static IntPtr WindowProc(IntPtr handle, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message != WmGetMinMaxInfo)
        {
            return IntPtr.Zero;
        }

        var monitor = MonitorFromWindow(handle, MonitorDefaultToNearest);

        if (monitor == IntPtr.Zero)
        {
            return IntPtr.Zero;
        }

        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };

        if (!GetMonitorInfo(monitor, ref info))
        {
            return IntPtr.Zero;
        }

        var minMax = Marshal.PtrToStructure<MinMaxInfo>(lParam);

        // Positions are relative to the monitor rather than to the desktop, which is what makes
        // this correct on a secondary monitor whose origin is not zero.
        minMax.MaxPosition.X = info.Work.Left - info.Monitor.Left;
        minMax.MaxPosition.Y = info.Work.Top - info.Monitor.Top;
        minMax.MaxSize.X = info.Work.Right - info.Work.Left;
        minMax.MaxSize.Y = info.Work.Bottom - info.Work.Top;
        minMax.MaxTrackSize.X = minMax.MaxSize.X;
        minMax.MaxTrackSize.Y = minMax.MaxSize.Y;

        Marshal.StructureToPtr(minMax, lParam, fDeleteOld: true);

        handled = true;
        return IntPtr.Zero;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr window, int flags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public int Size;
        public Rect Monitor;
        public Rect Work;
        public int Flags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Point
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MinMaxInfo
    {
        public Point Reserved;
        public Point MaxSize;
        public Point MaxPosition;
        public Point MinTrackSize;
        public Point MaxTrackSize;
    }
}
