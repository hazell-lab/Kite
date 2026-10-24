namespace Kite.Core.Storage;

public enum ThemeChoice
{
    Dark,
    Light,
    System,
}

/// <summary>what an alias opens.</summary>
public enum AliasKind
{
    Application,
    Folder,
    File,
    Website,
}

/// <summary>a named web address saved by the user.</summary>
public sealed class Bookmark
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Name { get; set; } = string.Empty;

    public string Url { get; set; } = string.Empty;
}

/// <summary>a short word that opens an app, folder, file or website.</summary>
public sealed class Alias
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Keyword { get; set; } = string.Empty;

    public AliasKind Kind { get; set; } = AliasKind.Folder;

    public string Target { get; set; } = string.Empty;
}

/// <summary>a web search that starts with a short prefix, like "g".</summary>
public sealed class WebSearchEngine
{
    public string Prefix { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    /// <summary>the address with {query} where the search words go.</summary>
    public string UrlTemplate { get; set; } = string.Empty;
}

public sealed class BookmarkList
{
    public List<Bookmark> Items { get; set; } = [];
}

public sealed class AliasList
{
    public List<Alias> Items { get; set; } = [];
}
