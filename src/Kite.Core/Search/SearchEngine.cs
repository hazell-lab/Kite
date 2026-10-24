using Kite.Core.Storage;

namespace Kite.Core.Search;

/// <summary>
/// asks every provider for results, adds history points, sorts them
/// and keeps the best ones.
/// </summary>
public sealed class SearchEngine
{
    private readonly IReadOnlyList<ISearchProvider> _providers;
    private readonly Func<UsageHistory?> _history;
    private readonly Func<DateTime> _clock;

    public SearchEngine(IEnumerable<ISearchProvider> providers, Func<UsageHistory?> history, Func<DateTime>? clock = null)
    {
        _providers = providers.ToList();
        _history = history;
        _clock = clock ?? (() => DateTime.UtcNow);
    }

    public IReadOnlyList<ISearchProvider> Providers => _providers;

    public async Task<SearchResponse> SearchAsync(SearchQuery query, CancellationToken cancellationToken)
    {
        if (query.IsEmpty)
        {
            return new SearchResponse(query, [], []);
        }

        var errors = new List<string>();
        var results = await RunProvidersAsync(_providers.Where(p => !p.IsFallback), query, errors, cancellationToken)
            .ConfigureAwait(false);

        if (results.Count == 0)
        {
            results = await RunProvidersAsync(_providers.Where(p => p.IsFallback), query, errors, cancellationToken)
                .ConfigureAwait(false);
        }

        cancellationToken.ThrowIfCancellationRequested();
        return new SearchResponse(query, Rank(results, query), errors);
    }

    /// <summary>add history points, remove duplicates, sort and cut to size.</summary>
    internal IReadOnlyList<SearchResult> Rank(IEnumerable<SearchResult> results, SearchQuery query)
    {
        var history = _history();
        var now = _clock();

        return results
            .Select(r => r.TrackUsage && history is not null
                ? r with { Score = r.Score + history.GetBoost(r.Id, query.Text, now) }
                : r)
            .GroupBy(r => r.Id, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.MaxBy(r => r.Score)!)
            .OrderByDescending(r => r.Score)
            .ThenBy(r => r.Title.Length)
            .ThenBy(r => r.Title, StringComparer.OrdinalIgnoreCase)
            .Take(Math.Max(1, query.MaxResults))
            .ToList();
    }

    private static async Task<List<SearchResult>> RunProvidersAsync(
        IEnumerable<ISearchProvider> providers,
        SearchQuery query,
        List<string> errors,
        CancellationToken cancellationToken)
    {
        // each provider runs in the background so a slow one never freezes the window
        var tasks = providers
            .Select(provider => (provider, task: Task.Run(() => provider.SearchAsync(query, cancellationToken), cancellationToken)))
            .ToList();

        var all = new List<SearchResult>();
        foreach (var (provider, task) in tasks)
        {
            try
            {
                all.AddRange(await task.ConfigureAwait(false));
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                errors.Add($"{provider.Name} search failed: {ex.Message}");
            }
        }

        return all;
    }
}
