using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.JapaneseLearningSubtitles.Cache;

/// <summary>
/// Stores generation status per media item for idempotency.
/// Persisted as JSON in the plugin data folder.
/// </summary>
public class GenerationCacheStore
{
    private readonly string _cacheFilePath;
    private readonly ILogger<GenerationCacheStore> _logger;
    private Dictionary<string, GenerationRecord> _cache;
    private static readonly object _lock = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="GenerationCacheStore"/> class.
    /// </summary>
    public GenerationCacheStore(string pluginDataPath, ILogger<GenerationCacheStore> logger)
    {
        _logger = logger;
        _cacheFilePath = Path.Combine(pluginDataPath, "generation-cache.json");
        _cache = LoadCache();
    }

    /// <summary>
    /// Gets the generation record for a media item, or null if not found.
    /// </summary>
    public GenerationRecord? GetRecord(Guid itemId)
    {
        lock (_lock)
        {
            return _cache.TryGetValue(itemId.ToString(), out var record) ? record : null;
        }
    }

    /// <summary>
    /// Determines whether generation should be skipped for this item.
    /// Returns true if the item was already generated and the base EN subtitle hasn't changed.
    /// </summary>
    public bool ShouldSkip(Guid itemId, string enSubtitlePath, bool overwrite)
    {
        if (overwrite)
        {
            return false;
        }

        var record = GetRecord(itemId);
        if (record == null)
        {
            return false;
        }

        // Check if the base English subtitle has changed
        var currentHash = ComputeFileHash(enSubtitlePath);
        return record.BaseEnglishSubtitleHash == currentHash
               && !string.IsNullOrEmpty(record.OutputPath)
               && File.Exists(record.OutputPath);
    }

    /// <summary>
    /// Saves a generation record for a media item.
    /// </summary>
    public void SaveRecord(Guid itemId, GenerationRecord record)
    {
        lock (_lock)
        {
            _cache[itemId.ToString()] = record;
            PersistCache();
        }
    }

    /// <summary>
    /// Removes the generation record for a media item.
    /// </summary>
    public void RemoveRecord(Guid itemId)
    {
        lock (_lock)
        {
            _cache.Remove(itemId.ToString());
            PersistCache();
        }
    }

    /// <summary>
    /// Computes a simple hash for a file based on size and last write time.
    /// </summary>
    public static string ComputeFileHash(string filePath)
    {
        try
        {
            var info = new FileInfo(filePath);
            if (!info.Exists) return string.Empty;
            return $"{info.Length}:{info.LastWriteTimeUtc.Ticks}";
        }
        catch
        {
            return string.Empty;
        }
    }

    private Dictionary<string, GenerationRecord> LoadCache()
    {
        try
        {
            if (File.Exists(_cacheFilePath))
            {
                var json = File.ReadAllText(_cacheFilePath);
                return JsonSerializer.Deserialize<Dictionary<string, GenerationRecord>>(json)
                       ?? new Dictionary<string, GenerationRecord>();
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load generation cache, starting fresh");
        }

        return new Dictionary<string, GenerationRecord>();
    }

    private void PersistCache()
    {
        try
        {
            var directory = Path.GetDirectoryName(_cacheFilePath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var json = JsonSerializer.Serialize(_cache, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_cacheFilePath, json);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to persist generation cache");
        }
    }
}

/// <summary>
/// Record of a subtitle generation for a specific media item.
/// </summary>
public class GenerationRecord
{
    /// <summary>Gets or sets when the subtitle was last generated.</summary>
    [JsonPropertyName("lastGeneratedAt")]
    public DateTime LastGeneratedAt { get; set; }

    /// <summary>Gets or sets the source used (OpenSubtitles or Translated).</summary>
    [JsonPropertyName("sourceUsed")]
    public string SourceUsed { get; set; } = string.Empty;

    /// <summary>Gets or sets a hash of the base English subtitle for change detection.</summary>
    [JsonPropertyName("baseEnglishSubtitleHash")]
    public string BaseEnglishSubtitleHash { get; set; } = string.Empty;

    /// <summary>Gets or sets the alignment engine version.</summary>
    [JsonPropertyName("alignmentVersion")]
    public string AlignmentVersion { get; set; } = "1.0";

    /// <summary>Gets or sets the output file path.</summary>
    [JsonPropertyName("outputPath")]
    public string OutputPath { get; set; } = string.Empty;

    /// <summary>Gets or sets the coverage percentage achieved.</summary>
    [JsonPropertyName("coveragePercent")]
    public double CoveragePercent { get; set; }
}
