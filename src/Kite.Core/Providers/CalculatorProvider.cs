using Kite.Core.Calculation;
using Kite.Core.Search;

namespace Kite.Core.Providers;

/// <summary>shows the answer for math like "25 * 4". enter copies it.</summary>
public sealed class CalculatorProvider : ISearchProvider
{
    public const int ResultScore = 1300;
    private const string OperatorChars = "+-*/%^()×÷−";

    public string Name => "Calculator";

    public Task<IReadOnlyList<SearchResult>> SearchAsync(SearchQuery query, CancellationToken cancellationToken)
    {
        var text = query.Text;
        var forced = text.StartsWith('=');
        var expression = forced ? text[1..].Trim() : text;

        if (!forced && !LooksLikeMath(expression))
        {
            return Task.FromResult<IReadOnlyList<SearchResult>>([]);
        }

        if (expression.Length == 0)
        {
            return Task.FromResult<IReadOnlyList<SearchResult>>([Message("Type a calculation", "For example 25 * 4 or (120 + 30) / 5", IconKind.Calculator)]);
        }

        var result = ExpressionEvaluator.Evaluate(expression);
        if (!result.Success)
        {
            return Task.FromResult<IReadOnlyList<SearchResult>>([Message(result.Error ?? "Invalid expression", expression, IconKind.Warning)]);
        }

        var formatted = ExpressionEvaluator.Format(result.Value);
        SearchResult answer = new()
        {
            Id = "calc:" + expression,
            Title = formatted,
            Subtitle = $"{expression} • press Enter to copy",
            Icon = new ResultIcon(IconKind.Calculator),
            Score = ResultScore,
            Action = new ResultAction(ActionKind.CopyText, formatted),
            ActionLabel = "Copy",
            ProviderName = Name,
            TrackUsage = false,
        };

        return Task.FromResult<IReadOnlyList<SearchResult>>([answer]);
    }

    /// <summary>
    /// true when the text only has numbers, operators and known math words,
    /// and has at least one digit and one operator or function.
    /// </summary>
    public static bool LooksLikeMath(string text)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Length > ExpressionEvaluator.MaxLength)
        {
            return false;
        }

        var hasDigit = false;
        var hasOperator = false;
        var i = 0;
        while (i < text.Length)
        {
            var ch = text[i];
            if (char.IsAsciiDigit(ch))
            {
                hasDigit = true;
                i++;
            }
            else if (ch == '.' || char.IsWhiteSpace(ch))
            {
                i++;
            }
            else if (OperatorChars.Contains(ch))
            {
                // a leading minus alone, like "-5", is not a calculation
                if (!(ch is '-' or '−' && i == 0) && ch is not '(' and not ')')
                {
                    hasOperator = true;
                }

                i++;
            }
            else if (char.IsAsciiLetter(ch))
            {
                var start = i;
                while (i < text.Length && char.IsAsciiLetter(text[i]))
                {
                    i++;
                }

                var word = text[start..i];
                if (!ExpressionEvaluator.KnownWords.Contains(word, StringComparer.OrdinalIgnoreCase))
                {
                    return false;
                }

                if (!word.Equals("pi", StringComparison.OrdinalIgnoreCase) && !word.Equals("e", StringComparison.OrdinalIgnoreCase))
                {
                    hasOperator = true;
                }
            }
            else
            {
                return false;
            }
        }

        return hasDigit && hasOperator;
    }

    private SearchResult Message(string title, string subtitle, IconKind icon) => new()
    {
        Id = "calc:message",
        Title = title,
        Subtitle = subtitle,
        Icon = new ResultIcon(icon),
        Score = ResultScore,
        Action = ResultAction.None,
        ActionLabel = string.Empty,
        ProviderName = Name,
        TrackUsage = false,
    };
}
