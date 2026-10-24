using Kite.Core.Calculation;
using Kite.Core.Providers;
using Kite.Core.Search;

namespace Kite.Core.Tests;

public class CalculatorTests
{
    [Theory]
    [InlineData("25 * 4", 100)]
    [InlineData("(120 + 30) / 5", 30)]
    [InlineData("2^8", 256)]
    [InlineData("2 ^ 3 ^ 2", 512)]
    [InlineData("-2^2", -4)]
    [InlineData("(-2)^2", 4)]
    [InlineData("2^-1", 0.5)]
    [InlineData("1 + 2 * 3", 7)]
    [InlineData("10 - 4 - 3", 3)]
    [InlineData("100 / 10 / 2", 5)]
    [InlineData("10 % 4", 2)]
    [InlineData("--5", 5)]
    [InlineData("3.5 * 2", 7)]
    [InlineData(".5 + .5", 1)]
    [InlineData("sqrt(16) + abs(-2)", 6)]
    [InlineData("round(2.5)", 3)]
    [InlineData("6 × 7", 42)]
    [InlineData("84 ÷ 2", 42)]
    public void EvaluatesValidExpressions(string expression, double expected)
    {
        var result = ExpressionEvaluator.Evaluate(expression);

        Assert.True(result.Success, result.Error);
        Assert.Equal(expected, result.Value, 9);
    }

    [Fact]
    public void KnowsPi()
    {
        var result = ExpressionEvaluator.Evaluate("2 * pi");

        Assert.True(result.Success);
        Assert.Equal(Math.PI * 2, result.Value, 9);
    }

    [Theory]
    [InlineData("1 / 0", "divide by zero")]
    [InlineData("5 % 0", "divide by zero")]
    [InlineData("1 / (2 - 2)", "divide by zero")]
    [InlineData("25 *", "incomplete")]
    [InlineData("(1 + 2", "bracket")]
    [InlineData("1 + 2)", "Unexpected")]
    [InlineData("1..2", "two decimal points")]
    [InlineData("foo(2)", "Unknown word")]
    [InlineData("sqrt 4", "brackets")]
    [InlineData("sqrt(-1)", "not a number")]
    [InlineData("10^400", "too large")]
    public void ReportsProblemsWithoutThrowing(string expression, string expectedMessagePart)
    {
        var result = ExpressionEvaluator.Evaluate(expression);

        Assert.False(result.Success);
        Assert.Contains(expectedMessagePart, result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RejectsVeryDeepNesting()
    {
        var expression = new string('(', 200) + "1" + new string(')', 200);

        var result = ExpressionEvaluator.Evaluate(expression);

        Assert.False(result.Success);
    }

    [Fact]
    public void RejectsVeryLongInput()
    {
        var expression = string.Join("+", Enumerable.Repeat("1", 200));

        Assert.False(ExpressionEvaluator.Evaluate(expression).Success);
    }

    [Theory]
    [InlineData(100, "100")]
    [InlineData(0.1 + 0.2, "0.3")]
    [InlineData(-0.0, "0")]
    [InlineData(1.0 / 3, "0.333333333333")]
    [InlineData(1e20, "1E+20")]
    public void FormatsNumbersReadably(double value, string expected)
    {
        Assert.Equal(expected, ExpressionEvaluator.Format(value));
    }

    [Theory]
    [InlineData("25 * 4", true)]
    [InlineData("2^8", true)]
    [InlineData("sqrt(9)", true)]
    [InlineData("42", false)]
    [InlineData("-5", false)]
    [InlineData("notepad", false)]
    [InlineData("g 2+2", false)]
    [InlineData("calc 2 + 2", false)]
    [InlineData("System.IO.File.Delete(1)", false)]
    [InlineData("rm -rf /", false)]
    public void DetectsMathOnlyWhenItLooksLikeMath(string text, bool expected)
    {
        Assert.Equal(expected, CalculatorProvider.LooksLikeMath(text));
    }

    [Fact]
    public async Task ProviderCopiesTheAnswer()
    {
        var provider = new CalculatorProvider();

        var results = await provider.SearchAsync(new SearchQuery("(120 + 30) / 5"), CancellationToken.None);

        var result = Assert.Single(results);
        Assert.Equal("30", result.Title);
        Assert.Equal(ActionKind.CopyText, result.Action.Kind);
        Assert.Equal("30", result.Action.Target);
    }

    [Fact]
    public async Task ProviderShowsDivisionByZeroAsMessage()
    {
        var provider = new CalculatorProvider();

        var results = await provider.SearchAsync(new SearchQuery("8 / 0"), CancellationToken.None);

        var result = Assert.Single(results);
        Assert.Equal(ActionKind.None, result.Action.Kind);
        Assert.Contains("divide by zero", result.Title, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task EqualsSignForcesCalculatorMode()
    {
        var provider = new CalculatorProvider();

        var results = await provider.SearchAsync(new SearchQuery("=42"), CancellationToken.None);

        Assert.Equal("42", Assert.Single(results).Title);
    }
}
