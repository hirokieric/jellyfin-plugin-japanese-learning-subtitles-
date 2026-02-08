using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.JapaneseLearningSubtitles.Providers;

/// <summary>
/// Locates English SRT sidecar subtitles for a given media file.
/// </summary>
public class EnglishSubtitleLocator
{
    private readonly ILogger<EnglishSubtitleLocator> _logger;

    // Priority order for English language tags in filenames
    private static readonly string[] EnglishTags = { ".en.", ".eng.", ".english." };

    // Priority order for default subtitles (no language tag)
    private static readonly string[] DefaultTags = { ".default.", ".forced." };

    /// <summary>
    /// Initializes a new instance of the <see cref="EnglishSubtitleLocator"/> class.
    /// </summary>
    public EnglishSubtitleLocator(ILogger<EnglishSubtitleLocator> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Finds the best English SRT subtitle for the given media file path.
    /// </summary>
    /// <param name="mediaFilePath">Full path to the media file (e.g., /movies/Foo.mkv).</param>
    /// <returns>Path to the best English SRT, or null if not found.</returns>
    public string? FindEnglishSrt(string mediaFilePath)
    {
        var directory = Path.GetDirectoryName(mediaFilePath);
        if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory))
        {
            _logger.LogWarning("Media directory not found: {Path}", directory);
            return null;
        }

        var mediaNameWithoutExt = Path.GetFileNameWithoutExtension(mediaFilePath);

        // Get all SRT files in the same directory
        var srtFiles = Directory.GetFiles(directory, "*.srt", SearchOption.TopDirectoryOnly);

        _logger.LogDebug("Found {Count} SRT files in {Dir}", srtFiles.Length, directory);

        // Candidates: SRT files whose name starts with the media file name
        var candidates = srtFiles
            .Where(f => Path.GetFileName(f).StartsWith(mediaNameWithoutExt, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (candidates.Count == 0)
        {
            _logger.LogDebug("No SRT files match media name '{Name}' in {Dir}", mediaNameWithoutExt, directory);
            return null;
        }

        // Score and sort candidates
        var scored = candidates
            .Select(f => new { Path = f, Score = ScoreEnglishSrt(f, mediaNameWithoutExt) })
            .Where(x => x.Score >= 0) // Exclude negatively scored (non-English language tags)
            .OrderByDescending(x => x.Score)
            .ToList();

        if (scored.Count == 0)
        {
            _logger.LogDebug("No English-tagged SRT found for '{Name}'", mediaNameWithoutExt);
            return null;
        }

        var best = scored[0];
        _logger.LogInformation("Selected English SRT: {Path} (score: {Score})", best.Path, best.Score);
        return best.Path;
    }

    /// <summary>
    /// Checks whether a Japanese SRT sidecar already exists for the given media file.
    /// </summary>
    public bool JapaneseSrtExists(string mediaFilePath)
    {
        var jaPath = GetJapaneseSrtPath(mediaFilePath);
        return File.Exists(jaPath);
    }

    /// <summary>
    /// Gets the expected path for the Japanese SRT sidecar.
    /// </summary>
    public static string GetJapaneseSrtPath(string mediaFilePath)
    {
        var directory = Path.GetDirectoryName(mediaFilePath) ?? ".";
        var nameWithoutExt = Path.GetFileNameWithoutExtension(mediaFilePath);
        return Path.Combine(directory, $"{nameWithoutExt}.ja.srt");
    }

    /// <summary>
    /// Scores an SRT file for being the best English subtitle.
    /// Higher is better. Negative means "exclude" (non-English language detected).
    /// </summary>
    private int ScoreEnglishSrt(string srtPath, string mediaName)
    {
        var fileName = Path.GetFileName(srtPath).ToLowerInvariant();
        var suffix = fileName[mediaName.Length..]; // Part after media name

        int score = 0;

        // Check for explicit English tags (highest priority)
        foreach (var tag in EnglishTags)
        {
            if (suffix.Contains(tag, StringComparison.OrdinalIgnoreCase))
            {
                score += 100;
                break;
            }
        }

        // Check for non-English language tags → exclude
        if (HasNonEnglishLanguageTag(suffix))
        {
            return -1;
        }

        // Check for default/forced tags
        foreach (var tag in DefaultTags)
        {
            if (suffix.Contains(tag, StringComparison.OrdinalIgnoreCase))
            {
                score += 50;
            }
        }

        // Simpler name = higher priority (fewer dots/segments)
        int dotCount = suffix.Count(c => c == '.');
        score += Math.Max(0, 10 - dotCount);

        // If it's just ".srt" (exact media name match), give it a decent base score
        if (suffix.Equals(".srt", StringComparison.OrdinalIgnoreCase))
        {
            score += 80; // Likely the "default" subtitle
        }

        return score;
    }

    /// <summary>
    /// Checks if the suffix contains a non-English language tag.
    /// </summary>
    private static bool HasNonEnglishLanguageTag(string suffix)
    {
        // Common non-English language codes to exclude
        var nonEnglishTags = new[]
        {
            ".ja.", ".jpn.", ".japanese.",
            ".fr.", ".fre.", ".french.",
            ".de.", ".ger.", ".german.",
            ".es.", ".spa.", ".spanish.",
            ".it.", ".ita.", ".italian.",
            ".pt.", ".por.", ".portuguese.",
            ".ru.", ".rus.", ".russian.",
            ".zh.", ".chi.", ".chinese.",
            ".ko.", ".kor.", ".korean.",
            ".ar.", ".ara.", ".arabic.",
            ".hi.", ".hin.", ".hindi.",
            ".th.", ".tha.", ".thai.",
            ".vi.", ".vie.", ".vietnamese.",
            ".pl.", ".pol.", ".polish.",
            ".nl.", ".dut.", ".dutch.",
            ".sv.", ".swe.", ".swedish.",
            ".no.", ".nor.", ".norwegian.",
            ".da.", ".dan.", ".danish.",
            ".fi.", ".fin.", ".finnish.",
        };

        foreach (var tag in nonEnglishTags)
        {
            if (suffix.Contains(tag, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
