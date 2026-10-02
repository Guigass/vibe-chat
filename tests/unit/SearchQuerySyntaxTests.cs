using FluentAssertions;
using VibeChat.Search;

namespace VibeChat.UnitTests;

public sealed class SearchQuerySyntaxTests
{
    [Theory]
    [InlineData("reuni", true)]
    [InlineData("\"plano Q3\"", true)]
    [InlineData("foo OR bar", true)]
    [InlineData("reuni -excluir", true)]
    [InlineData("-excluir", false)]
    [InlineData("OR", false)]
    [InlineData("\"\"", false)]
    [InlineData("", false)]
    public void HasPositiveTerm_detects_words_and_phrases(string raw, bool expected)
    {
        SearchQuerySyntax.HasPositiveTerm(raw).Should().Be(expected);
    }
}
