using System.Globalization;

namespace Kite.Core.Calculation;

/// <summary>the outcome of evaluating a math expression.</summary>
public sealed record CalculationResult(bool Success, double Value, string? Error)
{
    public static CalculationResult Ok(double value) => new(true, value, null);

    public static CalculationResult Fail(string error) => new(false, double.NaN, error);
}

/// <summary>
/// a small math parser. it only understands numbers, + - * / % ^,
/// brackets, a few functions and the constants pi and e. it never runs code.
/// </summary>
public sealed class ExpressionEvaluator
{
    public const int MaxLength = 256;
    private const int MaxDepth = 64;

    private static readonly Dictionary<string, Func<double, double>> Functions = new(StringComparer.OrdinalIgnoreCase)
    {
        ["sqrt"] = Math.Sqrt,
        ["abs"] = Math.Abs,
        ["round"] = v => Math.Round(v, MidpointRounding.AwayFromZero),
        ["floor"] = Math.Floor,
        ["ceil"] = Math.Ceiling,
        ["sin"] = Math.Sin,
        ["cos"] = Math.Cos,
        ["tan"] = Math.Tan,
        ["log"] = Math.Log10,
        ["ln"] = Math.Log,
    };

    private static readonly Dictionary<string, double> Constants = new(StringComparer.OrdinalIgnoreCase)
    {
        ["pi"] = Math.PI,
        ["e"] = Math.E,
    };

    private readonly string _text;
    private int _pos;
    private int _depth;

    private ExpressionEvaluator(string text)
    {
        _text = text;
    }

    /// <summary>names of the supported functions and constants.</summary>
    public static IEnumerable<string> KnownWords => Functions.Keys.Concat(Constants.Keys);

    public static CalculationResult Evaluate(string? expression)
    {
        if (string.IsNullOrWhiteSpace(expression))
        {
            return CalculationResult.Fail("Type an expression.");
        }

        if (expression.Length > MaxLength)
        {
            return CalculationResult.Fail("The expression is too long.");
        }

        var evaluator = new ExpressionEvaluator(expression);
        try
        {
            var value = evaluator.ParseExpression();
            evaluator.SkipSpaces();
            if (evaluator._pos < evaluator._text.Length)
            {
                return CalculationResult.Fail($"Unexpected \"{evaluator._text[evaluator._pos]}\".");
            }

            if (double.IsNaN(value))
            {
                return CalculationResult.Fail("The result is not a number.");
            }

            if (double.IsInfinity(value))
            {
                return CalculationResult.Fail("The result is too large.");
            }

            return CalculationResult.Ok(value);
        }
        catch (CalculationException ex)
        {
            return CalculationResult.Fail(ex.Message);
        }
    }

    /// <summary>show a number in a short, readable way.</summary>
    public static string Format(double value)
    {
        if (value == 0)
        {
            // avoid showing negative zero
            return "0";
        }

        if (Math.Abs(value) < 1e15 && Math.Abs(value % 1) < 1e-9 * Math.Max(1, Math.Abs(value)))
        {
            return Math.Round(value).ToString("0", CultureInfo.InvariantCulture);
        }

        return value.ToString("G12", CultureInfo.InvariantCulture);
    }

    // expression = term, then any number of + or - and another term
    private double ParseExpression()
    {
        EnterLevel();
        var value = ParseTerm();
        while (true)
        {
            SkipSpaces();
            if (TryTake('+'))
            {
                value += ParseTerm();
            }
            else if (TryTake('-') || TryTake('−'))
            {
                value -= ParseTerm();
            }
            else
            {
                break;
            }
        }

        _depth--;
        return value;
    }

    // term = unary, then any number of * / or % and another unary
    private double ParseTerm()
    {
        var value = ParseUnary();
        while (true)
        {
            SkipSpaces();
            if (TryTake('*') || TryTake('×'))
            {
                value *= ParseUnary();
            }
            else if (TryTake('/') || TryTake('÷'))
            {
                var divisor = ParseUnary();
                if (divisor == 0)
                {
                    throw new CalculationException("Cannot divide by zero.");
                }

                value /= divisor;
            }
            else if (TryTake('%'))
            {
                var divisor = ParseUnary();
                if (divisor == 0)
                {
                    throw new CalculationException("Cannot divide by zero.");
                }

                value %= divisor;
            }
            else
            {
                break;
            }
        }

        return value;
    }

    // a minus in front belongs to the whole power, so -2^2 is -4
    private double ParseUnary()
    {
        SkipSpaces();
        if (TryTake('-') || TryTake('−'))
        {
            EnterLevel();
            var value = -ParseUnary();
            _depth--;
            return value;
        }

        if (TryTake('+'))
        {
            EnterLevel();
            var value = ParseUnary();
            _depth--;
            return value;
        }

        return ParsePower();
    }

    // power works from the right, so 2^3^2 is 2^9
    private double ParsePower()
    {
        var baseValue = ParsePrimary();
        SkipSpaces();
        if (TryTake('^'))
        {
            EnterLevel();
            var exponent = ParseUnary();
            _depth--;
            return Math.Pow(baseValue, exponent);
        }

        return baseValue;
    }

    private double ParsePrimary()
    {
        SkipSpaces();
        if (_pos >= _text.Length)
        {
            throw new CalculationException("The expression is incomplete.");
        }

        var ch = _text[_pos];
        if (ch == '(')
        {
            _pos++;
            var value = ParseExpression();
            SkipSpaces();
            if (!TryTake(')'))
            {
                throw new CalculationException("A closing bracket is missing.");
            }

            return value;
        }

        if (char.IsAsciiDigit(ch) || ch == '.')
        {
            return ParseNumber();
        }

        if (char.IsAsciiLetter(ch))
        {
            var start = _pos;
            while (_pos < _text.Length && char.IsAsciiLetter(_text[_pos]))
            {
                _pos++;
            }

            var word = _text[start.._pos];
            if (Constants.TryGetValue(word, out var constant))
            {
                return constant;
            }

            if (Functions.TryGetValue(word, out var function))
            {
                SkipSpaces();
                if (!TryTake('('))
                {
                    throw new CalculationException($"Use brackets after {word.ToLowerInvariant()}, like {word.ToLowerInvariant()}(4).");
                }

                var argument = ParseExpression();
                SkipSpaces();
                if (!TryTake(')'))
                {
                    throw new CalculationException("A closing bracket is missing.");
                }

                return function(argument);
            }

            throw new CalculationException($"Unknown word \"{word}\".");
        }

        throw new CalculationException($"Unexpected \"{ch}\".");
    }

    private double ParseNumber()
    {
        var start = _pos;
        var seenDot = false;
        while (_pos < _text.Length && (char.IsAsciiDigit(_text[_pos]) || _text[_pos] == '.'))
        {
            if (_text[_pos] == '.')
            {
                if (seenDot)
                {
                    throw new CalculationException("A number has two decimal points.");
                }

                seenDot = true;
            }

            _pos++;
        }

        var token = _text[start.._pos];
        if (token == "." ||
            !double.TryParse(token, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var value))
        {
            throw new CalculationException($"\"{token}\" is not a number.");
        }

        return value;
    }

    private void EnterLevel()
    {
        _depth++;
        if (_depth > MaxDepth)
        {
            throw new CalculationException("The expression is nested too deeply.");
        }
    }

    private bool TryTake(char expected)
    {
        if (_pos < _text.Length && _text[_pos] == expected)
        {
            _pos++;
            return true;
        }

        return false;
    }

    private void SkipSpaces()
    {
        while (_pos < _text.Length && char.IsWhiteSpace(_text[_pos]))
        {
            _pos++;
        }
    }

    private sealed class CalculationException(string message) : Exception(message);
}
