using System.IO;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using Kite.App.Interop;
using Kite.Core.Launching;
using Kite.Core.Search;

namespace Kite.App.Services;

/// <summary>what happened when a result was activated.</summary>
internal sealed record ActionOutcome(bool Success, string? Error)
{
    public static readonly ActionOutcome Done = new(true, null);

    public static ActionOutcome Failed(string error) => new(false, error);
}

/// <summary>
/// carries out result actions. text typed in the search bar is never run as a
/// command. paths and web addresses are checked and handed to windows directly,
/// never through a command prompt.
/// </summary>
internal sealed class ActionRunner
{
    /// <summary>raised for the launcher's own commands, like opening settings.</summary>
    public event EventHandler<string>? CommandRequested;

    public ActionOutcome Run(ResultAction action)
    {
        try
        {
            return action.Kind switch
            {
                ActionKind.None => ActionOutcome.Failed("There is nothing to open for this item."),
                ActionKind.OpenUrl => OpenUrl(action.Target),
                ActionKind.OpenFolder => OpenFolder(action.Target),
                ActionKind.OpenPath => OpenPath(action.Target),
                ActionKind.OpenPackagedApp => OpenPackagedApp(action.Target),
                ActionKind.CopyText => CopyText(action.Target),
                ActionKind.RunCommand => RunCommand(action.Target),
                _ => ActionOutcome.Failed("This action is not supported."),
            };
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == NativeMethods.ERROR_CANCELLED)
        {
            // the user said no to an admin prompt, which is not an error
            return ActionOutcome.Done;
        }
        catch (Win32Exception ex)
        {
            Log.Error($"Could not open {action.Target}", ex);
            return ActionOutcome.Failed($"Windows could not open this item: {ex.Message}");
        }
        catch (InvalidOperationException ex)
        {
            Log.Error($"Could not open {action.Target}", ex);
            return ActionOutcome.Failed($"Could not open this item: {ex.Message}");
        }
    }

    private static ActionOutcome OpenUrl(string url)
    {
        if (!TargetValidator.IsWebAddress(url))
        {
            return ActionOutcome.Failed("Only http and https web addresses can be opened.");
        }

        ShellOpen(url, workingDirectory: null);
        return ActionOutcome.Done;
    }

    private static ActionOutcome OpenFolder(string path)
    {
        if (!Directory.Exists(path))
        {
            return ActionOutcome.Failed($"The folder was not found: {path}");
        }

        ShellOpen(path, workingDirectory: null);
        return ActionOutcome.Done;
    }

    private static ActionOutcome OpenPath(string path)
    {
        if (Directory.Exists(path))
        {
            return OpenFolder(path);
        }

        if (!File.Exists(path))
        {
            return ActionOutcome.Failed($"The file was not found: {path}. It may have been moved or uninstalled.");
        }

        ShellOpen(path, Path.GetDirectoryName(path));
        return ActionOutcome.Done;
    }

    private static ActionOutcome OpenPackagedApp(string appId)
    {
        if (string.IsNullOrWhiteSpace(appId) || appId.IndexOfAny(['"', '\r', '\n']) >= 0)
        {
            return ActionOutcome.Failed("This app id is not valid.");
        }

        // explorer opens apps folder entries, including store apps.
        // the id goes in as one argument, so no shell parsing happens.
        var explorer = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe");
        var startInfo = new ProcessStartInfo(explorer)
        {
            UseShellExecute = false,
        };
        startInfo.ArgumentList.Add(AppsFolderSource.ShellPrefix + appId);
        using var process = Process.Start(startInfo);
        return ActionOutcome.Done;
    }

    private static ActionOutcome CopyText(string text)
    {
        // the clipboard can be busy for a moment when another app uses it
        for (var attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                Clipboard.SetText(text);
                return ActionOutcome.Done;
            }
            catch (ExternalException)
            {
                Thread.Sleep(40);
            }
        }

        return ActionOutcome.Failed("The clipboard is busy. Please try again.");
    }

    private ActionOutcome RunCommand(string commandId)
    {
        CommandRequested?.Invoke(this, commandId);
        return ActionOutcome.Done;
    }

    /// <summary>let windows open a path or web address with its default program.</summary>
    private static void ShellOpen(string target, string? workingDirectory)
    {
        var startInfo = new ProcessStartInfo(target)
        {
            UseShellExecute = true,
        };
        if (!string.IsNullOrEmpty(workingDirectory))
        {
            startInfo.WorkingDirectory = workingDirectory;
        }

        using var process = Process.Start(startInfo);
    }
}
