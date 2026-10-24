using System.IO;
using Microsoft.Win32;

namespace Kite.App.Services;

/// <summary>turns "start kite when i sign in" on or off for the current user.</summary>
internal static class StartupRegistration
{
    public const string BackgroundArgument = "--background";
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "Kite";

    /// <summary>the command windows runs at sign in.</summary>
    private static string? Command()
    {
        var exe = Environment.ProcessPath;
        return string.IsNullOrEmpty(exe) ? null : $"\"{exe}\" {BackgroundArgument}";
    }

    public static bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
        return key?.GetValue(ValueName) is string;
    }

    /// <summary>add or remove the startup entry. returns an error message or null.</summary>
    public static string? Apply(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true);
            if (enabled)
            {
                var command = Command();
                if (command is null)
                {
                    return "Could not find Kite's program file.";
                }

                // only write when it changed, for example after moving the app
                if (key.GetValue(ValueName) as string != command)
                {
                    key.SetValue(ValueName, command, RegistryValueKind.String);
                }
            }
            else if (key.GetValue(ValueName) is not null)
            {
                key.DeleteValue(ValueName, throwOnMissingValue: false);
            }

            return null;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException)
        {
            Log.Error("Could not change the startup setting", ex);
            return $"Could not change the startup setting: {ex.Message}";
        }
    }
}
