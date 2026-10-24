using System.Collections.ObjectModel;
using System.Windows.Threading;
using Kite.App.Services;
using Kite.Core.Apps;
using Kite.Core.Search;
using Kite.Core.Storage;

namespace Kite.App.ViewModels;

/// <summary>which message or list the launcher shows below the search box.</summary>
public enum LauncherState
{
    /// <summary>nothing typed yet, so show tips.</summary>
    Tips,

    /// <summary>still searching or still finding apps.</summary>
    Loading,

    /// <summary>there are results to show.</summary>
    Results,

    /// <summary>the search found nothing.</summary>
    NoResults,
}

/// <summary>the data and behaviour behind the launcher window.</summary>
public sealed class LauncherViewModel : ObservableObject, IDisposable
{
    private static readonly TimeSpan DebounceDelay = TimeSpan.FromMilliseconds(60);
    private static readonly TimeSpan BusyDelay = TimeSpan.FromMilliseconds(150);

    private readonly SearchEngine _engine;
    private readonly ActionRunner _runner;
    private readonly LocalData _data;
    private readonly AppCatalog _catalog;
    private readonly ShellIconLoader _icons;
    private readonly Dispatcher _dispatcher;

    private CancellationTokenSource? _searchCancellation;
    private string _queryText = string.Empty;
    private int _selectedIndex = -1;
    private LauncherState _state = LauncherState.Tips;
    private bool _isBusy;
    private string? _errorMessage;
    private string _appStatus = string.Empty;
    private string _hotkeyText = LauncherSettings.DefaultHotkey;

    internal LauncherViewModel(
        SearchEngine engine,
        ActionRunner runner,
        LocalData data,
        AppCatalog catalog,
        ShellIconLoader icons,
        Dispatcher dispatcher)
    {
        _engine = engine;
        _runner = runner;
        _data = data;
        _catalog = catalog;
        _icons = icons;
        _dispatcher = dispatcher;
        _catalog.Changed += OnCatalogChanged;
        UpdateAppStatus();
    }

    public ObservableCollection<ResultItemViewModel> Results { get; } = [];

    public string QueryText
    {
        get => _queryText;
        set
        {
            if (SetProperty(ref _queryText, value ?? string.Empty))
            {
                OnPropertyChanged(nameof(HasQuery));
                StartSearch();
            }
        }
    }

    public bool HasQuery => !string.IsNullOrEmpty(_queryText);

    public int SelectedIndex
    {
        get => _selectedIndex;
        set
        {
            if (SetProperty(ref _selectedIndex, value))
            {
                OnPropertyChanged(nameof(SelectedItem));
            }
        }
    }

    public ResultItemViewModel? SelectedItem =>
        _selectedIndex >= 0 && _selectedIndex < Results.Count ? Results[_selectedIndex] : null;

    public LauncherState State
    {
        get => _state;
        private set
        {
            if (SetProperty(ref _state, value))
            {
                OnPropertyChanged(nameof(ShowTips));
                OnPropertyChanged(nameof(ShowLoading));
                OnPropertyChanged(nameof(ShowResults));
                OnPropertyChanged(nameof(ShowNoResults));
            }
        }
    }

    public bool ShowTips => _state == LauncherState.Tips;

    public bool ShowLoading => _state == LauncherState.Loading;

    public bool ShowResults => _state == LauncherState.Results;

    public bool ShowNoResults => _state == LauncherState.NoResults;

    public bool IsBusy
    {
        get => _isBusy;
        private set => SetProperty(ref _isBusy, value);
    }

    public string? ErrorMessage
    {
        get => _errorMessage;
        set
        {
            if (SetProperty(ref _errorMessage, value))
            {
                OnPropertyChanged(nameof(HasError));
            }
        }
    }

    public bool HasError => !string.IsNullOrEmpty(_errorMessage);

    /// <summary>a short line about the app list, shown in the footer.</summary>
    public string AppStatus
    {
        get => _appStatus;
        private set => SetProperty(ref _appStatus, value);
    }

    public bool IsFindingApps => _catalog.State is CatalogState.Loading or CatalogState.NotLoaded;

    public string HotkeyText
    {
        get => _hotkeyText;
        set => SetProperty(ref _hotkeyText, value);
    }

    /// <summary>move the highlight up or down, wrapping around at the ends.</summary>
    public void MoveSelection(int delta)
    {
        if (Results.Count == 0)
        {
            return;
        }

        var next = SelectedIndex < 0 ? 0 : (SelectedIndex + delta) % Results.Count;
        if (next < 0)
        {
            next += Results.Count;
        }

        SelectedIndex = next;
    }

    public void SelectFirst()
    {
        if (Results.Count > 0)
        {
            SelectedIndex = 0;
        }
    }

    public void SelectLast()
    {
        if (Results.Count > 0)
        {
            SelectedIndex = Results.Count - 1;
        }
    }

    /// <summary>open the highlighted result. returns true when the launcher should hide.</summary>
    public bool ActivateSelected()
    {
        var item = SelectedItem;
        if (item is null || !item.HasAction)
        {
            return false;
        }

        var outcome = _runner.Run(item.Result.Action);
        if (!outcome.Success)
        {
            ErrorMessage = outcome.Error;
            return false;
        }

        if (item.Result.TrackUsage && _data.Settings.RememberHistory)
        {
            _data.History.RecordLaunch(item.Result.Id, QueryText, DateTime.UtcNow);
            SaveHistoryInBackground();
        }

        ErrorMessage = null;
        return true;
    }

    /// <summary>run the current search again, for example after settings change.</summary>
    public void Refresh() => StartSearch();

    public void Dispose()
    {
        _catalog.Changed -= OnCatalogChanged;
        _searchCancellation?.Cancel();
        _searchCancellation?.Dispose();
        _searchCancellation = null;
    }

    private async void StartSearch()
    {
        // stop the old search before starting another
        _searchCancellation?.Cancel();
        _searchCancellation?.Dispose();
        var cancellation = new CancellationTokenSource();
        _searchCancellation = cancellation;
        var token = cancellation.Token;
        var text = _queryText;
        ErrorMessage = null;

        if (string.IsNullOrWhiteSpace(text))
        {
            Results.Clear();
            SelectedIndex = -1;
            IsBusy = false;
            State = LauncherState.Tips;
            return;
        }

        try
        {
            // wait a moment so fast typing does not start many searches
            await Task.Delay(DebounceDelay, token);

            var search = _engine.SearchAsync(new SearchQuery(text, _data.Settings.MaxResults), token);
            var finished = await Task.WhenAny(search, Task.Delay(BusyDelay, token));
            if (finished != search && !token.IsCancellationRequested)
            {
                IsBusy = true;
                if (Results.Count == 0)
                {
                    State = LauncherState.Loading;
                }
            }

            var response = await search;
            if (token.IsCancellationRequested)
            {
                return;
            }

            ShowResponse(response);
        }
        catch (OperationCanceledException)
        {
            // a newer search replaced this one
        }
        catch (Exception ex)
        {
            if (!token.IsCancellationRequested)
            {
                Log.Error("Search failed", ex);
                Results.Clear();
                SelectedIndex = -1;
                State = LauncherState.NoResults;
                ErrorMessage = $"Search failed: {ex.Message}";
            }
        }
        finally
        {
            if (ReferenceEquals(_searchCancellation, cancellation))
            {
                IsBusy = false;
            }
        }
    }

    private void ShowResponse(SearchResponse response)
    {
        Results.Clear();
        foreach (var result in response.Results)
        {
            var item = new ResultItemViewModel(result);
            Results.Add(item);
            item.LoadIcon(_icons);
        }

        // select the first result that can be opened
        var firstActive = Results.ToList().FindIndex(r => r.HasAction);
        SelectedIndex = Results.Count == 0 ? -1 : Math.Max(0, firstActive);

        // tell the list again, in case it cleared its own selection meanwhile
        OnPropertyChanged(nameof(SelectedIndex));
        OnPropertyChanged(nameof(SelectedItem));

        // while apps are still loading, a web search offer alone would be misleading
        var fallbackNames = _engine.Providers.Where(p => p.IsFallback).Select(p => p.Name).ToHashSet();
        var onlyFallback = Results.Count > 0 && Results.All(r => fallbackNames.Contains(r.Result.ProviderName));

        if (Results.Count > 0 && !(onlyFallback && IsFindingApps))
        {
            State = LauncherState.Results;
        }
        else
        {
            State = IsFindingApps ? LauncherState.Loading : LauncherState.NoResults;
        }

        if (response.Errors.Count > 0)
        {
            ErrorMessage = string.Join(" ", response.Errors);
        }
    }

    private void SaveHistoryInBackground()
    {
        _ = Task.Run(() =>
        {
            try
            {
                _data.SaveHistory();
            }
            catch (Exception ex)
            {
                Log.Error("Could not save history", ex);
            }
        });
    }

    private void OnCatalogChanged(object? sender, EventArgs e)
    {
        // the catalog changes on a background thread, so move to the window thread
        _dispatcher.InvokeAsync(() =>
        {
            UpdateAppStatus();
            if (HasQuery)
            {
                StartSearch();
            }
        });
    }

    public void UpdateAppStatus()
    {
        AppStatus = _catalog.State switch
        {
            CatalogState.Loading or CatalogState.NotLoaded => "Finding apps…",
            CatalogState.Failed => "Apps could not be listed",
            _ => $"{_catalog.Apps.Count} apps",
        };
        OnPropertyChanged(nameof(IsFindingApps));
    }
}
