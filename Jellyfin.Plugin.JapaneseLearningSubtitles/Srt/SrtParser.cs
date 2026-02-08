using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Jellyfin.Plugin.JapaneseLearningSubtitles.Srt;

/// <summary>
/// Parses SRT subtitle files into a list of <see cref="SubtitleCue"/>.
/// </summary>
public static partial class SrtParser
{
    // Pattern: 00:01:23,456 --> 00:01:25,789
    private static readonly Regex TimecodeRegex = new(
        @"(\d{1,2}):(\d{2}):(\d{2})[,.](\d{3})\s*-->\s*(\d{1,2}):(\d{2}):(\d{2})[,.](\d{3})",
        RegexOptions.Compiled);

    // HTML-style tags like <i>, </i>, <b>, <font color="...">, etc.
    private static readonly Regex HtmlTagRegex = new(
        @"<\/?[^>]+>",
        RegexOptions.Compiled);

    // SRT formatting tags like {\an8}, {\\pos(x,y)}, etc.
    private static readonly Regex AssTagRegex = new(
        @"\{[^}]*\}",
        RegexOptions.Compiled);

    /// <summary>
    /// Parses an SRT file from a file path.
    /// </summary>
    public static async Task<List<SubtitleCue>> ParseFileAsync(string filePath)
    {
        // Try UTF-8 first, then fallback to detect encoding
        var content = await File.ReadAllTextAsync(filePath, Encoding.UTF8).ConfigureAwait(false);

        // Handle BOM
        if (content.Length > 0 && content[0] == '\uFEFF')
        {
            content = content[1..];
        }

        return ParseString(content);
    }

    /// <summary>
    /// Parses SRT content from a string.
    /// </summary>
    public static List<SubtitleCue> ParseString(string content)
    {
        var cues = new List<SubtitleCue>();

        // Normalize line endings
        content = content.Replace("\r\n", "\n").Replace("\r", "\n");

        // Split by double newlines (empty lines separate cues)
        var blocks = content.Split(new[] { "\n\n" }, StringSplitOptions.RemoveEmptyEntries);

        foreach (var block in blocks)
        {
            var cue = ParseBlock(block.Trim());
            if (cue != null)
            {
                cues.Add(cue);
            }
        }

        // Re-index sequentially
        for (int i = 0; i < cues.Count; i++)
        {
            cues[i].Index = i + 1;
        }

        return cues;
    }

    private static SubtitleCue? ParseBlock(string block)
    {
        if (string.IsNullOrWhiteSpace(block))
        {
            return null;
        }

        var lines = block.Split('\n');
        if (lines.Length < 2)
        {
            return null;
        }

        // Find the timecode line (might be line 0 or line 1)
        int timecodeLineIndex = -1;
        Match? timecodeMatch = null;

        for (int i = 0; i < Math.Min(lines.Length, 3); i++)
        {
            var match = TimecodeRegex.Match(lines[i]);
            if (match.Success)
            {
                timecodeLineIndex = i;
                timecodeMatch = match;
                break;
            }
        }

        if (timecodeLineIndex < 0 || timecodeMatch == null)
        {
            return null;
        }

        // Parse timecodes
        long startMs = ParseTimecode(timecodeMatch, 1);
        long endMs = ParseTimecode(timecodeMatch, 5);

        // Collect text lines (everything after timecode line)
        var textLines = new List<string>();
        for (int i = timecodeLineIndex + 1; i < lines.Length; i++)
        {
            var line = lines[i].Trim();
            if (!string.IsNullOrEmpty(line))
            {
                textLines.Add(line);
            }
        }

        string text = string.Join("\n", textLines);

        return new SubtitleCue
        {
            StartMs = startMs,
            EndMs = endMs,
            Text = text
        };
    }

    private static long ParseTimecode(Match match, int startGroup)
    {
        int hours = int.Parse(match.Groups[startGroup].Value, CultureInfo.InvariantCulture);
        int minutes = int.Parse(match.Groups[startGroup + 1].Value, CultureInfo.InvariantCulture);
        int seconds = int.Parse(match.Groups[startGroup + 2].Value, CultureInfo.InvariantCulture);
        int millis = int.Parse(match.Groups[startGroup + 3].Value, CultureInfo.InvariantCulture);

        return (hours * 3600000L) + (minutes * 60000L) + (seconds * 1000L) + millis;
    }

    /// <summary>
    /// Strips HTML/ASS formatting tags from subtitle text.
    /// </summary>
    public static string StripTags(string text)
    {
        text = HtmlTagRegex.Replace(text, string.Empty);
        text = AssTagRegex.Replace(text, string.Empty);
        return text.Trim();
    }
}
