namespace Kite.Core.Search;

/// <summary>
/// a source of search results. add a new class that implements this
/// and register it in the app to add a new kind of search.
/// </summary>
public interface ISearchProvider
{
    /// <summary>short name, shown in error messages.</summary>
    string Name { get; }

    /// <summary>
    /// fallback providers only run when every normal provider found nothing.
    /// </summary>
    bool IsFallback => false;

    /// <summary>
    /// return results for the query. this runs on a background thread,
    /// so do not touch the window from here. stop early when the token is cancelled.
    /// </summary>
    Task<IReadOnlyList<SearchResult>> SearchAsync(SearchQuery query, CancellationToken cancellationToken);
}
