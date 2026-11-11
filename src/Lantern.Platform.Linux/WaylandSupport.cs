// Decisions about Wayland that only depend on environment values, kept apart from the Linux services so the app can ask
// them on every platform and tests can feed them values.

namespace Lantern.Platform.Linux;

public static class WaylandSupport
{
    /// <summary>
    /// Whether the native Wayland backend (Avalonia.Wayland) is opted into: on Linux, with <c>LANTERN_USE_WAYLAND=1</c>
    /// and a Wayland display to connect to. Otherwise Lantern runs through X11 or XWayland.
    /// </summary>
    public static bool IsNativeWaylandRequested(bool isLinux, string? lanternUseWayland, string? waylandDisplay) =>
        isLinux
        && string.Equals(lanternUseWayland, "1", StringComparison.Ordinal)
        && !string.IsNullOrEmpty(waylandDisplay);

    /// <summary><see cref="IsNativeWaylandRequested(bool, string?, string?)"/> for this process.</summary>
    public static bool NativeWaylandRequested => IsNativeWaylandRequested(OperatingSystem.IsLinux(),
        Environment.GetEnvironmentVariable("LANTERN_USE_WAYLAND"), Environment.GetEnvironmentVariable("WAYLAND_DISPLAY"));

    /// <summary>
    /// Whether the compositor has a way to put the donut at the cursor: KDE Plasma (the KWin script) or Hyprland (a
    /// window rule), told by <c>XDG_CURRENT_DESKTOP</c> or <c>HYPRLAND_INSTANCE_SIGNATURE</c>.
    /// </summary>
    public static bool IsSupportedCompositor(string? xdgCurrentDesktop, string? hyprlandSignature)
    {
        var desktops = (xdgCurrentDesktop ?? "").Split(':');
        return desktops.Contains("KDE", StringComparer.OrdinalIgnoreCase)
            || desktops.Contains("Hyprland", StringComparer.OrdinalIgnoreCase)
            || !string.IsNullOrEmpty(hyprlandSignature);
    }
}
