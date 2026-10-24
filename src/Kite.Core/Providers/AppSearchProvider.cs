using Kite.Core.Apps;
using Kite.Core.Search;

namespace Kite.Core.Providers;

/// <summary>finds installed apps by name.</summary>
public sealed class AppSearchProvider : ISearchProvider
{
    private readonly AppCatalog _catalog;

    public AppSearchProvider(AppCatalog catalog)
    {
        _catalog = catalog;
    }

    public string Name => "Apps";

    public Task<IReadOnlyList<SearchResult>> SearchAsync(SearchQuery query, CancellationToken cancellationToken)
    {
        var results = new List<SearchResult>();
        var apps = _catalog.Apps;
        for (var i = 0; i < apps.Count; i++)
        {
            // check now and then so a cancelled search stops quickly
            if ((i & 63) == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            var app = apps[i];
            var score = FuzzyMatcher.Score(query.Text, app.Name);
            if (score <= 0)
            {
                continue;
            }

            results.Add(new SearchResult
            {
                Id = app.Id,
                Title = app.Name,
                Subtitle = string.IsNullOrEmpty(app.Description) ? "Application" : app.Description,
                Icon = new ResultIcon(IconKind.Application, app.IconPath),
                Score = score,
                Action = app.LaunchKind == AppLaunchKind.Packaged
                    ? new ResultAction(ActionKind.OpenPackagedApp, app.Target)
                    : new ResultAction(ActionKind.OpenPath, app.Target),
                ActionLabel = "Open",
                ProviderName = Name,
            });
        }

        return Task.FromResult<IReadOnlyList<SearchResult>>(results);
    }
}
