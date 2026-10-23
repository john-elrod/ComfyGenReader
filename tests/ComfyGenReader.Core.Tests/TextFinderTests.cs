using ComfyGenReader.Core;

namespace ComfyGenReader.Core.Tests;

public sealed class TextFinderTests
{
    [Fact]
    public void FindAll_IsCaseInsensitive() =>
        Assert.Equal([0, 8, 16], TextFinder.FindAll("Seed 1, seed 2, SEED", "seed"));

    [Fact]
    public void FindAll_DoesNotOverlap() => Assert.Equal([0, 2], TextFinder.FindAll("aaaaa", "aa"));

    [Fact]
    public void FindAll_EmptyQuery_FindsNothing() => Assert.Empty(TextFinder.FindAll("abc", ""));

    [Fact]
    public void FindAll_NoMatch_FindsNothing() => Assert.Empty(TextFinder.FindAll("abc", "x"));

    [Theory]
    [InlineData(0, 0)]
    [InlineData(5, 1)]
    [InlineData(10, 1)]
    [InlineData(11, 2)]
    [InlineData(21, 0)] // past the last match: wraps to the first
    public void NextIndex_FirstMatchAtOrAfterPosition(int position, int expected) =>
        Assert.Equal(expected, TextFinder.NextIndex([0, 10, 20], position));

    [Theory]
    [InlineData(20, 1)]
    [InlineData(11, 1)]
    [InlineData(10, 0)]
    [InlineData(0, 2)] // before the first match: wraps to the last
    public void PreviousIndex_LastMatchBeforePosition(int position, int expected) =>
        Assert.Equal(expected, TextFinder.PreviousIndex([0, 10, 20], position));

    [Fact]
    public void NoMatches_ReturnMinusOne()
    {
        Assert.Equal(-1, TextFinder.NextIndex([], 0));
        Assert.Equal(-1, TextFinder.PreviousIndex([], 0));
    }
}
