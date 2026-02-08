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
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using Jellyfin.Data.Enums;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.JapaneseLearningSubtitles.ScheduledTasks;

/// <summary>
/// Scheduled task that scans the library and generates Japanese SRT subtitles
/// aligned to existing English subtitle timings.
/// </summary>
public class GenerateJapaneseLearningSubtitlesTask : IScheduledTask
{
    private readonly ILibraryManager _libraryManager;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<GenerateJapaneseLearningSubtitlesTask> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="GenerateJapaneseLearningSubtitlesTask"/> class.
    /// </summary>
    public GenerateJapaneseLearningSubtitlesTask(
        ILibraryManager libraryManager,
        IHttpClientFactory httpClientFactory,
        ILoggerFactory loggerFactory,
        ILogger<GenerateJapaneseLearningSubtitlesTask> logger)
    {
        _libraryManager = libraryManager;
        _httpClientFactory = httpClientFactory;
        _loggerFactory = loggerFactory;
        _logger = logger;
    }

    /// <inheritdoc />
    public string Name => "Generate Japanese Learning Subtitles";

    /// <inheritdoc />
    public string Key => "JapaneseLearningSubtitlesGeneration";

    /// <inheritdoc />
    public string Description =>
        "Scans libraries for videos with English subtitles and generates aligned Japanese SRT subtitles for dual-subtitle English learning.";

    /// <inheritdoc />
    public string Category => Plugin.PluginName;

    /// <inheritdoc />
    public bool IsHidden => false;

    /// <inheritdoc />
    public bool IsEnabled => true;

    /// <inheritdoc />
    public bool IsLogged => true;

    /// <inheritdoc />
    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
    {
        // Default: run weekly on Sunday at 2 AM
        return new[]
        {
            new TaskTriggerInfo
            {
                Type = TaskTriggerInfo.TriggerWeekly,
                DayOfWeek = DayOfWeek.Sunday,
                TimeOfDayTicks = TimeSpan.FromHours(2).Ticks
            }
        };
    }

    /// <inheritdoc />
    public async Task ExecuteAsync(IProgress<double> progress, CancellationToken ct)
    {
        var config = Plugin.Instance?.Configuration;
        if (config == null)
        {
            _logger.LogError("Plugin configuration is null, aborting");
            return;
        }

        _logger.LogInformation("=== Starting Japanese Learning Subtitles Generation ===");

        // Initialize components
        var enLocator = new EnglishSubtitleLocator(
            _loggerFactory.CreateLogger<EnglishSubtitleLocator>());

        var cacheStore = new GenerationCacheStore(
            Plugin.Instance!.DataFolderPath,
            _loggerFactory.CreateLogger<GenerationCacheStore>());

        var translationFactory = new TranslationProviderFactory(_httpClientFactory, _loggerFactory);
        var translator = translationFactory.Create(config);

        var aligner = new SubtitleAligner(
            _loggerFactory.CreateLogger<SubtitleAligner>(),
            config.AlignmentConfidenceThreshold);

        // Initialize OpenSubtitles client
        OpenSubtitlesClient? osClient = null;
        bool osAvailable = false;

        if (!string.IsNullOrEmpty(config.OpenSubtitlesApiKey))
        {
            osClient = new OpenSubtitlesClient(
                _httpClientFactory.CreateClient("OpenSubtitles"),
                _loggerFactory.CreateLogger<OpenSubtitlesClient>());

            if (!string.IsNullOrEmpty(config.OpenSubtitlesUsername) &&
                !string.IsNullOrEmpty(config.OpenSubtitlesPassword))
            {
                osAvailable = await osClient.LoginAsync(
                    config.OpenSubtitlesUsername,
                    config.OpenSubtitlesPassword,
                    config.OpenSubtitlesApiKey,
                    ct).ConfigureAwait(false);

                if (!osAvailable)
                {
                    _logger.LogWarning("OpenSubtitles login failed, will rely on translation only");
                }
            }
        }
        else
        {
            _logger.LogInformation("OpenSubtitles not configured, will use translation for all items");
        }

        // Enumerate target video items
        var items = GetTargetItems(config);
        _logger.LogInformation("Found {Count} video items to process", items.Count);

        if (items.Count == 0)
        {
            progress.Report(100);
            return;
        }

        // Process items with concurrency control
        var semaphore = new SemaphoreSlim(config.MaxParallel);
        var stats = new ProcessingStats();
        int completedCount = 0;

        var tasks = items.Select(async item =>
        {
            await semaphore.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                await ProcessItemAsync(
                    item, config, enLocator, cacheStore, osClient, osAvailable,
                    aligner, translator, stats, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing item {Name} ({Id})", item.Name, item.Id);
                Interlocked.Increment(ref stats.Failures);
            }
            finally
            {
                semaphore.Release();
                var completed = Interlocked.Increment(ref completedCount);
                progress.Report(completed * 100.0 / items.Count);
            }
        });

        await Task.WhenAll(tasks).ConfigureAwait(false);

        // Log summary
        _logger.LogInformation(
            "=== Generation Complete === Processed: {Processed}, Skipped: {Skipped}, Generated: {Generated} (OpenSubs: {OpenSubs}, Translated: {Translated}), Failures: {Failures}",
            stats.Processed, stats.Skipped, stats.Generated, stats.OpenSubsUsed, stats.Translated, stats.Failures);

        progress.Report(100);
    }

    /// <summary>
    /// Processes a single media item through the full pipeline.
    /// </summary>
    private async Task ProcessItemAsync(
        BaseItem item,
        PluginConfiguration config,
        EnglishSubtitleLocator enLocator,
        GenerationCacheStore cacheStore,
        OpenSubtitlesClient? osClient,
        bool osAvailable,
        SubtitleAligner aligner,
        ITranslationProvider translator,
        ProcessingStats stats,
        CancellationToken ct)
    {
        Interlocked.Increment(ref stats.Processed);

        var mediaPath = item.Path;
        if (string.IsNullOrEmpty(mediaPath))
        {
            _logger.LogDebug("Item {Name} has no file path, skipping", item.Name);
            Interlocked.Increment(ref stats.Skipped);
            return;
        }

        // Check if JA SRT already exists
        if (!config.OverwriteExisting && enLocator.JapaneseSrtExists(mediaPath))
        {
            _logger.LogDebug("Japanese SRT already exists for {Name}, skipping", item.Name);
            Interlocked.Increment(ref stats.Skipped);
            return;
        }

        // Find English SRT
        var enSrtPath = enLocator.FindEnglishSrt(mediaPath);
        if (string.IsNullOrEmpty(enSrtPath))
        {
            _logger.LogWarning("No English SRT found for {Name} ({Path}), skipping", item.Name, mediaPath);
            Interlocked.Increment(ref stats.Skipped);
            return;
        }

        // Check cache
        if (cacheStore.ShouldSkip(item.Id, enSrtPath, config.OverwriteExisting))
        {
            _logger.LogDebug("Cache hit for {Name}, skipping", item.Name);
            Interlocked.Increment(ref stats.Skipped);
            return;
        }

        _logger.LogInformation("Processing: {Name} | EN SRT: {EnPath}", item.Name, enSrtPath);

        // Parse English SRT
        var enCues = await SrtParser.ParseFileAsync(enSrtPath).ConfigureAwait(false);
        if (enCues.Count == 0)
        {
            _logger.LogWarning("English SRT is empty for {Name}, skipping", item.Name);
            Interlocked.Increment(ref stats.Skipped);
            return;
        }

        // Try OpenSubtitles first
        List<SubtitleCue>? jpCues = null;
        string sourceUsed = "Translated";
        bool usedOpenSubs = false;

        if (osAvailable && osClient != null)
        {
            jpCues = await TryFetchOpenSubtitlesJapanese(
                item, osClient, ct).ConfigureAwait(false);

            if (jpCues != null && jpCues.Count > 0)
            {
                usedOpenSubs = true;
                sourceUsed = "OpenSubtitles";
                _logger.LogInformation("Found {Count} JP cues from OpenSubtitles for {Name}", jpCues.Count, item.Name);
            }
        }

        // Build result cues
        List<SubtitleCue> resultCues;

        if (usedOpenSubs && jpCues != null && jpCues.Count > 0)
        {
            // Align JP to EN timings
            var alignmentResult = aligner.Align(enCues, jpCues);

            // Translate cues with low confidence
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
                _logger.LogInformation("Translating {Count} low-confidence cues for {Name}",
                    needsTranslation.Count, item.Name);

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
            // Full translation path
            _logger.LogInformation("No JP from OpenSubtitles, translating all {Count} cues for {Name}",
                enCues.Count, item.Name);

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

            Interlocked.Increment(ref stats.Translated);
        }

        // Write output
        var outputPath = EnglishSubtitleLocator.GetJapaneseSrtPath(mediaPath);
        await SrtWriter.WriteFileAsync(outputPath, resultCues).ConfigureAwait(false);

        _logger.LogInformation("Saved: {Path} ({CueCount} cues)", outputPath, resultCues.Count);

        // Update cache
        cacheStore.SaveRecord(item.Id, new GenerationRecord
        {
            LastGeneratedAt = DateTime.UtcNow,
            SourceUsed = sourceUsed,
            BaseEnglishSubtitleHash = GenerationCacheStore.ComputeFileHash(enSrtPath),
            AlignmentVersion = "1.0",
            OutputPath = outputPath,
            CoveragePercent = resultCues.Count(c => !string.IsNullOrWhiteSpace(c.Text)) * 100.0 / resultCues.Count
        });

        Interlocked.Increment(ref stats.Generated);
        if (usedOpenSubs) Interlocked.Increment(ref stats.OpenSubsUsed);
    }

    /// <summary>
    /// Tries to fetch Japanese subtitles from OpenSubtitles for the given item.
    /// </summary>
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

        // Extract identifiers from Jellyfin metadata
        if (item.ProviderIds.TryGetValue(MetadataProvider.Imdb.ToString(), out var imdb))
        {
            imdbId = imdb;
        }

        if (item.ProviderIds.TryGetValue(MetadataProvider.Tmdb.ToString(), out var tmdb) &&
            int.TryParse(tmdb, out var tmdbInt))
        {
            tmdbId = tmdbInt;
        }

        // For episodes, get parent series identifiers
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

        // Download the top result (highest download count)
        var best = results[0];
        var content = await osClient.DownloadSubtitleAsync(best.FileId, ct).ConfigureAwait(false);

        if (string.IsNullOrEmpty(content))
        {
            return null;
        }

        return SrtParser.ParseString(content);
    }

    /// <summary>
    /// Gets all target video items based on configuration.
    /// </summary>
    private List<BaseItem> GetTargetItems(PluginConfiguration config)
    {
        var query = new InternalItemsQuery
        {
            IsVirtualItem = false,
            Recursive = true,
            MediaTypes = new[] { MediaType.Video }
        };

        var allItems = _libraryManager.GetItemList(query);

        return allItems.Where(item =>
        {
            // Filter by scan scope
            return config.ScanScope switch
            {
                ScanScope.MoviesOnly => item is Movie,
                ScanScope.SeriesOnly => item is Episode,
                _ => item is Movie or Episode
            };
        }).ToList();
    }

    /// <summary>
    /// Reflows Japanese text to fit subtitle constraints.
    /// </summary>
    private static string ReflowJapaneseText(string text, int maxCharsPerLine, int maxLines)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        // If already within limits, return as-is
        var lines = text.Split('\n');
        if (lines.Length <= maxLines && lines.All(l => l.Length <= maxCharsPerLine))
        {
            return text;
        }

        // Join all text and re-break
        var joined = string.Join("", lines.Select(l => l.Trim()));

        if (joined.Length <= maxCharsPerLine)
        {
            return joined;
        }

        // Split into maxLines, distributing characters evenly
        int charsPerLine = (int)Math.Ceiling((double)joined.Length / maxLines);
        charsPerLine = Math.Min(charsPerLine, maxCharsPerLine);

        var result = new List<string>();
        for (int i = 0; i < joined.Length && result.Count < maxLines; i += charsPerLine)
        {
            int remaining = joined.Length - i;
            int take = Math.Min(charsPerLine, remaining);
            result.Add(joined.Substring(i, take));
        }

        // If there's leftover text, append to last line
        int totalTaken = result.Sum(r => r.Length);
        if (totalTaken < joined.Length)
        {
            result[^1] += joined[totalTaken..];
        }

        return string.Join("\n", result);
    }

    /// <summary>
    /// Thread-safe processing statistics.
    /// </summary>
    private class ProcessingStats
    {
        public int Processed;
        public int Skipped;
        public int Generated;
        public int OpenSubsUsed;
        public int Translated;
        public int Failures;
    }
}
