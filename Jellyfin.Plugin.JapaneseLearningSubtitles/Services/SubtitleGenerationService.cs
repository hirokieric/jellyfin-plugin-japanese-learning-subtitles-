using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.JapaneseLearningSubtitles.Alignment;
using Jellyfin.Plugin.JapaneseLearningSubtitles.Cache;
using Jellyfin.Plugin.JapaneseLearningSubtitles.Configuration;
using Jellyfin.Plugin.JapaneseLearningSubtitles.Providers;
using Jellyfin.Plugin.JapaneseLearningSubtitles.Srt;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.JapaneseLearningSubtitles.Services;

/// <summary>
/// Result of a single item generation.
/// </summary>
public class GenerationResult
{
    /// <summary>Gets or sets the item name.</summary>
    public string ItemName { get; set; } = string.Empty;

    /// <summary>Gets or sets a value indicating whether generation succeeded.</summary>
    public bool Success { get; set; }

    /// <summary>Gets or sets the output file path (null if skipped/failed).</summary>
    public string? OutputPath { get; set; }

    /// <summary>Gets or sets the source used.</summary>
    public string Source { get; set; } = string.Empty;

    /// <summary>Gets or sets a skip/error message if applicable.</summary>
    public string? Message { get; set; }

    /// <summary>Gets or sets the number of cues generated.</summary>
    public int CueCount { get; set; }

    /// <summary>Gets or sets the coverage percentage.</summary>
    public double CoveragePercent { get; set; }
}

/// <summary>
/// Core service for generating Japanese subtitles for a single media item.
/// Shared between the API controller (on-demand) and the scheduled task (batch).
/// </summary>
public class SubtitleGenerationService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<SubtitleGenerationService> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="SubtitleGenerationService"/> class.
    /// </summary>
    public SubtitleGenerationService(
        IHttpClientFactory httpClientFactory,
        ILoggerFactory loggerFactory,
        ILogger<SubtitleGenerationService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _loggerFactory = loggerFactory;
        _logger = logger;
    }

    /// <summary>
    /// Generates Japanese subtitles for a single media item.
    /// </summary>
    /// <param name="item">The Jellyfin media item.</param>
    /// <param name="overwrite">Whether to overwrite existing .ja.srt.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Generation result.</returns>
    public async Task<GenerationResult> GenerateForItemAsync(
        BaseItem item,
        bool overwrite,
        CancellationToken ct)
    {
        var config = Plugin.Instance?.Configuration;
        if (config == null)
        {
            return new GenerationResult
            {
                ItemName = item.Name,
                Success = false,
                Message = "Plugin configuration is not available."
            };
        }

        var mediaPath = item.Path;
        if (string.IsNullOrEmpty(mediaPath))
        {
            return new GenerationResult
            {
                ItemName = item.Name,
                Success = false,
                Message = "Item has no file path."
            };
        }

        var enLocator = new EnglishSubtitleLocator(
            _loggerFactory.CreateLogger<EnglishSubtitleLocator>());

        // Check if JA SRT already exists
        if (!overwrite && enLocator.JapaneseSrtExists(mediaPath))
        {
            return new GenerationResult
            {
                ItemName = item.Name,
                Success = false,
                Message = "Japanese subtitle already exists. Enable overwrite to regenerate."
            };
        }

        // Find English SRT
        var enSrtPath = enLocator.FindEnglishSrt(mediaPath);
        if (string.IsNullOrEmpty(enSrtPath))
        {
            return new GenerationResult
            {
                ItemName = item.Name,
                Success = false,
                Message = "No English SRT subtitle found for this item."
            };
        }

        // Cache check
        var cacheStore = new GenerationCacheStore(
            Plugin.Instance!.DataFolderPath,
            _loggerFactory.CreateLogger<GenerationCacheStore>());

        if (!overwrite && cacheStore.ShouldSkip(item.Id, enSrtPath, false))
        {
            return new GenerationResult
            {
                ItemName = item.Name,
                Success = false,
                Message = "Already generated and English subtitle unchanged."
            };
        }

        _logger.LogInformation("Generating JP subtitles for: {Name}", item.Name);

        // Parse English SRT
        var enCues = await SrtParser.ParseFileAsync(enSrtPath).ConfigureAwait(false);
        if (enCues.Count == 0)
        {
            return new GenerationResult
            {
                ItemName = item.Name,
                Success = false,
                Message = "English SRT is empty."
            };
        }

        // Initialize translation provider
        var translationFactory = new TranslationProviderFactory(_httpClientFactory, _loggerFactory);
        var translator = translationFactory.Create(config);

        // Initialize alignment engine
        var aligner = new SubtitleAligner(
            _loggerFactory.CreateLogger<SubtitleAligner>(),
            config.AlignmentConfidenceThreshold);

        // Try OpenSubtitles
        List<SubtitleCue>? jpCues = null;
        string sourceUsed = "Translated";
        bool usedOpenSubs = false;

        if (!string.IsNullOrEmpty(config.OpenSubtitlesApiKey) &&
            !string.IsNullOrEmpty(config.OpenSubtitlesUsername) &&
            !string.IsNullOrEmpty(config.OpenSubtitlesPassword))
        {
            using var osClient = new OpenSubtitlesClient(
                _httpClientFactory.CreateClient("OpenSubtitles"),
                _loggerFactory.CreateLogger<OpenSubtitlesClient>());

            var loggedIn = await osClient.LoginAsync(
                config.OpenSubtitlesUsername,
                config.OpenSubtitlesPassword,
                config.OpenSubtitlesApiKey,
                ct).ConfigureAwait(false);

            if (loggedIn)
            {
                jpCues = await TryFetchOpenSubtitlesJapanese(item, osClient, ct).ConfigureAwait(false);

                if (jpCues != null && jpCues.Count > 0)
                {
                    usedOpenSubs = true;
                    sourceUsed = "OpenSubtitles";
                    _logger.LogInformation("Found {Count} JP cues from OpenSubtitles", jpCues.Count);
                }
            }
        }

        // Build result cues
        List<SubtitleCue> resultCues;

        if (usedOpenSubs && jpCues != null && jpCues.Count > 0)
        {
            var alignmentResult = aligner.Align(enCues, jpCues);

            // Translate low-confidence cues
            var needsTranslation = alignmentResult.Entries
                .Where(e => e.NeedsTranslation && !TextNormalizer.IsEmptyOrMarker(e.EnglishText))
                .Select(e => new ContextCue
                {
                    CueIndex = e.EnCueIndex,
                    PreviousText = e.EnCueIndex > 0 ? enCues[e.EnCueIndex - 1].Text : null,
                    CurrentText = SrtParser.StripTags(e.EnglishText),
                    NextText = e.EnCueIndex < enCues.Count - 1 ? enCues[e.EnCueIndex + 1].Text : null
                })
                .ToList();

            if (needsTranslation.Count > 0)
            {
                _logger.LogInformation("Translating {Count} low-confidence cues", needsTranslation.Count);

                var translations = await translator.TranslateBatchAsync(needsTranslation, ct).ConfigureAwait(false);

                for (int i = 0; i < needsTranslation.Count && i < translations.Count; i++)
                {
                    var entry = alignmentResult.Entries[needsTranslation[i].CueIndex];
                    if (!string.IsNullOrWhiteSpace(translations[i]))
                    {
                        entry.JapaneseText = translations[i];
                        entry.NeedsTranslation = false;
                    }
                }

                sourceUsed = "OpenSubtitles+Translated";
            }

            resultCues = alignmentResult.Entries.Select((e, i) => new SubtitleCue
            {
                Index = i + 1,
                StartMs = e.StartMs,
                EndMs = e.EndMs,
                Text = ReflowJapaneseText(e.JapaneseText, config.MaxJapaneseCharsPerLine, config.MaxSubtitleLines)
            }).ToList();
        }
        else
        {
            // Full translation
            _logger.LogInformation("No JP from OpenSubtitles, translating all {Count} cues", enCues.Count);

            var contextCues = enCues.Select((c, i) => new ContextCue
            {
                CueIndex = i,
                PreviousText = i > 0 ? SrtParser.StripTags(enCues[i - 1].Text) : null,
                CurrentText = SrtParser.StripTags(c.Text),
                NextText = i < enCues.Count - 1 ? SrtParser.StripTags(enCues[i + 1].Text) : null
            }).ToList();

            var translations = await translator.TranslateBatchAsync(contextCues, ct).ConfigureAwait(false);

            resultCues = enCues.Select((c, i) => new SubtitleCue
            {
                Index = i + 1,
                StartMs = c.StartMs,
                EndMs = c.EndMs,
                Text = i < translations.Count
                    ? ReflowJapaneseText(translations[i], config.MaxJapaneseCharsPerLine, config.MaxSubtitleLines)
                    : string.Empty
            }).ToList();
        }

        // Write output
        var outputPath = EnglishSubtitleLocator.GetJapaneseSrtPath(mediaPath);
        await SrtWriter.WriteFileAsync(outputPath, resultCues).ConfigureAwait(false);

        _logger.LogInformation("Saved: {Path} ({CueCount} cues)", outputPath, resultCues.Count);

        // Update cache
        double coverage = resultCues.Count > 0
            ? resultCues.Count(c => !string.IsNullOrWhiteSpace(c.Text)) * 100.0 / resultCues.Count
            : 0;

        cacheStore.SaveRecord(item.Id, new GenerationRecord
        {
            LastGeneratedAt = DateTime.UtcNow,
            SourceUsed = sourceUsed,
            BaseEnglishSubtitleHash = GenerationCacheStore.ComputeFileHash(enSrtPath),
            AlignmentVersion = "1.0",
            OutputPath = outputPath,
            CoveragePercent = coverage
        });

        return new GenerationResult
        {
            ItemName = item.Name,
            Success = true,
            OutputPath = outputPath,
            Source = sourceUsed,
            CueCount = resultCues.Count,
            CoveragePercent = coverage
        };
    }

    /// <summary>
    /// Checks the current status of Japanese subtitle generation for an item.
    /// </summary>
    public GenerationStatusResult GetStatus(BaseItem item)
    {
        var mediaPath = item.Path;
        if (string.IsNullOrEmpty(mediaPath))
        {
            return new GenerationStatusResult { HasJapaneseSrt = false };
        }

        var enLocator = new EnglishSubtitleLocator(
            _loggerFactory.CreateLogger<EnglishSubtitleLocator>());

        var jaPath = EnglishSubtitleLocator.GetJapaneseSrtPath(mediaPath);
        bool hasJa = System.IO.File.Exists(jaPath);
        bool hasEn = enLocator.FindEnglishSrt(mediaPath) != null;

        var cacheStore = new GenerationCacheStore(
            Plugin.Instance!.DataFolderPath,
            _loggerFactory.CreateLogger<GenerationCacheStore>());

        var record = cacheStore.GetRecord(item.Id);

        return new GenerationStatusResult
        {
            HasEnglishSrt = hasEn,
            HasJapaneseSrt = hasJa,
            JapaneseSrtPath = hasJa ? jaPath : null,
            LastGeneratedAt = record?.LastGeneratedAt,
            Source = record?.SourceUsed,
            CoveragePercent = record?.CoveragePercent
        };
    }

    private async Task<List<SubtitleCue>?> TryFetchOpenSubtitlesJapanese(
        BaseItem item,
        OpenSubtitlesClient osClient,
        CancellationToken ct)
    {
        string? imdbId = null;
        int? tmdbId = null;
        string? parentImdbId = null;
        int? seasonNumber = null;
        int? episodeNumber = null;

        if (item.ProviderIds.TryGetValue(MetadataProvider.Imdb.ToString(), out var imdb))
        {
            imdbId = imdb;
        }

        if (item.ProviderIds.TryGetValue(MetadataProvider.Tmdb.ToString(), out var tmdb) &&
            int.TryParse(tmdb, out var tmdbInt))
        {
            tmdbId = tmdbInt;
        }

        if (item is Episode episode)
        {
            seasonNumber = episode.ParentIndexNumber;
            episodeNumber = episode.IndexNumber;

            var series = episode.Series;
            if (series != null && series.ProviderIds.TryGetValue(MetadataProvider.Imdb.ToString(), out var seriesImdb))
            {
                parentImdbId = seriesImdb;
            }
        }

        var results = await osClient.SearchJapaneseSubtitlesAsync(
            imdbId, tmdbId, item.Name, item.ProductionYear,
            parentImdbId, seasonNumber, episodeNumber, ct).ConfigureAwait(false);

        if (results.Count == 0)
        {
            return null;
        }

        var best = results[0];
        var content = await osClient.DownloadSubtitleAsync(best.FileId, ct).ConfigureAwait(false);

        if (string.IsNullOrEmpty(content))
        {
            return null;
        }

        return SrtParser.ParseString(content);
    }

    /// <summary>
    /// Reflows Japanese text to fit subtitle constraints.
    /// </summary>
    public static string ReflowJapaneseText(string text, int maxCharsPerLine, int maxLines)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var lines = text.Split('\n');
        if (lines.Length <= maxLines && lines.All(l => l.Length <= maxCharsPerLine))
        {
            return text;
        }

        var joined = string.Join("", lines.Select(l => l.Trim()));

        if (joined.Length <= maxCharsPerLine)
        {
            return joined;
        }

        int charsPerLine = (int)Math.Ceiling((double)joined.Length / maxLines);
        charsPerLine = Math.Min(charsPerLine, maxCharsPerLine);

        var result = new List<string>();
        for (int i = 0; i < joined.Length && result.Count < maxLines; i += charsPerLine)
        {
            int remaining = joined.Length - i;
            int take = Math.Min(charsPerLine, remaining);
            result.Add(joined.Substring(i, take));
        }

        int totalTaken = result.Sum(r => r.Length);
        if (totalTaken < joined.Length)
        {
            result[^1] += joined[totalTaken..];
        }

        return string.Join("\n", result);
    }
}

/// <summary>
/// Status information for an item's Japanese subtitle.
/// </summary>
public class GenerationStatusResult
{
    /// <summary>Gets or sets whether English SRT exists.</summary>
    public bool HasEnglishSrt { get; set; }

    /// <summary>Gets or sets whether Japanese SRT exists.</summary>
    public bool HasJapaneseSrt { get; set; }

    /// <summary>Gets or sets the Japanese SRT path.</summary>
    public string? JapaneseSrtPath { get; set; }

    /// <summary>Gets or sets when it was last generated.</summary>
    public DateTime? LastGeneratedAt { get; set; }

    /// <summary>Gets or sets the source used.</summary>
    public string? Source { get; set; }

    /// <summary>Gets or sets the coverage percentage.</summary>
    public double? CoveragePercent { get; set; }
}
