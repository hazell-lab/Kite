using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using Kite.App.Interop;
using Kite.Core.Storage;
using Forms = System.Windows.Forms;

namespace Kite.App.Services;

/// <summary>one monitor choice for the settings list.</summary>
internal sealed record MonitorOption(string Value, string Label);

/// <summary>picks a monitor and places the launcher near its top centre.</summary>
internal static class MonitorService
{
    /// <summary>how far down the screen the launcher sits, as a share of the height.</summary>
    private const double TopShare = 0.2;

    public static IReadOnlyList<MonitorOption> Options()
    {
        var options = new List<MonitorOption>
        {
            new(LauncherSettings.MonitorWithCursor, "Monitor with the mouse pointer"),
            new(LauncherSettings.PrimaryMonitor, "Primary monitor"),
        };

        var number = 1;
        foreach (var screen in Forms.Screen.AllScreens)
        {
            var primary = screen.Primary ? ", primary" : string.Empty;
            options.Add(new MonitorOption(
                screen.DeviceName,
                $"Display {number} ({screen.Bounds.Width} × {screen.Bounds.Height}{primary})"));
            number++;
        }

        return options;
    }

    /// <summary>
    /// move the window so it is centred near the top of the chosen monitor.
    /// all numbers here are real screen pixels, so scaling is handled by windows.
    /// </summary>
    public static void PlaceWindow(Window window, string monitorChoice)
    {
        var handle = new WindowInteropHelper(window).EnsureHandle();
        var monitor = FindMonitor(monitorChoice);
        var info = new NativeMethods.MONITORINFOEX { cbSize = Marshal.SizeOf<NativeMethods.MONITORINFOEX>() };
        if (monitor == IntPtr.Zero || !NativeMethods.GetMonitorInfo(monitor, ref info))
        {
            return;
        }

        var work = info.rcWork;
        var scale = 1.0;
        if (NativeMethods.GetDpiForMonitor(monitor, NativeMethods.MDT_EFFECTIVE_DPI, out var dpiX, out _) == 0 && dpiX > 0)
        {
            scale = dpiX / 96.0;
        }

        // use the real window width when it already sits on this monitor, otherwise estimate it
        var widthPixels = (int)Math.Round(window.Width * scale);
        if (NativeMethods.GetWindowRect(handle, out var rect) && rect.Width > 0 &&
            NativeMethods.MonitorFromPoint(new NativeMethods.POINT(rect.Left + (rect.Width / 2), rect.Top + 1), NativeMethods.MONITOR_DEFAULTTONEAREST) == monitor)
        {
            widthPixels = rect.Width;
        }

        var x = work.Left + ((work.Width - widthPixels) / 2);
        var y = work.Top + (int)(work.Height * TopShare);
        NativeMethods.SetWindowPos(
            handle,
            IntPtr.Zero,
            x,
            y,
            0,
            0,
            NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOACTIVATE);
    }

    private static IntPtr FindMonitor(string choice)
    {
        if (choice == LauncherSettings.PrimaryMonitor)
        {
            return NativeMethods.MonitorFromPoint(new NativeMethods.POINT(0, 0), NativeMethods.MONITOR_DEFAULTTOPRIMARY);
        }

        if (choice != LauncherSettings.MonitorWithCursor)
        {
            var screen = Forms.Screen.AllScreens.FirstOrDefault(s => string.Equals(s.DeviceName, choice, StringComparison.OrdinalIgnoreCase));
            if (screen is not null)
            {
                var center = new NativeMethods.POINT(
                    screen.Bounds.Left + (screen.Bounds.Width / 2),
                    screen.Bounds.Top + (screen.Bounds.Height / 2));
                return NativeMethods.MonitorFromPoint(center, NativeMethods.MONITOR_DEFAULTTONEAREST);
            }

            // the saved monitor is gone, so fall back to the pointer
        }

        NativeMethods.GetCursorPos(out var cursor);
        return NativeMethods.MonitorFromPoint(cursor, NativeMethods.MONITOR_DEFAULTTONEAREST);
    }
}
