using Kite.Core.Search;

namespace Kite.Core.Tests;

public class FuzzyMatcherTests
{
    [Fact]
    public void ExactMatchScoresHighest()
    {
        Assert.Equal(FuzzyMatcher.Exact, FuzzyMatcher.Score("notepad", "Notepad"));
    }

    [Fact]
    public void ScoresFollowMatchQuality()
    {
        var exact = FuzzyMatcher.Score("code", "Code");
        var prefix = FuzzyMatcher.Score("code", "Code Insiders");
        var wordPrefix = FuzzyMatcher.Score("code", "Visual Studio Code");
        var substring = FuzzyMatcher.Score("ode", "Visual Studio Code");
        var typo = FuzzyMatcher.Score("cdoe", "Visual Studio Code");

        Assert.True(exact > prefix, $"exact {exact} should beat prefix {prefix}");
        Assert.True(prefix > wordPrefix, $"prefix {prefix} should beat word prefix {wordPrefix}");
        Assert.True(wordPrefix > substring, $"word prefix {wordPrefix} should beat substring {substring}");
        Assert.True(substring > typo, $"substring {substring} should beat typo {typo}");
        Assert.True(typo > 0);
    }

    [Theory]
    [InlineData("vsc", "Visual Studio Code")]
    [InlineData("chorme", "Google Chrome")]
    [InlineData("notpad", "Notepad")]
    [InlineData("powershel", "Windows PowerShell")]
    [InlineData("shell", "PowerShell")]
    [InlineData("fire fox", "Mozilla Firefox")]
    [InlineData("café", "Cafe Manager")]
    [InlineData("termnal", "Windows Terminal")]
    public void MatchesPartialAndMisspelledNames(string query, string candidate)
    {
        Assert.True(FuzzyMatcher.Score(query, candidate) > 0, $"{query} should match {candidate}");
    }

    [Theory]
    [InlineData("xyz", "Notepad")]
    [InlineData("zz", "Calculator")]
    [InlineData("", "Notepad")]
    [InlineData("abc", "")]
    public void RejectsUnrelatedNames(string query, string candidate)
    {
        Assert.Equal(0, FuzzyMatcher.Score(query, candidate));
    }

    [Fact]
    public void ShorterNameWinsForSamePrefix()
    {
        Assert.True(FuzzyMatcher.Score("paint", "Paint 3D") > FuzzyMatcher.Score("paint", "Paint Shop Pro Studio"));
    }

    [Theory]
    [InlineData("kitten", "sitting", 3)]
    [InlineData("chrome", "chorme", 1)]
    [InlineData("same", "same", 0)]
    [InlineData("", "abc", 3)]
    public void EditDistanceCountsSwapsAsOneEdit(string a, string b, int expected)
    {
        Assert.Equal(expected, FuzzyMatcher.EditDistance(a, b));
    }

    [Fact]
    public void SplitsCamelCaseWords()
    {
        var words = FuzzyMatcher.SplitWords("PowerShell ISE");
        Assert.Contains("power", words);
        Assert.Contains("shell", words);
        Assert.Contains("powershell", words);
        Assert.Contains("ise", words);
    }
}
