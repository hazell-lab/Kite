using Kite.Core.Launching;
using Kite.Core.Search;
using Kite.Core.Storage;

namespace Kite.Core.Providers;

/// <summary>finds the user's saved bookmarks by name or address.</summary>
public sealed class BookmarkProvider : ISearchProvider
{
    private readonly Func<IReadOnlyList<Bookmark>> _bookmarks;

    public BookmarkProvider(Func<IReadOnlyList<Bookmark>> bookmarks)
    {
        _bookmarks = bookmarks;
    }

    public string Name => "Bookmarks";

    public Task<IReadOnlyList<SearchResult>> SearchAsync(SearchQuery query, CancellationToken cancellationToken)
    {
        var results = new List<SearchResult>();
        foreach (var bookmark in _bookmarks())
        {
            var url = TargetValidator.NormalizeWebAddress(bookmark.Url);
            if (url is null)
            {
                continue;
            }

            var score = FuzzyMatcher.Score(query.Text, bookmark.Name);

            // the address can also match, but counts for less than the name
            var host = Uri.TryCreate(url, UriKind.Absolute, out var uri) ? uri.Host : string.Empty;
            if (host.Length > 0 && query.Text.Length >= 3 &&
                host.Contains(query.Text, StringComparison.OrdinalIgnoreCase))
            {
                score = Math.Max(score, FuzzyMatcher.Substring - 100);
            }

            if (score <= 0)
            {
                continue;
            }

            results.Add(new SearchResult
            {
                Id = "bookmark:" + bookmark.Id,
                Title = bookmark.Name,
                Subtitle = url,
                Icon = new ResultIcon(IconKind.Bookmark),
                Score = score,
                Action = new ResultAction(ActionKind.OpenUrl, url),
                ActionLabel = "Open",
                ProviderName = Name,
            });
        }

        return Task.FromResult<IReadOnlyList<SearchResult>>(results);
    }
}
