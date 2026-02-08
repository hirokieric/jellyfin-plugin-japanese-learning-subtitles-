using Jellyfin.Plugin.JapaneseLearningSubtitles.Srt;
using Xunit;

namespace Jellyfin.Plugin.JapaneseLearningSubtitles.Tests;

public class SrtWriterTests
{
    [Fact]
    public void FormatSrt_BasicCues_FormatsCorrectly()
    {
        var cues = new List<SubtitleCue>
        {
            new() { Index = 1, StartMs = 1000, EndMs = 3500, Text = "Hello" },
            new() { Index = 2, StartMs = 4000, EndMs = 6500, Text = "World" }
        };

        var result = SrtWriter.FormatSrt(cues);

        Assert.Contains("1\r\n", result.Replace("\n", "\r\n")); // Handles both line endings
        Assert.Contains("00:00:01,000 --> 00:00:03,500", result);
        Assert.Contains("Hello", result);
        Assert.Contains("00:00:04,000 --> 00:00:06,500", result);
        Assert.Contains("World", result);
    }

    [Fact]
    public void FormatTimecode_Zero_FormatsCorrectly()
    {
        Assert.Equal("00:00:00,000", SrtWriter.FormatTimecode(0));
    }

    [Fact]
    public void FormatTimecode_Hours_FormatsCorrectly()
    {
        // 1 hour, 23 minutes, 45 seconds, 678 ms
        long ms = (1 * 3600 + 23 * 60 + 45) * 1000 + 678;
        Assert.Equal("01:23:45,678", SrtWriter.FormatTimecode(ms));
    }

    [Fact]
    public void FormatTimecode_Negative_ClampsToZero()
    {
        Assert.Equal("00:00:00,000", SrtWriter.FormatTimecode(-500));
    }

    [Fact]
    public void RoundTrip_ParseThenFormat_PreservesContent()
    {
        var original = new List<SubtitleCue>
        {
            new() { Index = 1, StartMs = 1000, EndMs = 3500, Text = "Hello, world!" },
            new() { Index = 2, StartMs = 4000, EndMs = 6500, Text = "Line one\nLine two" },
            new() { Index = 3, StartMs = 7000, EndMs = 10000, Text = "こんにちは" }
        };

        var formatted = SrtWriter.FormatSrt(original);
        var parsed = SrtParser.ParseString(formatted);

        Assert.Equal(original.Count, parsed.Count);

        for (int i = 0; i < original.Count; i++)
        {
            Assert.Equal(original[i].StartMs, parsed[i].StartMs);
            Assert.Equal(original[i].EndMs, parsed[i].EndMs);
            Assert.Equal(original[i].Text, parsed[i].Text);
        }
    }

    [Fact]
    public void RoundTrip_JapaneseCues_PreservesText()
    {
        var original = new List<SubtitleCue>
        {
            new() { Index = 1, StartMs = 1200, EndMs = 3800, Text = "こんにちは、お元気ですか？" },
            new() { Index = 2, StartMs = 4200, EndMs = 6800, Text = "元気です、ありがとう。" }
        };

        var formatted = SrtWriter.FormatSrt(original);
        var parsed = SrtParser.ParseString(formatted);

        Assert.Equal(2, parsed.Count);
        Assert.Equal("こんにちは、お元気ですか？", parsed[0].Text);
        Assert.Equal("元気です、ありがとう。", parsed[1].Text);
    }
}
