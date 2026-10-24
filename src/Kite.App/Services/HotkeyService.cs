using System.Runtime.InteropServices;
using System.Windows.Input;
using System.Windows.Interop;
using Kite.App.Interop;
using Kite.Core.Hotkeys;

namespace Kite.App.Services;

/// <summary>
/// asks windows for one shortcut that works in every app, and raises
/// pressed when it is used. a hidden window receives the key message.
/// </summary>
internal sealed class HotkeyService : IDisposable
{
    private const int HotkeyId = 0x4B49;
    private static readonly IntPtr MessageOnlyParent = new(-3);

    private readonly HwndSource _source;
    private bool _registered;
    private bool _disposed;

    public HotkeyService()
    {
        var parameters = new HwndSourceParameters("KiteHotkeyWindow")
        {
            ParentWindow = MessageOnlyParent,
            WindowStyle = 0,
        };
        _source = new HwndSource(parameters);
        _source.AddHook(WndProc);
    }

    public event EventHandler? Pressed;

    /// <summary>the shortcut that is currently active, if any.</summary>
    public HotkeyGesture? Current { get; private set; }

    /// <summary>turn a key name like space into the number windows uses.</summary>
    public static bool TryGetVirtualKey(string keyName, out uint virtualKey, out string? error)
    {
        virtualKey = 0;
        if (!Enum.TryParse<Key>(keyName, ignoreCase: true, out var key) || key == Key.None)
        {
            error = $"\"{keyName}\" is not a key Kite knows.";
            return false;
        }

        var code = KeyInterop.VirtualKeyFromKey(key);
        if (code == 0)
        {
            error = $"\"{keyName}\" cannot be used in a shortcut.";
            return false;
        }

        virtualKey = (uint)code;
        error = null;
        return true;
    }

    /// <summary>
    /// try to use a new shortcut. if windows refuses it, the old shortcut
    /// stays active and the error explains why.
    /// </summary>
    public bool TryRegister(HotkeyGesture gesture, out string? error)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!TryGetVirtualKey(gesture.Key, out var virtualKey, out error))
        {
            return false;
        }

        var previous = Current;
        UnregisterCurrent();

        if (RegisterRaw(gesture, virtualKey, out var errorCode))
        {
            Current = gesture;
            error = null;
            return true;
        }

        error = errorCode == NativeMethods.ERROR_HOTKEY_ALREADY_REGISTERED
            ? $"{gesture} is already used by Windows or another app. Choose a different shortcut."
            : $"Windows did not accept {gesture} (error {errorCode}). Choose a different shortcut.";

        // put the old shortcut back so the launcher stays reachable
        if (previous is not null && TryGetVirtualKey(previous.Key, out var oldKey, out _) && RegisterRaw(previous, oldKey, out _))
        {
            Current = previous;
        }

        return false;
    }

    /// <summary>stop listening for a moment, for example while the user records a new shortcut.</summary>
    public void Suspend() => UnregisterCurrentKeepingGesture();

    /// <summary>listen again after suspend.</summary>
    public void Resume()
    {
        if (_registered || Current is null || _disposed)
        {
            return;
        }

        if (TryGetVirtualKey(Current.Key, out var virtualKey, out _))
        {
            RegisterRaw(Current, virtualKey, out _);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        UnregisterCurrent();
        _source.RemoveHook(WndProc);
        _source.Dispose();
        _disposed = true;
    }

    private bool RegisterRaw(HotkeyGesture gesture, uint virtualKey, out int errorCode)
    {
        var modifiers = (uint)gesture.Modifiers | NativeMethods.MOD_NOREPEAT;
        if (NativeMethods.RegisterHotKey(_source.Handle, HotkeyId, modifiers, virtualKey))
        {
            _registered = true;
            errorCode = 0;
            return true;
        }

        errorCode = Marshal.GetLastWin32Error();
        return false;
    }

    private void UnregisterCurrent()
    {
        UnregisterCurrentKeepingGesture();
        Current = null;
    }

    private void UnregisterCurrentKeepingGesture()
    {
        if (_registered)
        {
            NativeMethods.UnregisterHotKey(_source.Handle, HotkeyId);
            _registered = false;
        }
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == NativeMethods.WM_HOTKEY && wParam.ToInt32() == HotkeyId)
        {
            handled = true;
            Pressed?.Invoke(this, EventArgs.Empty);
        }

        return IntPtr.Zero;
    }
}
