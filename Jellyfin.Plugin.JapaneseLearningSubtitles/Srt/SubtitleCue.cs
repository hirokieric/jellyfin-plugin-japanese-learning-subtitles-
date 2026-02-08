namespace Jellyfin.Plugin.JapaneseLearningSubtitles.Srt;

/// <summary>
/// Represents a single subtitle cue (entry) in an SRT file.
/// </summary>
public class SubtitleCue
{
    /// <summary>
    /// Gets or sets the 1-based cue index.
    /// </summary>
    public int Index { get; set; }

    /// <summary>
    /// Gets or sets the start time in milliseconds.
    /// </summary>
    public long StartMs { get; set; }

    /// <summary>
    /// Gets or sets the end time in milliseconds.
    /// </summary>
    public long EndMs { get; set; }

    /// <summary>
    /// Gets or sets the text content of the cue.
    /// </summary>
    public string Text { get; set; } = string.Empty;

    /// <summary>
    /// Gets the duration in milliseconds.
    /// </summary>
    public long DurationMs => EndMs - StartMs;

    /// <summary>
    /// Returns a debug-friendly string representation.
    /// </summary>
    public override string ToString()
        => $"[{Index}] {FormatTime(StartMs)} --> {FormatTime(EndMs)}: {Text}";

    private static string FormatTime(long ms)
    {
        var ts = TimeSpan.FromMilliseconds(ms);
        return $"{(int)ts.TotalHours:D2}:{ts.Minutes:D2}:{ts.Seconds:D2},{ts.Milliseconds:D3}";
    }
}
