using Jellyfin.Plugin.JapaneseLearningSubtitles.Srt;
using Xunit;

namespace Jellyfin.Plugin.JapaneseLearningSubtitles.Tests;

public class TextNormalizerTests
{
    [Theory]
    [InlineData("Hello, World!", "hello world")]
    [InlineData("I'm fine.", "i'm fine")]
    [InlineData("  Multiple   spaces  ", "multiple spaces")]
    [InlineData("<i>Tagged text</i>", "tagged text")]
    [InlineData("♪ Music playing ♪", "playing")]
    [InlineData("[Music]", "")]
    [InlineData("(singing)", "")]
    public void NormalizeEnglish_Various(string input, string expected)
    {
        Assert.Equal(expected, TextNormalizer.NormalizeEnglish(input));
    }

    [Theory]
    [InlineData("こんにちは。", "こんにちは")]
    [InlineData("元気ですか？", "元気ですか")]
    [InlineData("「テスト」", "テスト")]
    [InlineData("テスト！テスト", "テストテスト")]
    public void NormalizeJapanese_Various(string input, string expected)
    {
        Assert.Equal(expected, TextNormalizer.NormalizeJapanese(input));
    }

    [Theory]
    [InlineData("Ａ", "A")] // Full-width A → A
    [InlineData("０", "0")] // Full-width 0 → 0
    [InlineData("\u3000", " ")] // Ideographic space → ASCII space
    public void NormalizeJapanese_FullWidth(string input, string expected)
    {
        Assert.Equal(expected, TextNormalizer.NormalizeJapanese(input));
    }

    [Theory]
    [InlineData("", true)]
    [InlineData("   ", true)]
    [InlineData("♪", true)]
    [InlineData("♪♪♪", true)]
    [InlineData("♪ ♪", true)]
    [InlineData("[Music]", true)]
    [InlineData("(singing)", true)]
    [InlineData("Hello", false)]
    [InlineData("♪ Let's go ♪", false)]
    public void IsEmptyOrMarker_Various(string input, bool expected)
    {
        Assert.Equal(expected, TextNormalizer.IsEmptyOrMarker(input));
    }
}
