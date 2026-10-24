using System.IO;
using System.Windows;
using System.Windows.Interop;
using Kite.App.Interop;
using Kite.Core.Storage;
using Microsoft.Win32;

namespace Kite.App.Services;

/// <summary>switches between the dark and light colours.</summary>
internal sealed class ThemeService : IDisposable
{
    private const string PersonalizeKey = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
    private ThemeChoice _choice = ThemeChoice.Dark;
    private ResourceDictionary? _current;

    public ThemeService()
    {
        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
    }

    /// <summary>raised after the colours change.</summary>
    public event EventHandler? ThemeChanged;

    public bool IsDark { get; private set; } = true;

    public void Apply(ThemeChoice choice)
    {
        _choice = choice;
        var dark = choice switch
        {
            ThemeChoice.Light => false,
            ThemeChoice.System => !SystemUsesLightTheme(),
            _ => true,
        };

        if (_current is not null && dark == IsDark)
        {
            return;
        }

        IsDark = dark;
        var dictionary = new ResourceDictionary
        {
            Source = new Uri(dark ? "pack://application:,,,/Themes/Dark.xaml" : "pack://application:,,,/Themes/Light.xaml"),
        };

        var merged = Application.Current.Resources.MergedDictionaries;
        if (_current is not null)
        {
            merged.Remove(_current);
        }

        merged.Insert(0, dictionary);
        _current = dictionary;
        ThemeChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>give a normal window a dark or light title bar to match.</summary>
    public void ApplyTitleBar(Window window)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero)
        {
            return;
        }

        var value = IsDark ? 1 : 0;

        // older windows builds ignore this, which is fine
        NativeMethods.DwmSetWindowAttribute(handle, NativeMethods.DWMWA_USE_IMMERSIVE_DARK_MODE, ref value, sizeof(int));
    }

    public void Dispose()
    {
        SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
    }

    private static bool SystemUsesLightTheme()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(PersonalizeKey);
            return key?.GetValue("AppsUseLightTheme") is int value && value != 0;
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            return false;
        }
    }

    private void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (_choice != ThemeChoice.System || e.Category != UserPreferenceCategory.General)
        {
            return;
        }

        // this event can arrive on another thread
        Application.Current?.Dispatcher.InvokeAsync(() => Apply(_choice));
    }
}
