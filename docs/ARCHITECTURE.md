# How Kite is put together

Kite has two code projects and one test project:

| Project | What it holds | Depends on Windows? |
|---|---|---|
| `src/Kite.Core` | Search engine, ranking, calculator, settings files, web search, bookmarks, aliases | No. Plain .NET, so it can be tested anywhere. |
| `src/Kite.App` | The WPF windows, tray icon, global shortcut, app discovery, icons, launching | Yes |
| `tests/Kite.Core.Tests` | xUnit tests for ranking, calculator parsing and settings persistence | No |

## Main files

### Kite.Core

| File | Purpose |
|---|---|
| `Search/ISearchProvider.cs` | The interface every search source implements. |
| `Search/SearchModels.cs` | `SearchResult`, `SearchQuery`, `ResultAction` and the icon and action kinds. |
| `Search/SearchEngine.cs` | Runs all providers in parallel, adds history points, removes duplicates, sorts and trims. Runs *fallback* providers only when nothing else matched. |
| `Search/FuzzyMatcher.cs` | Scores a query against a name: exact, prefix, word start, acronym, substring, letters in order, then small typos. |
| `Calculation/ExpressionEvaluator.cs` | A hand-written math parser. It only understands numbers, operators, brackets and a short list of functions, so it can never run code. |
| `Providers/*.cs` | Apps, calculator, web search (plus the fallback), bookmarks, aliases and built-in commands. |
| `Apps/AppCatalog.cs` | Keeps the app list in memory. It is rebuilt only on startup, when the Start menu changes, when you ask, or when it is over 30 minutes old and the launcher opens. Never per keystroke. |
| `Storage/LocalData.cs` | Loads and saves `settings.json`, `bookmarks.json`, `aliases.json` and `history.json`. |
| `Storage/JsonFileStore.cs` | Safe JSON file reading and writing. Writes go to a temp file first. A damaged file is copied to `*.damaged-<time>.json` and defaults are used. |
| `Storage/UsageHistory.cs` | Counts launches and which result you picked for which query. The boost is capped so an exact name match always stays on top. |
| `Hotkeys/HotkeyGesture.cs` | Parses and validates shortcut text such as `Alt+Space`. |
| `Launching/TargetValidator.cs` | Checks web addresses (http and https only) and alias paths before anything is opened. |

### Kite.App

| File | Purpose |
|---|---|
| `App.xaml.cs` | Startup and shutdown. Creates every service, wires events, and removes them again on exit. |
| `Views/LauncherWindow.xaml(.cs)` | The floating search bar: layout, keyboard handling, show and hide, animations, screen reader announcements. |
| `Views/SettingsWindow.xaml(.cs)` | Settings, bookmarks, aliases and web search engines. Uses the Windows 11 Fluent look built into WPF. |
| `ViewModels/LauncherViewModel.cs` | Debounces typing, cancels old searches, decides which state to show (tips, loading, results, no matches, error). |
| `Services/HotkeyService.cs` | Registers the global shortcut with the Win32 `RegisterHotKey` function and restores the old one if a new one is refused. |
| `Services/AppSources.cs` | Finds apps from Start menu shortcuts and from the Windows apps folder (which includes Store apps). |
| `Services/ShellIconLoader.cs` | Loads real app icons on one background thread, with a cache. |
| `Services/ActionRunner.cs` | Opens results. Uses the Windows shell to open files and web addresses directly, never `cmd.exe`. |
| `Services/MonitorService.cs` | Picks the monitor and places the window near its top centre, in real pixels, so display scaling is handled. |
| `Services/TrayIconService.cs` | Tray icon and its menu. |
| `Services/SingleInstance.cs` | Stops a second copy from running and asks the first copy to show itself. |
| `Services/StartupRegistration.cs` | The "start when I sign in" setting, stored in the current user's `Run` registry key. |
| `Services/ThemeService.cs` | Swaps between `Themes/Dark.xaml` and `Themes/Light.xaml`, and follows Windows when set to "Match Windows". |
| `Themes/Controls.xaml` | Shared styles for the launcher. |

## How a search flows

1. You type. `LauncherViewModel` waits 60 ms, cancels any older search, and calls `SearchEngine.SearchAsync`.
2. Each provider runs on the thread pool, so a slow one never freezes the window.
3. The engine adds history points, merges results with the same `Id`, sorts by score and keeps `MaxResults`.
4. If nothing matched, fallback providers run (the default one offers a web search, which only opens if you choose it).
5. The window shows the results, and icons load in the background.
6. Enter calls `ActionRunner.Run`, which checks the target again before opening it.

### Score guide

Higher wins. These numbers keep the important results on top:

| Score | Result |
|---|---|
| 1300 | Calculator answer |
| 1200 | Web search with a prefix, like `g weather` |
| 1100 | Alias typed in full |
| 1000 | Exact name match |
| 850–900 | Name starts with the query |
| 750–800 | A word in the name starts with the query, or the initials match |
| 600–700 | Query is inside the name |
| 300–500 | Letters in order, or a small typo |
| +0–90 | Launch history boost |

## Adding a search provider

Say you want typing `time` to show the current time, and Enter to copy it.

**1. Create the provider in `src/Kite.Core/Providers/ClockProvider.cs`:**

```csharp
using System.Globalization;
using Kite.Core.Search;

namespace Kite.Core.Providers;

/// <summary>shows the current date and time.</summary>
public sealed class ClockProvider : ISearchProvider
{
    public string Name => "Clock";

    public Task<IReadOnlyList<SearchResult>> SearchAsync(SearchQuery query, CancellationToken cancellationToken)
    {
        // only answer when the query looks like a clock question
        var score = Math.Max(FuzzyMatcher.Score(query.Text, "time"), FuzzyMatcher.Score(query.Text, "date"));
        if (score < FuzzyMatcher.Prefix)
        {
            return Task.FromResult<IReadOnlyList<SearchResult>>([]);
        }

        var now = DateTime.Now.ToString("f", CultureInfo.CurrentCulture);
        SearchResult result = new()
        {
            Id = "clock:now",
            Title = now,
            Subtitle = "Current date and time • press Enter to copy",
            Icon = new ResultIcon(IconKind.Info),
            Score = score,
            Action = new ResultAction(ActionKind.CopyText, now),
            ActionLabel = "Copy",
            ProviderName = Name,
            TrackUsage = false,
        };
        return Task.FromResult<IReadOnlyList<SearchResult>>([result]);
    }
}
```

**2. Register it in `src/Kite.App/App.xaml.cs`**, in the list passed to `new SearchEngine(...)`:

```csharp
new ClockProvider(),
```

That's all. The window, keyboard handling, ranking and history work with it automatically.

**Tips**

- Give each result a stable `Id`. History uses it to learn what you open.
- Set `TrackUsage = false` for results that change every time, like answers or searches.
- Use the existing `ActionKind` values. If you need a new kind of action, add it to `ActionKind` and handle it in `Services/ActionRunner.cs`.
- Return `IsFallback => true` for providers that should only appear when nothing else matched.
- Check `cancellationToken` in long loops, since a new keystroke cancels the old search.
- Add a test in `tests/Kite.Core.Tests`.

## Ideas for later

- **File search.** Add a provider backed by the Windows Search index (the `SystemIndex` OLE DB catalog) or by a small index of chosen folders. It would plug in as one more `ISearchProvider`, with no window changes.
- **Secondary actions**, like Ctrl+Enter to open an app's file location.
- **Signed installer**, for example MSIX, so SmartScreen does not warn on first run.
