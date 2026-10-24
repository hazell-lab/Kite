using System.Text.Json;

namespace Kite.Core.Storage;

/// <summary>
/// all data kept on this computer: settings, bookmarks, aliases and history.
/// lists are swapped as a whole, so search threads always see a complete list.
/// </summary>
public sealed class LocalData
{
    private readonly JsonFileStore<LauncherSettings> _settingsStore;
    private readonly JsonFileStore<BookmarkList> _bookmarkStore;
    private readonly JsonFileStore<AliasList> _aliasStore;
    private readonly JsonFileStore<UsageHistoryData> _historyStore;

    private volatile LauncherSettings _settings = new();
    private volatile IReadOnlyList<Bookmark> _bookmarks = [];
    private volatile IReadOnlyList<Alias> _aliases = [];

    public LocalData(string folder)
    {
        Folder = folder;
        _settingsStore = new JsonFileStore<LauncherSettings>(Path.Combine(folder, "settings.json"));
        _bookmarkStore = new JsonFileStore<BookmarkList>(Path.Combine(folder, "bookmarks.json"));
        _aliasStore = new JsonFileStore<AliasList>(Path.Combine(folder, "aliases.json"));
        _historyStore = new JsonFileStore<UsageHistoryData>(Path.Combine(folder, "history.json"));
        History = new UsageHistory();
    }

    public string Folder { get; }

    public LauncherSettings Settings => _settings;

    public IReadOnlyList<Bookmark> Bookmarks => _bookmarks;

    public IReadOnlyList<Alias> Aliases => _aliases;

    public UsageHistory History { get; private set; }

    /// <summary>the default folder, inside the user's app data folder.</summary>
    public static string DefaultFolder() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Kite");

    /// <summary>load every file. returns warnings for files that had problems.</summary>
    public IReadOnlyList<string> Load()
    {
        var warnings = new List<string>();

        var settings = _settingsStore.Load();
        AddWarning(warnings, settings.Warning);
        var loadedSettings = settings.Value;
        if (loadedSettings.Normalize() && settings.Warning is null && File.Exists(_settingsStore.FilePath))
        {
            warnings.Add("Some settings were invalid and were reset to safe values.");
        }

        _settings = loadedSettings;

        var bookmarks = _bookmarkStore.Load();
        AddWarning(warnings, bookmarks.Warning);
        _bookmarks = (bookmarks.Value.Items ?? [])
            .Where(b => b is not null && !string.IsNullOrWhiteSpace(b.Name) && !string.IsNullOrWhiteSpace(b.Url))
            .ToList();

        var aliases = _aliasStore.Load();
        AddWarning(warnings, aliases.Warning);
        _aliases = (aliases.Value.Items ?? [])
            .Where(a => a is not null && !string.IsNullOrWhiteSpace(a.Keyword) && !string.IsNullOrWhiteSpace(a.Target))
            .ToList();

        var history = _historyStore.Load();
        AddWarning(warnings, history.Warning);
        History = new UsageHistory(history.Value);

        return warnings;
    }

    public void SaveSettings(LauncherSettings settings)
    {
        settings.Normalize();
        _settingsStore.Save(settings);
        _settings = settings;
    }

    public void SaveBookmarks(IEnumerable<Bookmark> bookmarks)
    {
        var list = bookmarks.ToList();
        _bookmarkStore.Save(new BookmarkList { Items = list });
        _bookmarks = list;
    }

    public void SaveAliases(IEnumerable<Alias> aliases)
    {
        var list = aliases.ToList();
        _aliasStore.Save(new AliasList { Items = list });
        _aliases = list;
    }

    public void SaveHistory() => _historyStore.Save(History.Snapshot());

    public void ClearHistory()
    {
        History.Clear();
        SaveHistory();
    }

    /// <summary>make a full copy, so the settings window can edit without changing the original.</summary>
    public static T Clone<T>(T value) =>
        JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value, JsonFileStore<LauncherSettings>.JsonOptions), JsonFileStore<LauncherSettings>.JsonOptions)!;

    private static void AddWarning(List<string> warnings, string? warning)
    {
        if (!string.IsNullOrEmpty(warning))
        {
            warnings.Add(warning);
        }
    }
}
