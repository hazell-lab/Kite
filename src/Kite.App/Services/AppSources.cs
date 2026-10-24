using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using Kite.Core.Apps;

namespace Kite.App.Services;

/// <summary>shared rules for skipping entries that are not really apps.</summary>
internal static class AppFilters
{
    private static readonly string[] SkipWords =
    [
        "uninstall", "uninstaller", "readme", "read me", "release notes", "license", "documentation",
        "manual", "website", "web site", "changelog", "what's new",
    ];

    public static bool ShouldSkip(string name)
    {
        var lower = name.ToLowerInvariant();
        foreach (var word in SkipWords)
        {
            // match whole words only, so "helper" or "helpdesk" are kept
            var index = lower.IndexOf(word, StringComparison.Ordinal);
            while (index >= 0)
            {
                var startOk = index == 0 || !char.IsLetter(lower[index - 1]);
                var end = index + word.Length;
                var endOk = end >= lower.Length || !char.IsLetter(lower[end]);
                if (startOk && endOk)
                {
                    return true;
                }

                index = lower.IndexOf(word, index + 1, StringComparison.Ordinal);
            }
        }

        return false;
    }
}

/// <summary>finds apps from start menu shortcuts, for this user and for all users.</summary>
internal sealed class StartMenuAppSource : IAppSource
{
    private static readonly string[] Extensions = [".lnk", ".url", ".appref-ms"];

    public string Name => "Start menu";

    public static IReadOnlyList<string> Folders() =>
        new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.Programs),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms),
        }
        .Where(f => !string.IsNullOrEmpty(f) && Directory.Exists(f))
        .ToList();

    public IReadOnlyList<AppEntry> Discover(CancellationToken cancellationToken)
    {
        var options = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.System,
        };

        var apps = new List<AppEntry>();
        foreach (var folder in Folders())
        {
            foreach (var file in Directory.EnumerateFiles(folder, "*", options))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var extension = Path.GetExtension(file);
                if (!Extensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
                {
                    continue;
                }

                var name = Path.GetFileNameWithoutExtension(file);
                if (string.IsNullOrWhiteSpace(name) || AppFilters.ShouldSkip(name))
                {
                    continue;
                }

                var relativeFolder = Path.GetRelativePath(folder, Path.GetDirectoryName(file) ?? folder);
                var description = relativeFolder == "." ? "Start menu" : $"Start menu • {relativeFolder}";

                apps.Add(new AppEntry(name, file, AppLaunchKind.Path, Name)
                {
                    Description = description,
                    IconPath = file,
                });
            }
        }

        return apps;
    }
}

/// <summary>
/// finds apps from the windows apps folder. this is where store and other
/// packaged apps show up. it asks the windows shell for the list.
/// </summary>
internal sealed class AppsFolderSource : IAppSource
{
    public const string ShellPrefix = "shell:AppsFolder\\";

    public string Name => "Apps folder";

    public IReadOnlyList<AppEntry> Discover(CancellationToken cancellationToken) =>
        StaThread.Run(() => ReadAppsFolder(cancellationToken), TimeSpan.FromSeconds(20), cancellationToken);

    private static List<AppEntry> ReadAppsFolder(CancellationToken cancellationToken)
    {
        var apps = new List<AppEntry>();
        var shellType = Type.GetTypeFromProgID("Shell.Application")
                        ?? throw new InvalidOperationException("The windows shell object is not available.");

        object? shell = null;
        object? folder = null;
        object? items = null;
        try
        {
            shell = Activator.CreateInstance(shellType)
                    ?? throw new InvalidOperationException("The windows shell object could not be created.");
            folder = Invoke(shell, "NameSpace", "shell:AppsFolder")
                     ?? throw new InvalidOperationException("The apps folder could not be opened.");
            items = Invoke(folder, "Items")
                    ?? throw new InvalidOperationException("The apps folder is empty.");

            var count = Convert.ToInt32(GetProperty(items, "Count"), System.Globalization.CultureInfo.InvariantCulture);
            for (var i = 0; i < count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                object? item = null;
                try
                {
                    item = Invoke(items, "Item", i);
                    if (item is null)
                    {
                        continue;
                    }

                    var name = GetProperty(item, "Name") as string;
                    var parsingName = GetProperty(item, "Path") as string;
                    if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(parsingName) || AppFilters.ShouldSkip(name))
                    {
                        continue;
                    }

                    // packaged apps have an id like publisher.app_hash!app
                    var isPackaged = parsingName.Contains('!');
                    apps.Add(new AppEntry(name, parsingName, AppLaunchKind.Packaged, "Apps folder")
                    {
                        Description = isPackaged ? "Microsoft Store or packaged app" : "Application",
                        IconPath = ShellPrefix + parsingName,
                    });
                }
                catch (COMException)
                {
                    // skip entries the shell cannot describe
                }
                finally
                {
                    Release(item);
                }
            }
        }
        finally
        {
            Release(items);
            Release(folder);
            Release(shell);
        }

        return apps;
    }

    private static object? Invoke(object target, string method, params object[] args) =>
        target.GetType().InvokeMember(method, BindingFlags.InvokeMethod, null, target, args, System.Globalization.CultureInfo.InvariantCulture);

    private static object? GetProperty(object target, string property) =>
        target.GetType().InvokeMember(property, BindingFlags.GetProperty, null, target, null, System.Globalization.CultureInfo.InvariantCulture);

    private static void Release(object? comObject)
    {
        if (comObject is not null && Marshal.IsComObject(comObject))
        {
            Marshal.ReleaseComObject(comObject);
        }
    }
}
