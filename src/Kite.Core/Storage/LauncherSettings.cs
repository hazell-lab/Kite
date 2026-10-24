using Kite.Core.Hotkeys;

namespace Kite.Core.Storage;

/// <summary>all user settings. saved as settings.json.</summary>
public sealed class LauncherSettings
{
    public const string MonitorWithCursor = "cursor";
    public const string PrimaryMonitor = "primary";
    public const string DefaultHotkey = "Alt+Space";
    public const int MinResults = 3;
    public const int MaxResultsLimit = 20;

    public string Hotkey { get; set; } = DefaultHotkey;

    public ThemeChoice Theme { get; set; } = ThemeChoice.Dark;

    /// <summary>"cursor", "primary", or a monitor device name.</summary>
    public string Monitor { get; set; } = MonitorWithCursor;

    public int MaxResults { get; set; } = 8;

    public bool StartWithWindows { get; set; }

    public bool ShowLauncherOnStart { get; set; }

    public bool Animations { get; set; } = true;

    public bool RememberHistory { get; set; } = true;

    /// <summary>prefix of the engine used when nothing else matches.</summary>
    public string FallbackSearchPrefix { get; set; } = "g";

    public List<WebSearchEngine> SearchEngines { get; set; } = DefaultSearchEngines();

    public static List<WebSearchEngine> DefaultSearchEngines() =>
    [
        new() { Prefix = "g", Name = "Google", UrlTemplate = "https://www.google.com/search?q={query}" },
        new() { Prefix = "gh", Name = "GitHub", UrlTemplate = "https://github.com/search?q={query}&type=repositories" },
        new() { Prefix = "ddg", Name = "DuckDuckGo", UrlTemplate = "https://duckduckgo.com/?q={query}" },
        new() { Prefix = "yt", Name = "YouTube", UrlTemplate = "https://www.youtube.com/results?search_query={query}" },
        new() { Prefix = "w", Name = "Wikipedia", UrlTemplate = "https://en.wikipedia.org/w/index.php?search={query}" },
    ];

    /// <summary>
    /// fix values that are missing or out of range, for example after
    /// someone edits the file by hand. returns true when anything changed.
    /// </summary>
    public bool Normalize()
    {
        var changed = false;

        if (!HotkeyGesture.TryParse(Hotkey, out _, out _))
        {
            Hotkey = DefaultHotkey;
            changed = true;
        }

        if (!Enum.IsDefined(Theme))
        {
            Theme = ThemeChoice.Dark;
            changed = true;
        }

        if (string.IsNullOrWhiteSpace(Monitor))
        {
            Monitor = MonitorWithCursor;
            changed = true;
        }

        var clamped = Math.Clamp(MaxResults, MinResults, MaxResultsLimit);
        if (clamped != MaxResults)
        {
            MaxResults = clamped;
            changed = true;
        }

        var validEngines = (SearchEngines ?? [])
            .Where(e => e is not null && IsValidEngine(e))
            .GroupBy(e => e.Prefix.Trim().ToLowerInvariant())
            .Select(g => g.First())
            .ToList();
        if (SearchEngines is null || validEngines.Count != SearchEngines.Count)
        {
            changed = true;
        }

        if (validEngines.Count == 0)
        {
            validEngines = DefaultSearchEngines();
            changed = true;
        }

        foreach (var engine in validEngines)
        {
            engine.Prefix = engine.Prefix.Trim().ToLowerInvariant();
            engine.Name = engine.Name.Trim();
        }

        SearchEngines = validEngines;

        if (string.IsNullOrWhiteSpace(FallbackSearchPrefix) ||
            !SearchEngines.Any(e => e.Prefix == FallbackSearchPrefix.Trim().ToLowerInvariant()))
        {
            FallbackSearchPrefix = SearchEngines[0].Prefix;
            changed = true;
        }

        return changed;
    }

    /// <summary>check that a web search engine can be used safely.</summary>
    public static bool IsValidEngine(WebSearchEngine engine)
    {
        if (string.IsNullOrWhiteSpace(engine.Prefix) ||
            string.IsNullOrWhiteSpace(engine.Name) ||
            string.IsNullOrWhiteSpace(engine.UrlTemplate))
        {
            return false;
        }

        if (engine.Prefix.Trim().Contains(' ') || !engine.UrlTemplate.Contains("{query}", StringComparison.Ordinal))
        {
            return false;
        }

        var sample = engine.UrlTemplate.Replace("{query}", "test", StringComparison.Ordinal);
        return Launching.TargetValidator.IsWebAddress(sample);
    }
}
