using Kite.Core.Hotkeys;
using Kite.Core.Launching;
using Kite.Core.Storage;

namespace Kite.Core.Tests;

public sealed class PersistenceTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "kite-tests-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }

    [Fact]
    public void MissingFilesGiveDefaults()
    {
        var data = new LocalData(_folder);

        var warnings = data.Load();

        Assert.Empty(warnings);
        Assert.Equal("Alt+Space", data.Settings.Hotkey);
        Assert.Equal(8, data.Settings.MaxResults);
        Assert.Empty(data.Bookmarks);
        Assert.Empty(data.Aliases);
    }

    [Fact]
    public void SettingsRoundTrip()
    {
        var data = new LocalData(_folder);
        data.Load();
        var settings = LocalData.Clone(data.Settings);
        settings.Hotkey = "Ctrl+Shift+K";
        settings.Theme = ThemeChoice.Light;
        settings.MaxResults = 12;
        settings.StartWithWindows = true;
        settings.Monitor = @"\\.\DISPLAY2";

        data.SaveSettings(settings);
        var reloaded = new LocalData(_folder);
        var warnings = reloaded.Load();

        Assert.Empty(warnings);
        Assert.Equal("Ctrl+Shift+K", reloaded.Settings.Hotkey);
        Assert.Equal(ThemeChoice.Light, reloaded.Settings.Theme);
        Assert.Equal(12, reloaded.Settings.MaxResults);
        Assert.True(reloaded.Settings.StartWithWindows);
        Assert.Equal(@"\\.\DISPLAY2", reloaded.Settings.Monitor);
    }

    [Fact]
    public void BookmarksAliasesAndHistoryRoundTrip()
    {
        var data = new LocalData(_folder);
        data.Load();
        data.SaveBookmarks([new Bookmark { Name = "Docs", Url = "https://learn.microsoft.com" }]);
        data.SaveAliases([new Alias { Keyword = "projects", Kind = AliasKind.Folder, Target = @"C:\Projects" }]);
        data.History.RecordLaunch("app:x", "x", DateTime.UtcNow);
        data.SaveHistory();

        var reloaded = new LocalData(_folder);
        reloaded.Load();

        Assert.Equal("Docs", Assert.Single(reloaded.Bookmarks).Name);
        var alias = Assert.Single(reloaded.Aliases);
        Assert.Equal(AliasKind.Folder, alias.Kind);
        Assert.Equal(1, reloaded.History.Count);
    }

    [Fact]
    public void DamagedFileIsBackedUpAndDefaultsAreUsed()
    {
        Directory.CreateDirectory(_folder);
        File.WriteAllText(Path.Combine(_folder, "settings.json"), "{ this is not json");

        var data = new LocalData(_folder);
        var warnings = data.Load();

        Assert.Contains(warnings, w => w.Contains("damaged"));
        Assert.Equal("Alt+Space", data.Settings.Hotkey);
        Assert.Single(Directory.GetFiles(_folder, "settings.damaged-*.json"));
    }

    [Fact]
    public void WrongTypesInFileDoNotCrash()
    {
        Directory.CreateDirectory(_folder);
        File.WriteAllText(Path.Combine(_folder, "bookmarks.json"), "{ \"Items\": \"oops\" }");
        File.WriteAllText(Path.Combine(_folder, "history.json"), "[1, 2, 3]");

        var data = new LocalData(_folder);
        var warnings = data.Load();

        Assert.Equal(2, warnings.Count);
        Assert.Empty(data.Bookmarks);
    }

    [Fact]
    public void InvalidValuesAreRepaired()
    {
        Directory.CreateDirectory(_folder);
        File.WriteAllText(
            Path.Combine(_folder, "settings.json"),
            """
            {
              "Hotkey": "Banana",
              "MaxResults": 999,
              "Monitor": "",
              "SearchEngines": [ { "Prefix": "bad", "Name": "Bad", "UrlTemplate": "javascript:{query}" } ]
            }
            """);

        var data = new LocalData(_folder);
        var warnings = data.Load();

        Assert.Single(warnings);
        Assert.Equal("Alt+Space", data.Settings.Hotkey);
        Assert.Equal(LauncherSettings.MaxResultsLimit, data.Settings.MaxResults);
        Assert.Equal(LauncherSettings.MonitorWithCursor, data.Settings.Monitor);
        Assert.Contains(data.Settings.SearchEngines, e => e.Prefix == "g");
        Assert.DoesNotContain(data.Settings.SearchEngines, e => e.Prefix == "bad");
    }

    [Fact]
    public void ClearHistoryEmptiesSavedFile()
    {
        var data = new LocalData(_folder);
        data.Load();
        data.History.RecordLaunch("app:x", "x", DateTime.UtcNow);
        data.SaveHistory();

        data.ClearHistory();
        var reloaded = new LocalData(_folder);
        reloaded.Load();

        Assert.Equal(0, reloaded.History.Count);
    }

    [Fact]
    public void SaveLeavesNoTempFileBehind()
    {
        var data = new LocalData(_folder);
        data.Load();

        data.SaveSettings(new LauncherSettings());

        Assert.Empty(Directory.GetFiles(_folder, "*.tmp"));
    }

    [Theory]
    [InlineData("Alt+Space", "Alt+Space")]
    [InlineData("ctrl + shift + k", "Ctrl+Shift+K")]
    [InlineData("Win+Alt+1", "Alt+Win+D1")]
    [InlineData("Control+Spacebar", "Ctrl+Space")]
    [InlineData("Alt+F2", "Alt+F2")]
    public void ParsesHotkeys(string text, string expected)
    {
        Assert.True(HotkeyGesture.TryParse(text, out var gesture, out var error), error);
        Assert.Equal(expected, gesture!.ToString());
    }

    [Theory]
    [InlineData("")]
    [InlineData("Space")]
    [InlineData("Alt+")]
    [InlineData("Ctrl+A+B")]
    [InlineData("Shift+A")]
    [InlineData("Alt+$")]
    public void RejectsBadHotkeys(string text)
    {
        Assert.False(HotkeyGesture.TryParse(text, out _, out var error));
        Assert.False(string.IsNullOrEmpty(error));
    }

    [Fact]
    public void AliasTargetsAreChecked()
    {
        Directory.CreateDirectory(_folder);
        var file = Path.Combine(_folder, "tool.exe");
        File.WriteAllText(file, "x");

        Assert.True(TargetValidator.Check(AliasKind.Folder, _folder).IsValid);
        Assert.True(TargetValidator.Check(AliasKind.Application, file).IsValid);
        Assert.False(TargetValidator.Check(AliasKind.Folder, Path.Combine(_folder, "missing")).IsValid);
        Assert.False(TargetValidator.Check(AliasKind.Application, _folder).IsValid);
        Assert.False(TargetValidator.Check(AliasKind.Application, "relative.exe").IsValid);
        Assert.False(TargetValidator.Check(AliasKind.Website, "javascript:alert(1)").IsValid);
        Assert.Equal("https://example.com", TargetValidator.Check(AliasKind.Website, "example.com").ResolvedTarget);
    }
}
