using Kite.Core.Search;
using Kite.Core.Storage;

namespace Kite.Core.Providers;

/// <summary>
/// turns "g some words" into a google search, "gh some words" into a github
/// search and so on. nothing is sent anywhere until the result is opened.
/// </summary>
public sealed class WebSearchProvider : ISearchProvider
{
    public const int PrefixScore = 1200;
    public const int HintScore = 120;

    private readonly Func<LauncherSettings> _settings;

    public WebSearchProvider(Func<LauncherSettings> settings)
    {
        _settings = settings;
    }

    public string Name => "Web search";

    /// <summary>build the address for a search. the words are safely encoded.</summary>
    public static string BuildUrl(WebSearchEngine engine, string terms) =>
        engine.UrlTemplate.Replace("{query}", Uri.EscapeDataString(terms.Trim()), StringComparison.Ordinal);

    public Task<IReadOnlyList<SearchResult>> SearchAsync(SearchQuery query, CancellationToken cancellationToken)
    {
        var text = query.Text;
        var spaceIndex = text.IndexOf(' ');
        var prefix = (spaceIndex < 0 ? text : text[..spaceIndex]).ToLowerInvariant();
        var terms = spaceIndex < 0 ? string.Empty : text[(spaceIndex + 1)..].Trim();

        var engine = _settings().SearchEngines.FirstOrDefault(e => e.Prefix == prefix);
        if (engine is null)
        {
            return Task.FromResult<IReadOnlyList<SearchResult>>([]);
        }

        if (terms.Length == 0)
        {
            // only the prefix so far, so show a small hint below other matches
            SearchResult hint = new()
            {
                Id = "web-hint:" + engine.Prefix,
                Title = $"Search {engine.Name}",
                Subtitle = $"Type \"{engine.Prefix} \" followed by your search words",
                Icon = new ResultIcon(IconKind.Web),
                Score = HintScore,
                Action = ResultAction.None,
                ActionLabel = string.Empty,
                ProviderName = Name,
                TrackUsage = false,
            };
            return Task.FromResult<IReadOnlyList<SearchResult>>([hint]);
        }

        return Task.FromResult<IReadOnlyList<SearchResult>>([MakeResult(engine, terms, PrefixScore, Name)]);
    }

    internal static SearchResult MakeResult(WebSearchEngine engine, string terms, int score, string providerName) => new()
    {
        Id = "web:" + engine.Prefix,
        Title = $"Search {engine.Name} for \"{terms}\"",
        Subtitle = BuildUrl(engine, terms),
        Icon = new ResultIcon(IconKind.Web),
        Score = score,
        Action = new ResultAction(ActionKind.OpenUrl, BuildUrl(engine, terms)),
        ActionLabel = "Search",
        ProviderName = providerName,
        TrackUsage = false,
    };
}

/// <summary>offers a web search when nothing else matched.</summary>
public sealed class WebFallbackProvider : ISearchProvider
{
    private readonly Func<LauncherSettings> _settings;

    public WebFallbackProvider(Func<LauncherSettings> settings)
    {
        _settings = settings;
    }

    public string Name => "Web fallback";

    public bool IsFallback => true;

    public Task<IReadOnlyList<SearchResult>> SearchAsync(SearchQuery query, CancellationToken cancellationToken)
    {
        var settings = _settings();
        var engine = settings.SearchEngines.FirstOrDefault(e => e.Prefix == settings.FallbackSearchPrefix)
                     ?? settings.SearchEngines.FirstOrDefault();
        if (engine is null || query.IsEmpty)
        {
            return Task.FromResult<IReadOnlyList<SearchResult>>([]);
        }

        return Task.FromResult<IReadOnlyList<SearchResult>>([WebSearchProvider.MakeResult(engine, query.Text, 10, Name)]);
    }
}
