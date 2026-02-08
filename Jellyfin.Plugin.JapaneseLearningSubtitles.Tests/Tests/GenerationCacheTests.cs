using Jellyfin.Plugin.JapaneseLearningSubtitles.Cache;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Jellyfin.Plugin.JapaneseLearningSubtitles.Tests;

public class GenerationCacheTests
{
    [Fact]
    public void SaveAndRetrieveRecord_RoundTrips()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "jls_cache_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            var cache = new GenerationCacheStore(tempDir, NullLogger<GenerationCacheStore>.Instance);
            var itemId = Guid.NewGuid();

            var record = new GenerationRecord
            {
                LastGeneratedAt = DateTime.UtcNow,
                SourceUsed = "OpenSubtitles",
                BaseEnglishSubtitleHash = "12345:67890",
                AlignmentVersion = "1.0",
                OutputPath = "/test/movie.ja.srt",
                CoveragePercent = 95.5
            };

            cache.SaveRecord(itemId, record);

            var retrieved = cache.GetRecord(itemId);
            Assert.NotNull(retrieved);
            Assert.Equal("OpenSubtitles", retrieved.SourceUsed);
            Assert.Equal("12345:67890", retrieved.BaseEnglishSubtitleHash);
            Assert.Equal(95.5, retrieved.CoveragePercent);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void GetRecord_NotFound_ReturnsNull()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "jls_cache_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            var cache = new GenerationCacheStore(tempDir, NullLogger<GenerationCacheStore>.Instance);
            Assert.Null(cache.GetRecord(Guid.NewGuid()));
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void ShouldSkip_NoRecord_ReturnsFalse()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "jls_cache_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            var cache = new GenerationCacheStore(tempDir, NullLogger<GenerationCacheStore>.Instance);
            Assert.False(cache.ShouldSkip(Guid.NewGuid(), "/nonexistent.srt", false));
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void ShouldSkip_OverwriteTrue_AlwaysFalse()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "jls_cache_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            var cache = new GenerationCacheStore(tempDir, NullLogger<GenerationCacheStore>.Instance);
            var itemId = Guid.NewGuid();

            cache.SaveRecord(itemId, new GenerationRecord
            {
                LastGeneratedAt = DateTime.UtcNow,
                BaseEnglishSubtitleHash = "test",
                OutputPath = "/test.ja.srt"
            });

            Assert.False(cache.ShouldSkip(itemId, "/test.srt", overwrite: true));
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void RemoveRecord_RemovesSuccessfully()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "jls_cache_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            var cache = new GenerationCacheStore(tempDir, NullLogger<GenerationCacheStore>.Instance);
            var itemId = Guid.NewGuid();

            cache.SaveRecord(itemId, new GenerationRecord { SourceUsed = "Test" });
            Assert.NotNull(cache.GetRecord(itemId));

            cache.RemoveRecord(itemId);
            Assert.Null(cache.GetRecord(itemId));
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void ComputeFileHash_NonExistent_ReturnsEmpty()
    {
        Assert.Equal(string.Empty, GenerationCacheStore.ComputeFileHash("/nonexistent/file.srt"));
    }

    [Fact]
    public void ComputeFileHash_ExistingFile_ReturnsNonEmpty()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tempFile, "test content");
            var hash = GenerationCacheStore.ComputeFileHash(tempFile);
            Assert.False(string.IsNullOrEmpty(hash));
            Assert.Contains(":", hash); // Format: size:ticks
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public void Persistence_NewInstance_LoadsPreviousData()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "jls_cache_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            var itemId = Guid.NewGuid();

            // First instance: save a record
            var cache1 = new GenerationCacheStore(tempDir, NullLogger<GenerationCacheStore>.Instance);
            cache1.SaveRecord(itemId, new GenerationRecord { SourceUsed = "Persisted" });

            // Second instance: should load the saved record
            var cache2 = new GenerationCacheStore(tempDir, NullLogger<GenerationCacheStore>.Instance);
            var record = cache2.GetRecord(itemId);

            Assert.NotNull(record);
            Assert.Equal("Persisted", record.SourceUsed);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }
}
