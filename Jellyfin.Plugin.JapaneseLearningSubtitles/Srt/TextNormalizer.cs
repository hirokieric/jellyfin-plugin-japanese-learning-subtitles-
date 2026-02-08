using System.Text.RegularExpressions;

namespace Jellyfin.Plugin.JapaneseLearningSubtitles.Srt;

/// <summary>
/// Text normalization utilities for subtitle matching.
/// </summary>
public static partial class TextNormalizer
{
    private static readonly Regex MusicMarkerRegex = new(
        @"[♪♫🎵🎶#]+|^\s*[\(\[]\s*(?:music|singing|song|instrumental)\s*[\)\]]\s*$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex WhitespaceRegex = new(
        @"\s+",
        RegexOptions.Compiled);

    private static readonly Regex EnglishPunctuationRegex = new(
        @"[^\w\s']",
        RegexOptions.Compiled);

    private static readonly Regex JapanesePunctuationRegex = new(
        @"[。、！？「」『』（）…―～・\u3000\uFF01-\uFF5E]",
        RegexOptions.Compiled);

    /// <summary>
    /// Normalizes English text for matching purposes.
    /// </summary>
    public static string NormalizeEnglish(string text)
    {
        // Strip tags
        text = SrtParser.StripTags(text);

        // Remove music markers
        text = MusicMarkerRegex.Replace(text, string.Empty);

        // Lowercase
        text = text.ToLowerInvariant();

        // Remove punctuation (keep apostrophes for contractions)
        text = EnglishPunctuationRegex.Replace(text, " ");

        // Collapse whitespace
        text = WhitespaceRegex.Replace(text, " ").Trim();

        return text;
    }

    /// <summary>
    /// Normalizes Japanese text for matching purposes.
    /// </summary>
    public static string NormalizeJapanese(string text)
    {
        // Strip tags
        text = SrtParser.StripTags(text);

        // Remove Japanese punctuation
        text = JapanesePunctuationRegex.Replace(text, string.Empty);

        // Normalize full-width ASCII to half-width
        text = NormalizeFullWidth(text);

        // Collapse whitespace
        text = WhitespaceRegex.Replace(text, " ").Trim();

        return text;
    }

    /// <summary>
    /// Checks if the text is essentially empty or a non-translatable marker (music, SFX).
    /// </summary>
    public static bool IsEmptyOrMarker(string text)
    {
        var stripped = SrtParser.StripTags(text).Trim();
        if (string.IsNullOrWhiteSpace(stripped))
        {
            return true;
        }

        // Check if it's only music symbols
        var afterMusic = MusicMarkerRegex.Replace(stripped, string.Empty).Trim();
        return string.IsNullOrWhiteSpace(afterMusic);
    }

    /// <summary>
    /// Converts full-width ASCII characters (U+FF01..U+FF5E) to half-width (U+0021..U+007E).
    /// </summary>
    private static string NormalizeFullWidth(string text)
    {
        var chars = text.ToCharArray();
        for (int i = 0; i < chars.Length; i++)
        {
            if (chars[i] >= '\uFF01' && chars[i] <= '\uFF5E')
            {
                chars[i] = (char)(chars[i] - 0xFEE0);
            }
            else if (chars[i] == '\u3000') // Ideographic space → ASCII space
            {
                chars[i] = ' ';
            }
        }

        return new string(chars);
    }
}
