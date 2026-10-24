using System.IO;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using Kite.App.Services;
using Kite.App.ViewModels;
using Kite.App.Views;
using Kite.Core.Apps;
using Kite.Core.Hotkeys;
using Kite.Core.Providers;
using Kite.Core.Search;
using Kite.Core.Storage;

namespace Kite.App;

/// <summary>starts kite, connects all the parts and cleans up on exit.</summary>
public partial class App : Application
{
    private static readonly TimeSpan StaleAppListAge = TimeSpan.FromMinutes(30);

    private readonly CancellationTokenSource _lifetime = new();
    private readonly List<FileSystemWatcher> _watchers = [];

    private SingleInstance? _instance;
    private LocalData? _data;
    private AppCatalog? _catalog;
    private ShellIconLoader? _icons;
    private ActionRunner? _runner;
    private ThemeService? _themes;
    private HotkeyService? _hotkeys;
    private TrayIconService? _tray;
    private LauncherViewModel? _viewModel;
    private LauncherWindow? _launcher;
    private SettingsWindow? _settingsWindow;
    private DispatcherTimer? _reloadTimer;

    /// <summary>true once kite has started to close for real.</summary>
    public bool IsShuttingDown { get; private set; }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _instance = new SingleInstance();
        if (!_instance.IsFirstInstance)
        {
            // kite is already running, so ask it to show the launcher and leave
            SingleInstance.SignalFirstInstance();
            IsShuttingDown = true;
            Shutdown();
            return;
        }

        DispatcherUnhandledException += OnDispatcherUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

        var folder = LocalData.DefaultFolder();
        Log.Initialize(folder);
        var data = new LocalData(folder);
        _data = data;
        var warnings = new List<string>();
        try
        {
            warnings.AddRange(data.Load());
        }
        catch (Exception ex)
        {
            // keep going with default values rather than crashing
            Log.Error("Loading saved data failed", ex);
            warnings.Add($"Saved data could not be loaded, so defaults are used: {ex.Message}");
        }

        foreach (var warning in warnings)
        {
            Log.Info(warning);
        }

        _themes = new ThemeService();
        _themes.Apply(data.Settings.Theme);

        _catalog = new AppCatalog([new StartMenuAppSource(), new AppsFolderSource()]);
        _icons = new ShellIconLoader();
        _runner = new ActionRunner();
        _runner.CommandRequested += OnCommandRequested;

        // the order here does not change ranking, scores decide that
        var engine = new SearchEngine(
            [
                new CalculatorProvider(),
                new WebSearchProvider(() => data.Settings),
                new AliasProvider(() => data.Aliases),
                new AppSearchProvider(_catalog),
                new BookmarkProvider(() => data.Bookmarks),
                new CommandProvider(),
                new WebFallbackProvider(() => data.Settings),
            ],
            () => data.Settings.RememberHistory ? data.History : null);

        _viewModel = new LauncherViewModel(engine, _runner, data, _catalog, _icons, Dispatcher);
        _launcher = new LauncherWindow(_viewModel, () => data.Settings);
        _launcher.SettingsRequested += OnLauncherSettingsRequested;
        new WindowInteropHelper(_launcher).EnsureHandle();

        _tray = new TrayIconService();
        _tray.OpenRequested += OnTrayOpen;
        _tray.SettingsRequested += OnTraySettings;
        _tray.QuitRequested += OnTrayQuit;

        _hotkeys = new HotkeyService();
        _hotkeys.Pressed += OnHotkeyPressed;
        var hotkeyReady = RegisterSavedHotkey();

        _instance.ListenForShowRequests(() => Dispatcher.InvokeAsync(ShowLauncher));

        _reloadTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        _reloadTimer.Tick += OnReloadTimerTick;
        WatchStartMenu();
        _ = RefreshAppsAsync();

        var startupError = StartupRegistration.Apply(data.Settings.StartWithWindows);
        if (startupError is not null)
        {
            warnings.Add(startupError);
        }

        var startedAtSignIn = e.Args.Contains(StartupRegistration.BackgroundArgument, StringComparer.OrdinalIgnoreCase);
        if (warnings.Count > 0)
        {
            _tray.ShowMessage("Kite started with a problem", string.Join(" ", warnings), isWarning: true);
        }
        else if (hotkeyReady && !startedAtSignIn)
        {
            _tray.ShowMessage("Kite is running", $"Press {_hotkeys.Current} to search. Right click this icon for settings.", isWarning: false);
        }

        if (data.Settings.ShowLauncherOnStart)
        {
            ShowLauncher();
        }
    }

    protected override void OnSessionEnding(SessionEndingCancelEventArgs e)
    {
        // windows is signing out or shutting down, so let every window close
        IsShuttingDown = true;
        base.OnSessionEnding(e);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        IsShuttingDown = true;
        _lifetime.Cancel();

        foreach (var watcher in _watchers)
        {
            watcher.EnableRaisingEvents = false;
            watcher.Created -= OnStartMenuChanged;
            watcher.Deleted -= OnStartMenuChanged;
            watcher.Changed -= OnStartMenuChanged;
            watcher.Renamed -= OnStartMenuChanged;
            watcher.Dispose();
        }

        _watchers.Clear();

        if (_reloadTimer is not null)
        {
            _reloadTimer.Stop();
            _reloadTimer.Tick -= OnReloadTimerTick;
        }

        if (_hotkeys is not null)
        {
            _hotkeys.Pressed -= OnHotkeyPressed;
            _hotkeys.Dispose();
        }

        if (_tray is not null)
        {
            _tray.OpenRequested -= OnTrayOpen;
            _tray.SettingsRequested -= OnTraySettings;
            _tray.QuitRequested -= OnTrayQuit;
            _tray.Dispose();
        }

        if (_runner is not null)
        {
            _runner.CommandRequested -= OnCommandRequested;
        }

        if (_launcher is not null)
        {
            _launcher.SettingsRequested -= OnLauncherSettingsRequested;
        }

        _viewModel?.Dispose();
        _icons?.Dispose();
        _themes?.Dispose();

        try
        {
            _data?.SaveHistory();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Error("Could not save history on exit", ex);
        }

        DispatcherUnhandledException -= OnDispatcherUnhandledException;
        TaskScheduler.UnobservedTaskException -= OnUnobservedTaskException;
        _instance?.Dispose();
        _lifetime.Dispose();
        base.OnExit(e);
    }

    private void Quit()
    {
        IsShuttingDown = true;
        _settingsWindow?.Close();
        _launcher?.Close();
        Shutdown();
    }

    private void ShowLauncher()
    {
        if (_launcher is null || _catalog is null || IsShuttingDown)
        {
            return;
        }

        // pick up apps installed since the last look
        if (_catalog.State == CatalogState.Ready && DateTime.UtcNow - _catalog.LastRefreshUtc > StaleAppListAge)
        {
            _ = RefreshAppsAsync();
        }

        _launcher.ShowLauncher();
    }

    private void ToggleLauncher()
    {
        if (_launcher is { IsVisible: true, IsActive: true })
        {
            _launcher.HideLauncher();
        }
        else
        {
            ShowLauncher();
        }
    }

    private void OpenSettings()
    {
        if (_data is null || _hotkeys is null || _themes is null || _catalog is null)
        {
            return;
        }

        _launcher?.HideLauncher();
        if (_settingsWindow is not null)
        {
            if (_settingsWindow.WindowState == WindowState.Minimized)
            {
                _settingsWindow.WindowState = WindowState.Normal;
            }

            _settingsWindow.Activate();
            return;
        }

        var context = new SettingsContext(_data, _hotkeys, _themes, _catalog, OnSettingsSaved, RefreshAppsAsync);
        _settingsWindow = new SettingsWindow(context);
        _settingsWindow.Closed += OnSettingsClosed;
        _settingsWindow.Show();
        _settingsWindow.Activate();
    }

    private void OnSettingsClosed(object? sender, EventArgs e)
    {
        if (_settingsWindow is not null)
        {
            _settingsWindow.Closed -= OnSettingsClosed;
            _settingsWindow = null;
        }
    }

    private void OnSettingsSaved(LauncherSettings settings)
    {
        _themes?.Apply(settings.Theme);
        _tray?.SetShortcutText(_hotkeys?.Current?.ToString());
        if (_viewModel is not null)
        {
            _viewModel.HotkeyText = _hotkeys?.Current?.ToString() ?? string.Empty;
            _viewModel.Refresh();
        }
    }

    /// <summary>use the saved shortcut. if windows refuses it, explain and offer to pick another.</summary>
    private bool RegisterSavedHotkey()
    {
        if (_data is null || _hotkeys is null || _tray is null)
        {
            return false;
        }

        if (!HotkeyGesture.TryParse(_data.Settings.Hotkey, out var gesture, out _) || gesture is null)
        {
            HotkeyGesture.TryParse(LauncherSettings.DefaultHotkey, out gesture, out _);
        }

        string? error = null;
        if (gesture is not null && _hotkeys.TryRegister(gesture, out error))
        {
            _tray.SetShortcutText(gesture.ToString());
            if (_viewModel is not null)
            {
                _viewModel.HotkeyText = gesture.ToString();
            }

            return true;
        }

        error ??= "The shortcut could not be registered.";
        Log.Info(error);
        _tray.SetShortcutText(null);

        // wait until startup has finished before asking
        Dispatcher.InvokeAsync(
            () =>
            {
                var answer = MessageBox.Show(
                    $"{error}\n\nKite is still running. You can open it from its icon in the notification area.\n\nDo you want to choose a different shortcut now?",
                    "Kite shortcut not available",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning,
                    MessageBoxResult.Yes);
                if (answer == MessageBoxResult.Yes)
                {
                    OpenSettings();
                }
            },
            DispatcherPriority.ApplicationIdle);
        return false;
    }

    private async Task RefreshAppsAsync()
    {
        if (_catalog is null)
        {
            return;
        }

        try
        {
            await _catalog.RefreshAsync(_lifetime.Token);
            foreach (var problem in _catalog.LastErrors)
            {
                Log.Info($"App discovery: {problem}");
            }
        }
        catch (OperationCanceledException)
        {
            // kite is closing
        }
        catch (Exception ex)
        {
            Log.Error("App discovery failed", ex);
        }
        finally
        {
            _viewModel?.UpdateAppStatus();
        }
    }

    private void WatchStartMenu()
    {
        foreach (var folder in StartMenuAppSource.Folders())
        {
            try
            {
                var watcher = new FileSystemWatcher(folder)
                {
                    IncludeSubdirectories = true,
                    NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite,
                };
                watcher.Created += OnStartMenuChanged;
                watcher.Deleted += OnStartMenuChanged;
                watcher.Changed += OnStartMenuChanged;
                watcher.Renamed += OnStartMenuChanged;
                watcher.EnableRaisingEvents = true;
                _watchers.Add(watcher);
            }
            catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException)
            {
                // the app list still refreshes when the launcher opens later
                Log.Error($"Could not watch {folder}", ex);
            }
        }
    }

    private void OnStartMenuChanged(object sender, FileSystemEventArgs e)
    {
        // installs change many files at once, so wait for things to settle
        Dispatcher.InvokeAsync(() =>
        {
            if (_reloadTimer is null || IsShuttingDown)
            {
                return;
            }

            _reloadTimer.Stop();
            _reloadTimer.Start();
        });
    }

    private void OnReloadTimerTick(object? sender, EventArgs e)
    {
        _reloadTimer?.Stop();
        _ = RefreshAppsAsync();
    }

    private void OnCommandRequested(object? sender, string commandId)
    {
        // run after the launcher has hidden, so dialogs do not fight for focus
        Dispatcher.InvokeAsync(
            () =>
            {
                switch (commandId)
                {
                    case CommandProvider.OpenSettings:
                        OpenSettings();
                        break;
                    case CommandProvider.ReloadApps:
                        _ = RefreshAppsAsync();
                        _tray?.ShowMessage("Kite", "Looking for newly installed apps.", isWarning: false);
                        break;
                    case CommandProvider.ClearHistory:
                        ConfirmClearHistory();
                        break;
                    case CommandProvider.Quit:
                        Quit();
                        break;
                }
            },
            DispatcherPriority.Background);
    }

    private void ConfirmClearHistory()
    {
        if (_data is null)
        {
            return;
        }

        var answer = MessageBox.Show(
            "Forget which results you opened before?",
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
            _data.ClearHistory();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show($"Could not clear history: {ex.Message}", "Kite", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void OnHotkeyPressed(object? sender, EventArgs e) => ToggleLauncher();

    private void OnTrayOpen(object? sender, EventArgs e) => ShowLauncher();

    private void OnTraySettings(object? sender, EventArgs e) => OpenSettings();

    private void OnTrayQuit(object? sender, EventArgs e) => Quit();

    private void OnLauncherSettingsRequested(object? sender, EventArgs e) => OpenSettings();

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Log.Error("Unexpected error", e.Exception);

        // keep running for most errors so the tray icon and shortcut keep working
        e.Handled = true;
        _tray?.ShowMessage("Kite hit a problem", $"{e.Exception.Message} Details are in kite.log in the data folder.", isWarning: true);
    }

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        Log.Error("Background task error", e.Exception);
        e.SetObserved();
    }
}
