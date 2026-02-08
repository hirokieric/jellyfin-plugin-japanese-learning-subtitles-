using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace Jellyfin.Plugin.JapaneseLearningSubtitles.Srt;

/// <summary>
/// Writes subtitle cues to SRT format.
/// </summary>
public static class SrtWriter
{
    /// <summary>
    /// Writes cues to an SRT file (UTF-8 with BOM for maximum compatibility).
    /// </summary>
    public static async Task WriteFileAsync(string filePath, IReadOnlyList<SubtitleCue> cues)
    {
        var content = FormatSrt(cues);
        var utf8Bom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true);
        await File.WriteAllTextAsync(filePath, content, utf8Bom).ConfigureAwait(false);
    }

    /// <summary>
    /// Formats cues as an SRT string.
    /// </summary>
    public static string FormatSrt(IReadOnlyList<SubtitleCue> cues)
    {
        var sb = new StringBuilder();

        for (int i = 0; i < cues.Count; i++)
        {
            var cue = cues[i];

            // Cue index (1-based)
            sb.AppendLine((i + 1).ToString());

            // Timecode line
            sb.Append(FormatTimecode(cue.StartMs));
            sb.Append(" --> ");
            sb.AppendLine(FormatTimecode(cue.EndMs));

            // Text (may be multi-line)
            sb.AppendLine(cue.Text);

            // Blank line separator
            sb.AppendLine();
        }

        return sb.ToString();
    }

    /// <summary>
    /// Formats milliseconds as SRT timecode: HH:MM:SS,mmm
    /// </summary>
    public static string FormatTimecode(long milliseconds)
    {
        if (milliseconds < 0)
        {
            milliseconds = 0;
        }

        var ts = TimeSpan.FromMilliseconds(milliseconds);
        return $"{(int)ts.TotalHours:D2}:{ts.Minutes:D2}:{ts.Seconds:D2},{ts.Milliseconds:D3}";
    }
}
