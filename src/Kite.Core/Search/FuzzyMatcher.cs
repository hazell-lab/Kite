using System.Globalization;
using System.Text;

namespace Kite.Core.Search;

/// <summary>
/// scores how well a query matches a name. the score is between 0 and 1000.
/// zero means no match.
/// </summary>
public static class FuzzyMatcher
{
    public const int Exact = 1000;
    public const int Prefix = 900;
    public const int WordPrefix = 800;
    public const int AllWords = 780;
    public const int Acronym = 750;
    public const int Substring = 700;
    public const int Subsequence = 500;
    public const int Typo = 450;

    private static readonly char[] WordSeparators = [' ', '-', '_', '.', '(', ')', '[', ']', '&', '+', ',', '/', '\\', ':'];

    /// <summary>score a query against one candidate name.</summary>
    public static int Score(string query, string candidate)
    {
        var q = Normalize(query);
        var c = Normalize(candidate);
        if (q.Length == 0 || c.Length == 0)
        {
            return 0;
        }

        if (c == q)
        {
            return Exact;
        }

        // shorter names win when several names start the same way
        if (c.StartsWith(q, StringComparison.Ordinal))
        {
            return Prefix - Math.Min(50, c.Length - q.Length);
        }

        var words = SplitWords(candidate);
        var best = 0;

        foreach (var word in words)
        {
            if (word.StartsWith(q, StringComparison.Ordinal))
            {
                best = Math.Max(best, WordPrefix - Math.Min(30, word.Length - q.Length));
            }
        }

        var queryWords = q.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (queryWords.Length > 1 && AllQueryWordsMatch(queryWords, words, allowTypos: false))
        {
            best = Math.Max(best, AllWords);
        }

        if (q.Length >= 2 && !q.Contains(' ') && words.Count >= 2)
        {
            var initials = string.Concat(words.Select(w => w[0]));
            if (initials.StartsWith(q, StringComparison.Ordinal))
            {
                best = Math.Max(best, Acronym);
            }
        }

        var position = c.IndexOf(q, StringComparison.Ordinal);
        if (position >= 0)
        {
            best = Math.Max(best, Substring - Math.Min(100, position * 5));
        }

        if (best > 0)
        {
            return best;
        }

        if (q.Length >= 3)
        {
            var gaps = SubsequenceGaps(q.Replace(" ", string.Empty), c.Replace(" ", string.Empty));
            if (gaps >= 0 && gaps <= 12)
            {
                best = Math.Max(best, Math.Max(300, Subsequence - gaps * 15));
            }
        }

        var typoScore = TypoScore(q, queryWords, words);
        return Math.Max(best, typoScore);
    }

    /// <summary>lower case, remove accents and squash repeated spaces.</summary>
    public static string Normalize(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var decomposed = text.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        var lastWasSpace = false;
        foreach (var ch in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            if (char.IsWhiteSpace(ch))
            {
                if (!lastWasSpace)
                {
                    builder.Append(' ');
                }

                lastWasSpace = true;
                continue;
            }

            builder.Append(ch);
            lastWasSpace = false;
        }

        return builder.ToString().Normalize(NormalizationForm.FormC);
    }

    /// <summary>split a name into lower case words, also where a capital letter starts a new word.</summary>
    public static IReadOnlyList<string> SplitWords(string? text)
    {
        var result = new List<string>();
        if (string.IsNullOrWhiteSpace(text))
        {
            return result;
        }

        foreach (var part in text.Split(WordSeparators, StringSplitOptions.RemoveEmptyEntries))
        {
            var start = 0;
            for (var i = 1; i < part.Length; i++)
            {
                // a capital after a small letter starts a new word, like the s in powershell
                if (char.IsUpper(part[i]) && char.IsLower(part[i - 1]))
                {
                    result.Add(Normalize(part[start..i]));
                    start = i;
                }
            }

            result.Add(Normalize(part[start..]));
        }

        result.RemoveAll(w => w.Length == 0);

        // also keep the whole parts so "powershell" still matches as one word
        foreach (var part in text.Split(WordSeparators, StringSplitOptions.RemoveEmptyEntries))
        {
            var whole = Normalize(part);
            if (whole.Length > 0 && !result.Contains(whole))
            {
                result.Add(whole);
            }
        }

        return result;
    }

    /// <summary>
    /// counts how many letter changes turn one word into another.
    /// swapping two neighbouring letters counts as one change.
    /// </summary>
    public static int EditDistance(string a, string b)
    {
        var rows = a.Length + 1;
        var cols = b.Length + 1;
        var d = new int[rows, cols];
        for (var i = 0; i < rows; i++)
        {
            d[i, 0] = i;
        }

        for (var j = 0; j < cols; j++)
        {
            d[0, j] = j;
        }

        for (var i = 1; i < rows; i++)
        {
            for (var j = 1; j < cols; j++)
            {
                var cost = a[i - 1] == b[j - 1] ? 0 : 1;
                d[i, j] = Math.Min(Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1), d[i - 1, j - 1] + cost);
                if (i > 1 && j > 1 && a[i - 1] == b[j - 2] && a[i - 2] == b[j - 1])
                {
                    d[i, j] = Math.Min(d[i, j], d[i - 2, j - 2] + 1);
                }
            }
        }

        return d[rows - 1, cols - 1];
    }

    /// <summary>how many typos are allowed for a word of this length.</summary>
    public static int AllowedTypos(int length) => length switch
    {
        < 4 => 0,
        < 7 => 1,
        _ => 2,
    };

    private static bool AllQueryWordsMatch(string[] queryWords, IReadOnlyList<string> words, bool allowTypos)
    {
        foreach (var queryWord in queryWords)
        {
            var found = words.Any(w =>
                w.StartsWith(queryWord, StringComparison.Ordinal) ||
                (allowTypos && WordTypoDistance(queryWord, w) <= AllowedTypos(queryWord.Length)));
            if (!found)
            {
                return false;
            }
        }

        return true;
    }

    private static int TypoScore(string q, string[] queryWords, IReadOnlyList<string> words)
    {
        if (q.Length < 3 || words.Count == 0)
        {
            return 0;
        }

        if (queryWords.Length > 1)
        {
            return AllQueryWordsMatch(queryWords, words, allowTypos: true) ? Typo - 50 : 0;
        }

        var bestDistance = int.MaxValue;
        foreach (var word in words)
        {
            bestDistance = Math.Min(bestDistance, WordTypoDistance(q, word));
        }

        return bestDistance <= AllowedTypos(q.Length) ? Typo - bestDistance * 60 : 0;
    }

    /// <summary>
    /// compare the query with the whole word and with the start of the word,
    /// so a typo while still typing is also forgiven.
    /// </summary>
    private static int WordTypoDistance(string queryWord, string word)
    {
        var whole = EditDistance(queryWord, word);
        if (word.Length > queryWord.Length)
        {
            var start = EditDistance(queryWord, word[..queryWord.Length]);
            return Math.Min(whole, start);
        }

        return whole;
    }

    /// <summary>
    /// returns how many letters were skipped to find the query in order,
    /// or -1 when the letters are not all there.
    /// </summary>
    private static int SubsequenceGaps(string q, string c)
    {
        var qi = 0;
        var gaps = 0;
        var started = false;
        for (var ci = 0; ci < c.Length && qi < q.Length; ci++)
        {
            if (c[ci] == q[qi])
            {
                qi++;
                started = true;
            }
            else if (started)
            {
                gaps++;
            }
        }

        return qi == q.Length ? gaps : -1;
    }
}
