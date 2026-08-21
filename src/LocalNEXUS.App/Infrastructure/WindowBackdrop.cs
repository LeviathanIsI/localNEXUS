using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace LocalNEXUS.App.Infrastructure;

/// <summary>
/// Asks the desktop compositor to blur what is behind a window, so a translucent base layer over
/// it reads as frosted glass rather than as a hole.
/// </summary>
/// <remarks>
/// Three mechanisms exist for this on Windows and only one of them is both documented and current.
///
/// <c>AllowsTransparency</c> is the pure WPF answer and is the wrong one here. It forces
/// <c>WindowStyle.None</c>, which this window cannot have because its custom chrome is built on
/// <c>WindowChrome</c> and depends on the real frame for snapping, the resize border and the
/// maximised bounds. It also turns the window into a layered one, which costs the whole surface
/// its cheap presentation path, and it blurs nothing: it is see through, not frosted.
///
/// <c>SetWindowCompositionAttribute</c> with the acrylic accent state is what most of the WPF
/// acrylic packages use, and its gradient colour carries an alpha byte that would drive a slider
/// directly. It has never been documented, and it was the source of the drag lag that made acrylic
/// windows notorious on Windows 10. Building a shipped feature on it is not worth the tint control
/// it hands back, because the same control exists as an ordinary opacity on our own layer.
///
/// <c>DwmSetWindowAttribute</c> with <c>DWMWA_SYSTEMBACKDROP_TYPE</c> is documented, is what the
/// system itself uses, and needs no change to the window style at all. It arrived in Windows 11
/// 22H2, so <see cref="IsSupported"/> is a real question rather than a formality, and a build
/// without it keeps an opaque window instead of failing.
///
/// The cost is real and is not hidden. A transient backdrop is a live blur of everything behind
/// the window, recomputed by the compositor as those windows change, so it is GPU work that an
/// opaque window does not do at all. It is also why the backdrop is cleared rather than left in
/// place when transparency is turned off.
/// </remarks>
public static class WindowBackdrop
{
    /// <summary>The first Windows 11 build that honours a system backdrop request.</summary>
    private const int FirstSupportedBuild = 22621;

    private const int SystemBackdropTypeAttribute = 38;

    private const int BackdropAuto = 0;

    /// <summary>Acrylic: a blur of what is behind, rather than of the desktop wallpaper.</summary>
    private const int BackdropTransientWindow = 3;

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);

    /// <summary>True when this machine can show a system backdrop at all.</summary>
    public static bool IsSupported { get; } =
        Environment.OSVersion.Platform == PlatformID.Win32NT
        && Environment.OSVersion.Version.Major >= 10
        && Environment.OSVersion.Version.Build >= FirstSupportedBuild;

    /// <summary>
    /// Turns the backdrop on or off for a window.
    /// </summary>
    /// <returns>True when the compositor accepted the request.</returns>
    /// <remarks>
    /// Asking for the backdrop is only half of it. WPF clears its surface to an opaque colour of
    /// its own before anything is drawn on top, so a window that asked for acrylic and did nothing
    /// else renders solid instead. The composition target's clear colour is the other half, and
    /// the value it had is kept so that turning transparency off restores the window rather than
    /// leaving it clearing to whatever happened to suit the other state.
    ///
    /// The window's own background is left alone. It is transparent in the XAML for every theme,
    /// the base layer inside being what actually paints, so there is nothing here to toggle.
    /// </remarks>
    public static bool Apply(Window window, bool translucent)
    {
        if (!IsSupported)
        {
            return false;
        }

        var handle = new WindowInteropHelper(window).Handle;

        if (handle == IntPtr.Zero)
        {
            return false;
        }

        var backdrop = translucent ? BackdropTransientWindow : BackdropAuto;

        if (DwmSetWindowAttribute(handle, SystemBackdropTypeAttribute, ref backdrop, sizeof(int)) != 0)
        {
            return false;
        }

        var target = HwndSource.FromHwnd(handle)?.CompositionTarget;

        if (target is null)
        {
            return false;
        }

        if (!OpaqueClearColours.TryGetValue(handle, out var opaque))
        {
            opaque = target.BackgroundColor;
            OpaqueClearColours[handle] = opaque;
        }

        target.BackgroundColor = translucent ? Colors.Transparent : opaque;

        return true;
    }

    /// <summary>
    /// What each window's composition target cleared to before transparency was first asked for.
    /// </summary>
    private static readonly Dictionary<IntPtr, Color> OpaqueClearColours = new();
}
