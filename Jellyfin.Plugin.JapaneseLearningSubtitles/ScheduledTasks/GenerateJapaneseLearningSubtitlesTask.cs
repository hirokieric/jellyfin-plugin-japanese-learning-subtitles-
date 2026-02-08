using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.JapaneseLearningSubtitles.Configuration;
using Jellyfin.Plugin.JapaneseLearningSubtitles.Services;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using Jellyfin.Data.Enums;
using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.JapaneseLearningSubtitles.ScheduledTasks;

/// <summary>
/// Scheduled task that scans the library and generates Japanese SRT subtitles
/// aligned to existing English subtitle timings (batch mode).
/// Delegates per-item work to <see cref="SubtitleGenerationService"/>.
/// </summary>
public class GenerateJapaneseLearningSubtitlesTask : IScheduledTask
{
    private readonly ILibraryManager _libraryManager;
    private readonly SubtitleGenerationService _generationService;
    private readonly ILogger<GenerateJapaneseLearningSubtitlesTask> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="GenerateJapaneseLearningSubtitlesTask"/> class.
    /// </summary>
    public GenerateJapaneseLearningSubtitlesTask(
        ILibraryManager libraryManager,
        SubtitleGenerationService generationService,
        ILogger<GenerateJapaneseLearningSubtitlesTask> logger)
    {
        _libraryManager = libraryManager;
        _generationService = generationService;
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

        _logger.LogInformation("=== Starting Japanese Learning Subtitles Batch Generation ===");

        var items = GetTargetItems(config);
        _logger.LogInformation("Found {Count} video items to process", items.Count);

        if (items.Count == 0)
        {
            progress.Report(100);
            return;
        }

        var semaphore = new SemaphoreSlim(config.MaxParallel);
        int completedCount = 0;
        int generated = 0;
        int skipped = 0;
        int failures = 0;

        var tasks = items.Select(async item =>
        {
            await semaphore.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                var result = await _generationService.GenerateForItemAsync(
                    item, config.OverwriteExisting, ct).ConfigureAwait(false);

                if (result.Success)
                {
                    Interlocked.Increment(ref generated);
                }
                else
                {
                    Interlocked.Increment(ref skipped);
                    _logger.LogDebug("Skipped {Name}: {Message}", item.Name, result.Message);
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing item {Name} ({Id})", item.Name, item.Id);
                Interlocked.Increment(ref failures);
            }
            finally
            {
                semaphore.Release();
                var completed = Interlocked.Increment(ref completedCount);
                progress.Report(completed * 100.0 / items.Count);
            }
        });

        await Task.WhenAll(tasks).ConfigureAwait(false);

        _logger.LogInformation(
            "=== Batch Complete === Total: {Total}, Generated: {Generated}, Skipped: {Skipped}, Failures: {Failures}",
            items.Count, generated, skipped, failures);

        progress.Report(100);
    }

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
            return config.ScanScope switch
            {
                ScanScope.MoviesOnly => item is Movie,
                ScanScope.SeriesOnly => item is Episode,
                _ => item is Movie or Episode
            };
        }).ToList();
    }
}
