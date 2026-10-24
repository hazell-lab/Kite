using Kite.Core.Apps;
using Kite.Core.Providers;
using Kite.Core.Search;
using Kite.Core.Storage;

namespace Kite.Core.Tests;

public class SearchRankingTests
{
    private static readonly DateTime Now = new(2026, 1, 15, 12, 0, 0, DateTimeKind.Utc);

    private sealed class FixedAppSource(params string[] names) : IAppSource
    {
        public string Name => "Test";

        public IReadOnlyList<AppEntry> Discover(CancellationToken cancellationToken) =>
            names.Select(n => new AppEntry(n, $@"C:\Apps\{n}.lnk", AppLaunchKind.Path, Name)).ToList();
    }

    private sealed class ThrowingProvider : ISearchProvider
    {
        public string Name => "Broken";

        public Task<IReadOnlyList<SearchResult>> SearchAsync(SearchQuery query, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("boom");
    }

    private sealed class SlowProvider : ISearchProvider
    {
        public string Name => "Slow";

        public async Task<IReadOnlyList<SearchResult>> SearchAsync(SearchQuery query, CancellationToken cancellationToken)
        {
            await Task.Delay(5000, cancellationToken);
            return [];
        }
    }

    private static async Task<SearchEngine> CreateEngine(UsageHistory history, params string[] appNames)
    {
        var catalog = new AppCatalog([new FixedAppSource(appNames)]);
        await catalog.RefreshAsync();
        var settings = new LauncherSettings();
        return new SearchEngine(
            [
                new AppSearchProvider(catalog),
                new CalculatorProvider(),
                new WebSearchProvider(() => settings),
                new WebFallbackProvider(() => settings),
                new CommandProvider(),
            ],
            () => history,
            () => Now);
    }

    [Fact]
    public async Task ExactMatchComesFirst()
    {
        var engine = await CreateEngine(new UsageHistory(), "Code Insiders", "Code", "Visual Studio Code", "Encoder");

        var response = await engine.SearchAsync(new SearchQuery("code"), CancellationToken.None);

        Assert.Equal("Code", response.Results[0].Title);
        Assert.Equal("Code Insiders", response.Results[1].Title);
    }

    [Fact]
    public async Task HistoryMovesOftenUsedAppUpWithoutBeatingExactMatch()
    {
        var history = new UsageHistory();
        var engine = await CreateEngine(history, "Code", "Code Insiders", "Codeview Explorer Pro");
        var insidersId = @"app:c:\apps\code insiders.lnk";
        var readerId = @"app:c:\apps\codeview explorer pro.lnk";

        for (var i = 0; i < 20; i++)
        {
            history.RecordLaunch(readerId, "cod", Now.AddMinutes(-i));
        }

        var response = await engine.SearchAsync(new SearchQuery("code"), CancellationToken.None);

        // exact name still wins even against a heavily used app
        Assert.Equal("Code", response.Results[0].Title);

        // the used app now beats the unused app with a similar score
        var readerIndex = response.Results.ToList().FindIndex(r => r.Id == readerId);
        var insidersIndex = response.Results.ToList().FindIndex(r => r.Id == insidersId);
        Assert.True(readerIndex < insidersIndex, $"reader at {readerIndex} should be above insiders at {insidersIndex}");
    }

    [Fact]
    public void BoostIsCapped()
    {
        var history = new UsageHistory();
        for (var i = 0; i < 500; i++)
        {
            history.RecordLaunch("app:x", "x", Now);
        }

        Assert.Equal(UsageHistory.MaxBoost, history.GetBoost("app:x", "x", Now));
        Assert.True(FuzzyMatcher.Prefix + UsageHistory.MaxBoost < FuzzyMatcher.Exact);
    }

    [Fact]
    public async Task CalculatorResultIsOnTop()
    {
        var engine = await CreateEngine(new UsageHistory(), "Calculator");

        var response = await engine.SearchAsync(new SearchQuery("25 * 4"), CancellationToken.None);

        Assert.Equal("100", response.Results[0].Title);
        Assert.Equal(ActionKind.CopyText, response.Results[0].Action.Kind);
    }

    [Fact]
    public async Task WebPrefixGivesEncodedSearch()
    {
        var engine = await CreateEngine(new UsageHistory(), "Google Chrome");

        var response = await engine.SearchAsync(new SearchQuery("g c# & .net"), CancellationToken.None);

        var top = response.Results[0];
        Assert.Equal(ActionKind.OpenUrl, top.Action.Kind);
        Assert.Equal("https://www.google.com/search?q=c%23%20%26%20.net", top.Action.Target);
    }

    [Fact]
    public async Task FallbackOnlyAppearsWhenNothingMatches()
    {
        var engine = await CreateEngine(new UsageHistory(), "Notepad");

        var withMatch = await engine.SearchAsync(new SearchQuery("notepad"), CancellationToken.None);
        var withoutMatch = await engine.SearchAsync(new SearchQuery("qqqq zzzz"), CancellationToken.None);

        Assert.DoesNotContain(withMatch.Results, r => r.ProviderName == "Web fallback");
        var fallback = Assert.Single(withoutMatch.Results);
        Assert.Equal("Web fallback", fallback.ProviderName);
    }

    [Fact]
    public async Task ResultCountIsLimited()
    {
        var names = Enumerable.Range(1, 30).Select(i => $"Tool {i}").ToArray();
        var engine = await CreateEngine(new UsageHistory(), names);

        var response = await engine.SearchAsync(new SearchQuery("tool", MaxResults: 5), CancellationToken.None);

        Assert.Equal(5, response.Results.Count);
    }

    [Fact]
    public async Task EmptyQueryGivesNoResults()
    {
        var engine = await CreateEngine(new UsageHistory(), "Notepad");

        var response = await engine.SearchAsync(new SearchQuery("   "), CancellationToken.None);

        Assert.Empty(response.Results);
    }

    [Fact]
    public async Task BrokenProviderIsReportedButOthersStillWork()
    {
        var catalog = new AppCatalog([new FixedAppSource("Notepad")]);
        await catalog.RefreshAsync();
        var engine = new SearchEngine([new ThrowingProvider(), new AppSearchProvider(catalog)], () => null);

        var response = await engine.SearchAsync(new SearchQuery("note"), CancellationToken.None);

        Assert.Equal("Notepad", Assert.Single(response.Results).Title);
        Assert.Contains(response.Errors, e => e.Contains("boom"));
    }

    [Fact]
    public async Task CancelledSearchStops()
    {
        var engine = new SearchEngine([new SlowProvider()], () => null);
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => engine.SearchAsync(new SearchQuery("anything"), cts.Token));
    }

    [Fact]
    public async Task CatalogRemovesDuplicateNames()
    {
        var catalog = new AppCatalog([new FixedAppSource("Notepad", "Paint"), new FixedAppSource("notepad", "Calculator")]);

        await catalog.RefreshAsync();

        Assert.Equal(3, catalog.Apps.Count);
        Assert.Equal(CatalogState.Ready, catalog.State);
    }

    [Fact]
    public async Task AliasTypedInFullWins()
    {
        var aliases = new List<Alias> { new() { Keyword = "projects", Kind = AliasKind.Website, Target = "example.com" } };
        var catalog = new AppCatalog([new FixedAppSource("Projects Manager")]);
        await catalog.RefreshAsync();
        var engine = new SearchEngine([new AppSearchProvider(catalog), new AliasProvider(() => aliases)], () => null);

        var response = await engine.SearchAsync(new SearchQuery("projects"), CancellationToken.None);

        Assert.Equal("projects", response.Results[0].Title);
        Assert.Equal("https://example.com", response.Results[0].Action.Target);
    }

    [Fact]
    public async Task BookmarksMatchByNameAndHost()
    {
        var bookmarks = new List<Bookmark>
        {
            new() { Name = "Team wiki", Url = "https://wiki.example.org" },
            new() { Name = "Broken", Url = "javascript:alert(1)" },
        };
        var provider = new BookmarkProvider(() => bookmarks);

        var byName = await provider.SearchAsync(new SearchQuery("team"), CancellationToken.None);
        var byHost = await provider.SearchAsync(new SearchQuery("example"), CancellationToken.None);
        var unsafeOne = await provider.SearchAsync(new SearchQuery("broken"), CancellationToken.None);

        Assert.Single(byName);
        Assert.Single(byHost);
        Assert.Empty(unsafeOne);
    }
}
