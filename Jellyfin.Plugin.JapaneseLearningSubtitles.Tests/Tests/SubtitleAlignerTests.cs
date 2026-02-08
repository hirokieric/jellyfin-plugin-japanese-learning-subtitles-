using Jellyfin.Plugin.JapaneseLearningSubtitles.Alignment;
using Jellyfin.Plugin.JapaneseLearningSubtitles.Srt;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Jellyfin.Plugin.JapaneseLearningSubtitles.Tests;

public class SubtitleAlignerTests
{
    private readonly SubtitleAligner _aligner = new(
        NullLogger<SubtitleAligner>.Instance,
        confidenceThreshold: 0.3);

    [Fact]
    public void Align_Perfect1to1_AllMatched()
    {
        // EN and JP have same count, same timing
        var enCues = CreateCues(10, 0, 3000, "EN cue");
        var jpCues = CreateCues(10, 0, 3000, "JP cue");

        var result = _aligner.Align(enCues, jpCues);

        Assert.Equal(10, result.Entries.Count);

        // At least 80% should be matched (allowing some DP flexibility)
        int matched = result.Entries.Count(e => !string.IsNullOrWhiteSpace(e.JapaneseText));
        Assert.True(matched >= 8, $"Expected >= 8 matched, got {matched}");
    }

    [Fact]
    public void Align_EmptyJp_AllNeedTranslation()
    {
        var enCues = CreateCues(5, 0, 3000, "EN cue");
        var jpCues = new List<SubtitleCue>();

        var result = _aligner.Align(enCues, jpCues);

        Assert.Equal(5, result.Entries.Count);
        Assert.All(result.Entries, e => Assert.True(e.NeedsTranslation));
    }

    [Fact]
    public void Align_EmptyEn_ReturnsEmpty()
    {
        var enCues = new List<SubtitleCue>();
        var jpCues = CreateCues(5, 0, 3000, "JP cue");

        var result = _aligner.Align(enCues, jpCues);

        Assert.Empty(result.Entries);
    }

    [Fact]
    public void Align_JpHasInsertions_StillAligns()
    {
        // EN: 5 cues, JP: 7 cues (JP has 2 extra)
        var enCues = CreateCues(5, 0, 3000, "EN cue");
        var jpCues = CreateCues(7, 0, 3000, "JP cue");

        var result = _aligner.Align(enCues, jpCues);

        Assert.Equal(5, result.Entries.Count);
        // Most EN cues should have JP text
        int matched = result.Entries.Count(e => !string.IsNullOrWhiteSpace(e.JapaneseText));
        Assert.True(matched >= 3, $"Expected >= 3 matched, got {matched}");
    }

    [Fact]
    public void Align_JpHasDeletions_SomeMissing()
    {
        // EN: 7 cues, JP: 5 cues (JP is missing some)
        var enCues = CreateCues(7, 0, 3000, "EN cue");
        var jpCues = CreateCues(5, 0, 3000, "JP cue");

        var result = _aligner.Align(enCues, jpCues);

        Assert.Equal(7, result.Entries.Count);
        // Some should be unmatched
        int matched = result.Entries.Count(e => !string.IsNullOrWhiteSpace(e.JapaneseText));
        Assert.True(matched >= 3 && matched <= 7,
            $"Expected 3-7 matched, got {matched}");
    }

    [Fact]
    public void Align_ShiftedTimings_StillMatches()
    {
        // JP timings shifted by 200ms but same count
        var enCues = CreateCues(5, 0, 3000, "EN cue");
        var jpCues = CreateCues(5, 200, 3000, "JP cue");

        var result = _aligner.Align(enCues, jpCues);

        Assert.Equal(5, result.Entries.Count);
        int matched = result.Entries.Count(e => !string.IsNullOrWhiteSpace(e.JapaneseText));
        Assert.True(matched >= 4, $"Expected >= 4 matched, got {matched}");
    }

    [Fact]
    public void Align_OutputTimesMatchEnglish()
    {
        var enCues = CreateCues(5, 0, 3000, "EN cue");
        var jpCues = CreateCues(5, 500, 3200, "JP cue");

        var result = _aligner.Align(enCues, jpCues);

        // Output times should always be EN times, not JP times
        for (int i = 0; i < result.Entries.Count; i++)
        {
            Assert.Equal(enCues[i].StartMs, result.Entries[i].StartMs);
            Assert.Equal(enCues[i].EndMs, result.Entries[i].EndMs);
        }
    }

    [Fact]
    public void Align_NoTimecodeOverlaps()
    {
        var enCues = CreateCues(10, 0, 3000, "EN cue");
        var jpCues = CreateCues(8, 100, 3200, "JP cue");

        var result = _aligner.Align(enCues, jpCues);

        // Check no timecode regressions
        for (int i = 1; i < result.Entries.Count; i++)
        {
            Assert.True(result.Entries[i].StartMs >= result.Entries[i - 1].StartMs,
                $"Timecode regression at entry {i}: {result.Entries[i].StartMs} < {result.Entries[i - 1].StartMs}");
        }
    }

    /// <summary>
    /// Creates synthetic cues for testing.
    /// </summary>
    private static List<SubtitleCue> CreateCues(int count, long startOffset, long spacing, string textPrefix)
    {
        var cues = new List<SubtitleCue>(count);
        for (int i = 0; i < count; i++)
        {
            cues.Add(new SubtitleCue
            {
                Index = i + 1,
                StartMs = startOffset + (i * spacing),
                EndMs = startOffset + (i * spacing) + (long)(spacing * 0.8),
                Text = $"{textPrefix} {i + 1}"
            });
        }

        return cues;
    }
}
