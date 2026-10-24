using Kite.Core.Search;

namespace Kite.Core.Apps;

/// <summary>how an app is started.</summary>
public enum AppLaunchKind
{
    /// <summary>a shortcut or program file that windows can open directly.</summary>
    Path,

    /// <summary>a store or packaged app, started through the apps folder.</summary>
    Packaged,
}

/// <summary>one installed app.</summary>
public sealed record AppEntry(string Name, string Target, AppLaunchKind LaunchKind, string Source)
{
    /// <summary>a short line shown under the name.</summary>
    public string Description { get; init; } = string.Empty;

    /// <summary>a path or shell name used to load the app icon.</summary>
    public string? IconPath { get; init; }

    public string Id => "app:" + Target.ToLowerInvariant();
}

/// <summary>one place to look for installed apps.</summary>
public interface IAppSource
{
    string Name { get; }

    /// <summary>find apps. this runs on a background thread.</summary>
    IReadOnlyList<AppEntry> Discover(CancellationToken cancellationToken);
}

public enum CatalogState
{
    NotLoaded,
    Loading,
    Ready,
    Failed,
}

/// <summary>
/// keeps the list of installed apps in memory. the list is only rebuilt
/// when asked, never while the user types.
/// </summary>
public sealed class AppCatalog
{
    private readonly IReadOnlyList<IAppSource> _sources;
    private readonly SemaphoreSlim _refreshLock = new(1, 1);
    private volatile IReadOnlyList<AppEntry> _apps = [];
    private volatile CatalogState _state = CatalogState.NotLoaded;

    public AppCatalog(IEnumerable<IAppSource> sources)
    {
        _sources = sources.ToList();
    }

    /// <summary>raised on a background thread after the list changes.</summary>
    public event EventHandler? Changed;

    public IReadOnlyList<AppEntry> Apps => _apps;

    public CatalogState State => _state;

    public DateTime LastRefreshUtc { get; private set; }

    /// <summary>problems from the last refresh, one line per source.</summary>
    public IReadOnlyList<string> LastErrors { get; private set; } = [];

    /// <summary>rebuild the list. a second call while one is running just waits.</summary>
    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        if (!await _refreshLock.WaitAsync(0, cancellationToken).ConfigureAwait(false))
        {
            // a refresh is already running, wait for it instead of starting another
            await _refreshLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            _refreshLock.Release();
            return;
        }

        try
        {
            if (_apps.Count == 0)
            {
                _state = CatalogState.Loading;
            }

            var errors = new List<string>();
            var merged = new List<AppEntry>();
            var seenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var seenTargets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // earlier sources win when two sources find the same app
            foreach (var source in _sources)
            {
                cancellationToken.ThrowIfCancellationRequested();
                IReadOnlyList<AppEntry> found;
                try
                {
                    found = await Task.Run(() => source.Discover(cancellationToken), cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    errors.Add($"{source.Name}: {ex.Message}");
                    continue;
                }

                foreach (var app in found)
                {
                    var nameKey = FuzzyMatcher.Normalize(app.Name);
                    if (nameKey.Length == 0 || seenNames.Contains(nameKey) || !seenTargets.Add(app.Target))
                    {
                        continue;
                    }

                    seenNames.Add(nameKey);
                    merged.Add(app);
                }
            }

            merged.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
            _apps = merged;
            LastErrors = errors;
            LastRefreshUtc = DateTime.UtcNow;
            _state = merged.Count == 0 && errors.Count > 0 ? CatalogState.Failed : CatalogState.Ready;
        }
        catch (OperationCanceledException)
        {
            if (_apps.Count == 0)
            {
                _state = CatalogState.NotLoaded;
            }

            throw;
        }
        finally
        {
            _refreshLock.Release();
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }
}
