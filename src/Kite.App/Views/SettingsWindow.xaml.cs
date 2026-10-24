using System.IO;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Kite.App.Services;
using Kite.Core.Apps;
using Kite.Core.Hotkeys;
using Kite.Core.Launching;
using Kite.Core.Storage;
using Microsoft.Win32;

namespace Kite.App.Views;

/// <summary>everything the settings window needs from the rest of the app.</summary>
internal sealed record SettingsContext(
    LocalData Data,
    HotkeyService Hotkeys,
    ThemeService Themes,
    AppCatalog Catalog,
    Action<LauncherSettings> SettingsSaved,
    Func<Task> ReloadApps);

/// <summary>the settings window. closing it leaves kite running in the tray.</summary>
public partial class SettingsWindow : Window
{
    private readonly SettingsContext _context;
    private readonly List<Bookmark> _bookmarks;
    private readonly List<Alias> _aliases;
    private List<WebSearchEngine> _engines;
    private bool _loading = true;
    private Bookmark? _editingBookmark;
    private Alias? _editingAlias;
    private WebSearchEngine? _editingEngine;
    private string? _pendingHotkey;

    internal SettingsWindow(SettingsContext context)
    {
        _context = context;
        _bookmarks = context.Data.Bookmarks.Select(CopyOf).ToList();
        _aliases = context.Data.Aliases.Select(CopyOf).ToList();
        _engines = context.Data.Settings.SearchEngines.Select(CopyOf).ToList();

        InitializeComponent();

        AliasKindBox.ItemsSource = Enum.GetValues<AliasKind>();
        AliasKindBox.SelectedItem = AliasKind.Folder;
        BookmarkList.ItemsSource = _bookmarks;
        AliasList.ItemsSource = _aliases;
        LoadGeneral();
        RefreshEngines();
        UpdateAppCount();
        DataFolderText.Text = context.Data.Folder;
        _loading = false;

        ApplyWindowTheme();
        SourceInitialized += (_, _) => _context.Themes.ApplyTitleBar(this);
        _context.Themes.ThemeChanged += OnThemeChanged;
        _context.Catalog.Changed += OnCatalogChanged;
    }

    /// <summary>open on a tab: 0 general, 1 bookmarks, 2 aliases, 3 web search.</summary>
    public void SelectTab(int index)
    {
        if (index >= 0 && index < Tabs.Items.Count)
        {
            Tabs.SelectedIndex = index;
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        _context.Themes.ThemeChanged -= OnThemeChanged;
        _context.Catalog.Changed -= OnCatalogChanged;

        // make sure the shortcut works again if the window closed while recording
        _context.Hotkeys.Resume();
        base.OnClosed(e);
    }

    private static Bookmark CopyOf(Bookmark b) => new() { Id = b.Id, Name = b.Name, Url = b.Url };

    private static Alias CopyOf(Alias a) => new() { Id = a.Id, Keyword = a.Keyword, Kind = a.Kind, Target = a.Target };

    private static WebSearchEngine CopyOf(WebSearchEngine e) => new() { Prefix = e.Prefix, Name = e.Name, UrlTemplate = e.UrlTemplate };

    private static void ShowStatus(TextBlock target, string message, bool isError)
    {
        target.Text = message;
        target.Style = (Style)Application.Current.FindResource(isError ? "ErrorTextStyle" : "SuccessTextStyle");
        target.Visibility = Visibility.Visible;
    }

    private void ApplyWindowTheme()
    {
        ThemeMode = _context.Themes.IsDark ? ThemeMode.Dark : ThemeMode.Light;
        _context.Themes.ApplyTitleBar(this);
    }

    private void OnThemeChanged(object? sender, EventArgs e) => ApplyWindowTheme();

    private void OnCatalogChanged(object? sender, EventArgs e) => Dispatcher.InvokeAsync(UpdateAppCount);

    private void UpdateAppCount()
    {
        var catalog = _context.Catalog;
        AppCountText.Text = catalog.State switch
        {
            CatalogState.Loading or CatalogState.NotLoaded => "Looking for apps…",
            CatalogState.Failed => "Apps could not be listed. " + string.Join(" ", catalog.LastErrors),
            _ when catalog.LastErrors.Count > 0 => $"{catalog.Apps.Count} apps found. Some sources had problems: {string.Join(" ", catalog.LastErrors)}",
            _ => $"{catalog.Apps.Count} apps found.",
        };
    }

    // general tab

    private void LoadGeneral()
    {
        var settings = _context.Data.Settings;
        HotkeyBox.Text = settings.Hotkey;

        foreach (ComboBoxItem item in ThemeBox.Items)
        {
            if ((string)item.Tag == settings.Theme.ToString())
            {
                ThemeBox.SelectedItem = item;
            }
        }

        var monitors = MonitorService.Options().ToList();
        if (!monitors.Any(m => m.Value == settings.Monitor))
        {
            // keep a saved monitor that is unplugged right now
            monitors.Add(new MonitorOption(settings.Monitor, "Saved monitor (not connected)"));
        }

        MonitorBox.ItemsSource = monitors;
        MonitorBox.SelectedValue = settings.Monitor;
        AnimationsBox.IsChecked = settings.Animations;
        ResultCountSlider.Value = settings.MaxResults;
        ResultCountText.Text = settings.MaxResults.ToString(System.Globalization.CultureInfo.CurrentCulture);
        HistoryBox.IsChecked = settings.RememberHistory;
        StartWithWindowsBox.IsChecked = StartupRegistration.IsEnabled() || settings.StartWithWindows;
        ShowOnStartBox.IsChecked = settings.ShowLauncherOnStart;
    }

    private void OnGeneralChanged(object sender, RoutedEventArgs e) => SaveGeneral();

    private void OnGeneralSelectionChanged(object sender, SelectionChangedEventArgs e) => SaveGeneral();

    private void OnResultCountChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (ResultCountText is not null)
        {
            ResultCountText.Text = ((int)e.NewValue).ToString(System.Globalization.CultureInfo.CurrentCulture);
        }

        SaveGeneral();
    }

    private void SaveGeneral()
    {
        if (_loading)
        {
            return;
        }

        var current = _context.Data.Settings;
        var updated = LocalData.Clone(current);
        if (ThemeBox.SelectedItem is ComboBoxItem { Tag: string themeTag } && Enum.TryParse<ThemeChoice>(themeTag, out var theme))
        {
            updated.Theme = theme;
        }

        updated.Monitor = MonitorBox.SelectedValue as string ?? LauncherSettings.MonitorWithCursor;
        updated.Animations = AnimationsBox.IsChecked == true;
        updated.MaxResults = (int)ResultCountSlider.Value;
        updated.RememberHistory = HistoryBox.IsChecked == true;
        updated.StartWithWindows = StartWithWindowsBox.IsChecked == true;
        updated.ShowLauncherOnStart = ShowOnStartBox.IsChecked == true;

        if (!TrySave(updated, StartupStatus))
        {
            return;
        }

        StartupStatus.Visibility = Visibility.Collapsed;
        var startupError = StartupRegistration.Apply(updated.StartWithWindows);
        if (startupError is not null)
        {
            ShowStatus(StartupStatus, startupError, isError: true);
        }
    }

    /// <summary>save settings and tell the app. shows an error and returns false if saving fails.</summary>
    private bool TrySave(LauncherSettings settings, TextBlock statusTarget)
    {
        try
        {
            _context.Data.SaveSettings(settings);
            _context.SettingsSaved(settings);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Error("Could not save settings", ex);
            ShowStatus(statusTarget, $"Could not save settings: {ex.Message}", isError: true);
            return false;
        }
    }

    private void OnHotkeyBoxGotFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        // pause the real shortcut so pressing it here records it instead of opening kite
        _context.Hotkeys.Suspend();
    }

    private void OnHotkeyBoxLostFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        _context.Hotkeys.Resume();
    }

    private void OnHotkeyBoxKeyDown(object sender, KeyEventArgs e)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        var modifiers = Keyboard.Modifiers;

        // let tab move to the next control as usual
        if (key == Key.Tab && (modifiers == ModifierKeys.None || modifiers == ModifierKeys.Shift))
        {
            return;
        }

        e.Handled = true;
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift
            or Key.LWin or Key.RWin or Key.ImeProcessed or Key.DeadCharProcessed or Key.None)
        {
            return;
        }

        var parts = new List<string>();
        if (modifiers.HasFlag(ModifierKeys.Control))
        {
            parts.Add("Ctrl");
        }

        if (modifiers.HasFlag(ModifierKeys.Alt))
        {
            parts.Add("Alt");
        }

        if (modifiers.HasFlag(ModifierKeys.Shift))
        {
            parts.Add("Shift");
        }

        if (modifiers.HasFlag(ModifierKeys.Windows))
        {
            parts.Add("Win");
        }

        parts.Add(key.ToString());
        _pendingHotkey = string.Join("+", parts);
        HotkeyBox.Text = _pendingHotkey;

        if (HotkeyGesture.TryParse(_pendingHotkey, out _, out var error))
        {
            ShowStatus(HotkeyStatus, "Choose Apply to start using this shortcut.", isError: false);
        }
        else
        {
            ShowStatus(HotkeyStatus, error ?? "This shortcut cannot be used.", isError: true);
        }
    }

    private void OnApplyHotkey(object sender, RoutedEventArgs e) => ApplyHotkey(_pendingHotkey ?? HotkeyBox.Text);

    private void OnResetHotkey(object sender, RoutedEventArgs e)
    {
        HotkeyBox.Text = LauncherSettings.DefaultHotkey;
        ApplyHotkey(LauncherSettings.DefaultHotkey);
    }

    private void ApplyHotkey(string text)
    {
        if (!HotkeyGesture.TryParse(text, out var gesture, out var parseError) || gesture is null)
        {
            ShowStatus(HotkeyStatus, parseError ?? "This shortcut cannot be used.", isError: true);
            return;
        }

        if (!_context.Hotkeys.TryRegister(gesture, out var registerError))
        {
            var fallback = _context.Hotkeys.Current is { } current ? $" Kite still uses {current}." : string.Empty;
            ShowStatus(HotkeyStatus, (registerError ?? "Windows did not accept this shortcut.") + fallback, isError: true);
            return;
        }

        var updated = LocalData.Clone(_context.Data.Settings);
        updated.Hotkey = gesture.ToString();
        if (TrySave(updated, HotkeyStatus))
        {
            HotkeyBox.Text = updated.Hotkey;
            _pendingHotkey = null;
            ShowStatus(HotkeyStatus, $"Done. Press {updated.Hotkey} to open Kite.", isError: false);
        }
    }

    private void OnClearHistory(object sender, RoutedEventArgs e)
    {
        var answer = MessageBox.Show(
            this,
            "Forget which results you opened before? Ranking goes back to name matching only.",
            "Clear launch history",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question,
            MessageBoxResult.No);
        if (answer != MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            _context.Data.ClearHistory();
            ShowStatus(HistoryStatus, "History cleared.", isError: false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ShowStatus(HistoryStatus, $"Could not clear history: {ex.Message}", isError: true);
        }
    }

    private async void OnReloadApps(object sender, RoutedEventArgs e)
    {
        AppCountText.Text = "Looking for apps…";
        try
        {
            await _context.ReloadApps();
        }
        catch (Exception ex)
        {
            Log.Error("Reload apps failed", ex);
        }

        UpdateAppCount();
    }

    private void OnOpenDataFolder(object sender, RoutedEventArgs e)
    {
        try
        {
            Directory.CreateDirectory(_context.Data.Folder);
            using var process = Process.Start(new ProcessStartInfo(_context.Data.Folder) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is IOException or System.ComponentModel.Win32Exception or UnauthorizedAccessException)
        {
            MessageBox.Show(this, $"Could not open the folder: {ex.Message}", "Kite", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    // bookmarks tab

    private void OnBookmarkSelected(object sender, SelectionChangedEventArgs e)
    {
        if (BookmarkList.SelectedItem is not Bookmark bookmark)
        {
            return;
        }

        _editingBookmark = bookmark;
        BookmarkNameBox.Text = bookmark.Name;
        BookmarkUrlBox.Text = bookmark.Url;
        BookmarkStatus.Text = string.Empty;
    }

    private void OnNewBookmark(object sender, RoutedEventArgs e)
    {
        _editingBookmark = null;
        BookmarkList.SelectedItem = null;
        BookmarkNameBox.Text = string.Empty;
        BookmarkUrlBox.Text = string.Empty;
        BookmarkStatus.Text = string.Empty;
        BookmarkNameBox.Focus();
    }

    private void OnSaveBookmark(object sender, RoutedEventArgs e)
    {
        var name = BookmarkNameBox.Text.Trim();
        var url = TargetValidator.NormalizeWebAddress(BookmarkUrlBox.Text);
        if (name.Length == 0)
        {
            ShowStatus(BookmarkStatus, "Give the bookmark a name.", isError: true);
            return;
        }

        if (url is null)
        {
            ShowStatus(BookmarkStatus, "Enter a web address, for example https://example.com.", isError: true);
            return;
        }

        var target = _editingBookmark ?? new Bookmark();
        target.Name = name;
        target.Url = url;
        if (_editingBookmark is null)
        {
            _bookmarks.Add(target);
        }

        if (PersistBookmarks())
        {
            BookmarkList.SelectedItem = target;
            BookmarkUrlBox.Text = url;
            ShowStatus(BookmarkStatus, "Bookmark saved.", isError: false);
        }
    }

    private void OnDeleteBookmark(object sender, RoutedEventArgs e)
    {
        if (_editingBookmark is null)
        {
            ShowStatus(BookmarkStatus, "Choose a bookmark in the list first.", isError: true);
            return;
        }

        _bookmarks.Remove(_editingBookmark);
        if (PersistBookmarks())
        {
            OnNewBookmark(sender, e);
            ShowStatus(BookmarkStatus, "Bookmark deleted.", isError: false);
        }
    }

    private bool PersistBookmarks()
    {
        BookmarkList.Items.Refresh();
        try
        {
            _context.Data.SaveBookmarks(_bookmarks.Select(CopyOf));
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ShowStatus(BookmarkStatus, $"Could not save bookmarks: {ex.Message}", isError: true);
            return false;
        }
    }

    // aliases tab

    private void OnAliasSelected(object sender, SelectionChangedEventArgs e)
    {
        if (AliasList.SelectedItem is not Alias alias)
        {
            return;
        }

        _editingAlias = alias;
        AliasKeywordBox.Text = alias.Keyword;
        AliasKindBox.SelectedItem = alias.Kind;
        AliasTargetBox.Text = alias.Target;

        // warn early when the target has gone missing
        var check = TargetValidator.Check(alias.Kind, alias.Target);
        if (check.IsValid)
        {
            AliasStatus.Text = string.Empty;
        }
        else
        {
            ShowStatus(AliasStatus, check.Error!, isError: true);
        }
    }

    private void OnNewAlias(object sender, RoutedEventArgs e)
    {
        _editingAlias = null;
        AliasList.SelectedItem = null;
        AliasKeywordBox.Text = string.Empty;
        AliasTargetBox.Text = string.Empty;
        AliasKindBox.SelectedItem = AliasKind.Folder;
        AliasStatus.Text = string.Empty;
        AliasKeywordBox.Focus();
    }

    private void OnBrowseAliasTarget(object sender, RoutedEventArgs e)
    {
        var kind = AliasKindBox.SelectedItem is AliasKind selected ? selected : AliasKind.Folder;
        switch (kind)
        {
            case AliasKind.Folder:
            {
                var dialog = new OpenFolderDialog { Title = "Choose the folder this alias opens" };
                if (dialog.ShowDialog(this) == true)
                {
                    AliasTargetBox.Text = dialog.FolderName;
                }

                break;
            }

            case AliasKind.Application:
            case AliasKind.File:
            {
                var dialog = new OpenFileDialog
                {
                    Title = "Choose what this alias opens",
                    Filter = kind == AliasKind.Application
                        ? "Programs and shortcuts|*.exe;*.lnk;*.appref-ms;*.url|All files|*.*"
                        : "All files|*.*",
                    DereferenceLinks = false,
                    CheckFileExists = true,
                };
                if (dialog.ShowDialog(this) == true)
                {
                    AliasTargetBox.Text = dialog.FileName;
                }

                break;
            }

            default:
                ShowStatus(AliasStatus, "For a website, type its address in the box.", isError: false);
                AliasTargetBox.Focus();
                break;
        }
    }

    private void OnSaveAlias(object sender, RoutedEventArgs e)
    {
        var keyword = AliasKeywordBox.Text.Trim();
        var kind = AliasKindBox.SelectedItem is AliasKind selected ? selected : AliasKind.Folder;
        if (keyword.Length == 0 || keyword.Length > 40)
        {
            ShowStatus(AliasStatus, "Enter an alias of 1 to 40 characters.", isError: true);
            return;
        }

        if (_aliases.Any(a => a != _editingAlias && string.Equals(a.Keyword, keyword, StringComparison.OrdinalIgnoreCase)))
        {
            ShowStatus(AliasStatus, $"The alias \"{keyword}\" already exists.", isError: true);
            return;
        }

        var check = TargetValidator.Check(kind, AliasTargetBox.Text);
        if (!check.IsValid)
        {
            ShowStatus(AliasStatus, check.Error!, isError: true);
            return;
        }

        var target = _editingAlias ?? new Alias();
        target.Keyword = keyword;
        target.Kind = kind;
        target.Target = kind == AliasKind.Website ? check.ResolvedTarget : AliasTargetBox.Text.Trim().Trim('"');
        if (_editingAlias is null)
        {
            _aliases.Add(target);
        }

        if (PersistAliases())
        {
            AliasList.SelectedItem = target;
            ShowStatus(AliasStatus, $"Saved. Type {keyword} in Kite to open it.", isError: false);
        }
    }

    private void OnDeleteAlias(object sender, RoutedEventArgs e)
    {
        if (_editingAlias is null)
        {
            ShowStatus(AliasStatus, "Choose an alias in the list first.", isError: true);
            return;
        }

        _aliases.Remove(_editingAlias);
        if (PersistAliases())
        {
            OnNewAlias(sender, e);
            ShowStatus(AliasStatus, "Alias deleted.", isError: false);
        }
    }

    private bool PersistAliases()
    {
        AliasList.Items.Refresh();
        try
        {
            _context.Data.SaveAliases(_aliases.Select(CopyOf));
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ShowStatus(AliasStatus, $"Could not save aliases: {ex.Message}", isError: true);
            return false;
        }
    }

    // web search tab

    private void RefreshEngines()
    {
        var wasLoading = _loading;
        _loading = true;
        EngineList.ItemsSource = null;
        EngineList.ItemsSource = _engines;
        FallbackBox.ItemsSource = null;
        FallbackBox.ItemsSource = _engines;
        FallbackBox.SelectedValue = _context.Data.Settings.FallbackSearchPrefix;
        _loading = wasLoading;
    }

    private void OnEngineSelected(object sender, SelectionChangedEventArgs e)
    {
        if (EngineList.SelectedItem is not WebSearchEngine engine)
        {
            return;
        }

        _editingEngine = engine;
        EnginePrefixBox.Text = engine.Prefix;
        EngineNameBox.Text = engine.Name;
        EngineUrlBox.Text = engine.UrlTemplate;
        EngineStatus.Text = string.Empty;
    }

    private void OnNewEngine(object sender, RoutedEventArgs e)
    {
        _editingEngine = null;
        EngineList.SelectedItem = null;
        EnginePrefixBox.Text = string.Empty;
        EngineNameBox.Text = string.Empty;
        EngineUrlBox.Text = "https://";
        EngineStatus.Text = string.Empty;
        EnginePrefixBox.Focus();
    }

    private void OnSaveEngine(object sender, RoutedEventArgs e)
    {
        var candidate = new WebSearchEngine
        {
            Prefix = EnginePrefixBox.Text.Trim().ToLowerInvariant(),
            Name = EngineNameBox.Text.Trim(),
            UrlTemplate = EngineUrlBox.Text.Trim(),
        };

        if (!LauncherSettings.IsValidEngine(candidate))
        {
            ShowStatus(EngineStatus, "Fill in all fields. The prefix cannot contain spaces, and the address must start with https:// and contain {query}.", isError: true);
            return;
        }

        if (_engines.Any(x => x != _editingEngine && x.Prefix == candidate.Prefix))
        {
            ShowStatus(EngineStatus, $"The prefix \"{candidate.Prefix}\" is already used.", isError: true);
            return;
        }

        var previousEngines = _engines.Select(CopyOf).ToList();
        if (_editingEngine is null)
        {
            _engines.Add(candidate);
        }
        else
        {
            _editingEngine.Prefix = candidate.Prefix;
            _editingEngine.Name = candidate.Name;
            _editingEngine.UrlTemplate = candidate.UrlTemplate;
        }

        if (PersistEngines(previousEngines))
        {
            ShowStatus(EngineStatus, $"Saved. Type \"{candidate.Prefix} something\" in Kite to search {candidate.Name}.", isError: false);
        }
    }

    private void OnDeleteEngine(object sender, RoutedEventArgs e)
    {
        if (_editingEngine is null)
        {
            ShowStatus(EngineStatus, "Choose a search engine in the list first.", isError: true);
            return;
        }

        if (_engines.Count == 1)
        {
            ShowStatus(EngineStatus, "Keep at least one search engine.", isError: true);
            return;
        }

        var previousEngines = _engines.Select(CopyOf).ToList();
        _engines.Remove(_editingEngine);
        if (PersistEngines(previousEngines))
        {
            OnNewEngine(sender, e);
            ShowStatus(EngineStatus, "Search engine deleted.", isError: false);
        }
    }

    private void OnResetEngines(object sender, RoutedEventArgs e)
    {
        var previousEngines = _engines.Select(CopyOf).ToList();
        _engines = LauncherSettings.DefaultSearchEngines();
        if (PersistEngines(previousEngines))
        {
            OnNewEngine(sender, e);
            ShowStatus(EngineStatus, "Default search engines restored.", isError: false);
        }
    }

    private void OnFallbackChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || FallbackBox.SelectedValue is not string prefix)
        {
            return;
        }

        var updated = LocalData.Clone(_context.Data.Settings);
        updated.FallbackSearchPrefix = prefix;
        TrySave(updated, EngineStatus);
    }

    /// <summary>save the engine list. on failure the old list comes back.</summary>
    private bool PersistEngines(List<WebSearchEngine> previousEngines)
    {
        var updated = LocalData.Clone(_context.Data.Settings);
        updated.SearchEngines = _engines.Select(CopyOf).ToList();
        if (!TrySave(updated, EngineStatus))
        {
            _engines = previousEngines;
            RefreshEngines();
            return false;
        }

        // saving may tidy the list, so show what was really saved
        _engines = _context.Data.Settings.SearchEngines.Select(CopyOf).ToList();
        _editingEngine = null;
        RefreshEngines();
        return true;
    }
}
