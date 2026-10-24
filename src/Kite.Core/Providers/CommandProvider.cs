using Kite.Core.Search;

namespace Kite.Core.Providers;

/// <summary>a built in launcher command, like opening settings.</summary>
public sealed record LauncherCommand(string Id, string Title, string Subtitle, params string[] Keywords);

/// <summary>finds the launcher's own commands.</summary>
public sealed class CommandProvider : ISearchProvider
{
    public const string OpenSettings = "settings";
    public const string ReloadApps = "reload-apps";
    public const string ClearHistory = "clear-history";
    public const string Quit = "quit";

    public static readonly IReadOnlyList<LauncherCommand> DefaultCommands =
    [
        new(OpenSettings, "Kite settings", "Change the shortcut, theme, bookmarks and aliases", "settings", "preferences", "options", "kite"),
        new(ReloadApps, "Reload app list", "Look for newly installed apps", "reload", "refresh", "rescan", "apps"),
        new(ClearHistory, "Clear launch history", "Forget which results you opened before", "clear", "history", "forget"),
        new(Quit, "Quit Kite", "Close the launcher completely", "quit", "exit", "close"),
    ];

    private readonly IReadOnlyList<LauncherCommand> _commands;

    public CommandProvider(IReadOnlyList<LauncherCommand>? commands = null)
    {
        _commands = commands ?? DefaultCommands;
    }

    public string Name => "Commands";

    public Task<IReadOnlyList<SearchResult>> SearchAsync(SearchQuery query, CancellationToken cancellationToken)
    {
        var results = new List<SearchResult>();
        foreach (var command in _commands)
        {
            var score = FuzzyMatcher.Score(query.Text, command.Title);
            foreach (var keyword in command.Keywords)
            {
                // keywords count a bit less than the title
                score = Math.Max(score, FuzzyMatcher.Score(query.Text, keyword) - 100);
            }

            // keep commands below apps with the same score
            score -= 20;
            if (score <= 0 || query.Text.Length < 2)
            {
                continue;
            }

            results.Add(new SearchResult
            {
                Id = "command:" + command.Id,
                Title = command.Title,
                Subtitle = command.Subtitle,
                Icon = new ResultIcon(IconKind.Command),
                Score = score,
                Action = new ResultAction(ActionKind.RunCommand, command.Id),
                ActionLabel = "Run",
                ProviderName = Name,
            });
        }

        return Task.FromResult<IReadOnlyList<SearchResult>>(results);
    }
}
