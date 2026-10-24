namespace Kite.Core.Search;

/// <summary>what kind of picture to show next to a result.</summary>
public enum IconKind
{
    Application,
    Calculator,
    Web,
    Bookmark,
    Folder,
    File,
    Alias,
    Command,
    Info,
    Warning,
}

/// <summary>what happens when a result is activated.</summary>
public enum ActionKind
{
    /// <summary>nothing happens. used for hints and messages.</summary>
    None,

    /// <summary>open a file, shortcut or program path.</summary>
    OpenPath,

    /// <summary>open a store app by its app id.</summary>
    OpenPackagedApp,

    /// <summary>open a folder in file explorer.</summary>
    OpenFolder,

    /// <summary>open a web address in the default browser.</summary>
    OpenUrl,

    /// <summary>copy text to the clipboard.</summary>
    CopyText,

    /// <summary>run one of the launcher's own commands.</summary>
    RunCommand,
}

/// <summary>the action to run for a result.</summary>
public sealed record ResultAction(ActionKind Kind, string Target)
{
    public static readonly ResultAction None = new(ActionKind.None, string.Empty);
}

/// <summary>the icon to show for a result.</summary>
/// <param name="Kind">used for the fallback symbol.</param>
/// <param name="ShellPath">optional path or shell name used to load a real icon.</param>
public sealed record ResultIcon(IconKind Kind, string? ShellPath = null);

/// <summary>one row in the results list.</summary>
public sealed record SearchResult
{
    /// <summary>stable key used for launch history.</summary>
    public required string Id { get; init; }

    public required string Title { get; init; }

    public string Subtitle { get; init; } = string.Empty;

    public required ResultIcon Icon { get; init; }

    /// <summary>higher scores are shown first.</summary>
    public required int Score { get; init; }

    public required ResultAction Action { get; init; }

    /// <summary>short verb shown as the enter hint, like open or copy.</summary>
    public string ActionLabel { get; init; } = "Open";

    /// <summary>name of the provider that made this result.</summary>
    public string ProviderName { get; init; } = string.Empty;

    /// <summary>false for results that should not affect launch history.</summary>
    public bool TrackUsage { get; init; } = true;
}

/// <summary>what the user typed, plus a few search options.</summary>
public sealed record SearchQuery(string RawText, int MaxResults = 8)
{
    /// <summary>the text without spaces at the start or end.</summary>
    public string Text { get; } = (RawText ?? string.Empty).Trim();

    public bool IsEmpty => Text.Length == 0;
}

/// <summary>everything a search produced.</summary>
public sealed record SearchResponse(
    SearchQuery Query,
    IReadOnlyList<SearchResult> Results,
    IReadOnlyList<string> Errors);
