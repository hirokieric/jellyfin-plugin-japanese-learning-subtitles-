using Jellyfin.Plugin.JapaneseLearningSubtitles.Srt;
using Xunit;

namespace Jellyfin.Plugin.JapaneseLearningSubtitles.Tests;

public class SrtParserTests
{
    [Fact]
    public void ParseString_BasicSrt_ParsesCorrectly()
    {
        var srt = @"1
00:00:01,000 --> 00:00:03,500
Hello, how are you?

2
00:00:04,000 --> 00:00:06,500
I'm fine, thank you.
";

        var cues = SrtParser.ParseString(srt);

        Assert.Equal(2, cues.Count);
        Assert.Equal(1, cues[0].Index);
        Assert.Equal(1000, cues[0].StartMs);
        Assert.Equal(3500, cues[0].EndMs);
        Assert.Equal("Hello, how are you?", cues[0].Text);
        Assert.Equal(2, cues[1].Index);
        Assert.Equal(4000, cues[1].StartMs);
        Assert.Equal(6500, cues[1].EndMs);
        Assert.Equal("I'm fine, thank you.", cues[1].Text);
    }

    [Fact]
    public void ParseString_MultilineText_PreservesNewlines()
    {
        var srt = @"1
00:00:31,000 --> 00:00:34,000
This is a beautiful day
for an adventure.
";

        var cues = SrtParser.ParseString(srt);

        Assert.Single(cues);
        Assert.Contains("This is a beautiful day", cues[0].Text);
        Assert.Contains("for an adventure.", cues[0].Text);
    }

    [Fact]
    public void ParseString_EmptyInput_ReturnsEmpty()
    {
        var cues = SrtParser.ParseString("");
        Assert.Empty(cues);
    }

    [Fact]
    public void ParseString_DotSeparator_ParsesCorrectly()
    {
        // Some SRT files use period instead of comma in timecodes
        var srt = @"1
00:00:01.000 --> 00:00:03.500
Hello
";

        var cues = SrtParser.ParseString(srt);
        Assert.Single(cues);
        Assert.Equal(1000, cues[0].StartMs);
        Assert.Equal(3500, cues[0].EndMs);
    }

    [Fact]
    public void ParseString_JapaneseText_ParsesCorrectly()
    {
        var srt = @"1
00:00:01,200 --> 00:00:03,800
こんにちは、お元気ですか？

2
00:00:04,200 --> 00:00:06,800
元気です、ありがとう。
";

        var cues = SrtParser.ParseString(srt);

        Assert.Equal(2, cues.Count);
        Assert.Equal("こんにちは、お元気ですか？", cues[0].Text);
        Assert.Equal("元気です、ありがとう。", cues[1].Text);
    }

    [Fact]
    public void ParseString_ReindexesSequentially()
    {
        var srt = @"5
00:00:01,000 --> 00:00:02,000
First

10
00:00:03,000 --> 00:00:04,000
Second
";

        var cues = SrtParser.ParseString(srt);
        Assert.Equal(1, cues[0].Index);
        Assert.Equal(2, cues[1].Index);
    }

    [Fact]
    public void StripTags_HtmlTags_Stripped()
    {
        Assert.Equal("Hello, world!", SrtParser.StripTags("<i>Hello, world!</i>"));
        Assert.Equal("Bold text", SrtParser.StripTags("<b>Bold text</b>"));
        Assert.Equal("Colored", SrtParser.StripTags("<font color=\"#FFFF00\">Colored</font>"));
    }

    [Fact]
    public void StripTags_AssTags_Stripped()
    {
        Assert.Equal("Text here", SrtParser.StripTags("{\\an8}Text here"));
        Assert.Equal("Positioned", SrtParser.StripTags("{\\pos(320,50)}Positioned"));
    }

    [Fact]
    public void StripTags_Mixed_Stripped()
    {
        Assert.Equal("Hello world", SrtParser.StripTags("{\\an8}<i>Hello world</i>"));
    }
}
