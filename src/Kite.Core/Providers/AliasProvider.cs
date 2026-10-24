using Kite.Core.Launching;
using Kite.Core.Search;
using Kite.Core.Storage;

namespace Kite.Core.Providers;

/// <summary>finds user aliases, like "projects" for a projects folder.</summary>
public sealed class AliasProvider : ISearchProvider
{
    /// <summary>an alias typed in full beats everything except the calculator.</summary>
    public const int ExactScore = 1100;

    private readonly Func<IReadOnlyList<Alias>> _aliases;

    public AliasProvider(Func<IReadOnlyList<Alias>> aliases)
    {
        _aliases = aliases;
    }

    public string Name => "Aliases";

    public static ActionKind ActionFor(AliasKind kind) => kind switch
    {
        AliasKind.Website => ActionKind.OpenUrl,
        AliasKind.Folder => ActionKind.OpenFolder,
        _ => ActionKind.OpenPath,
    };

    public static IconKind IconFor(AliasKind kind) => kind switch
    {
        AliasKind.Website => IconKind.Web,
        AliasKind.Folder => IconKind.Folder,
        AliasKind.File => IconKind.File,
        _ => IconKind.Application,
    };

    public Task<IReadOnlyList<SearchResult>> SearchAsync(SearchQuery query, CancellationToken cancellationToken)
    {
        var results = new List<SearchResult>();
        foreach (var alias in _aliases())
        {
            var keyword = alias.Keyword.Trim();
            var score = string.Equals(keyword, query.Text, StringComparison.OrdinalIgnoreCase)
                ? ExactScore
                : FuzzyMatcher.Score(query.Text, keyword);
            if (score <= 0)
            {
                continue;
            }

            var target = alias.Kind == AliasKind.Website
                ? TargetValidator.NormalizeWebAddress(alias.Target) ?? alias.Target
                : TargetValidator.ExpandPath(alias.Target);

            var shellIconPath = alias.Kind is AliasKind.Application or AliasKind.File or AliasKind.Folder ? target : null;

            results.Add(new SearchResult
            {
                Id = "alias:" + alias.Id,
                Title = keyword,
                Subtitle = $"Alias • {target}",
                Icon = new ResultIcon(IconFor(alias.Kind), shellIconPath),
                Score = score,
                Action = new ResultAction(ActionFor(alias.Kind), target),
                ActionLabel = "Open",
                ProviderName = Name,
            });
        }

        return Task.FromResult<IReadOnlyList<SearchResult>>(results);
    }
}
