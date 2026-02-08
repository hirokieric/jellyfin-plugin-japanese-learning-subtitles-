using Jellyfin.Plugin.JapaneseLearningSubtitles.Providers;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Jellyfin.Plugin.JapaneseLearningSubtitles.Tests;

public class EnglishSubtitleLocatorTests
{
    private readonly EnglishSubtitleLocator _locator = new(
        NullLogger<EnglishSubtitleLocator>.Instance);

    [Fact]
    public void GetJapaneseSrtPath_Movie_CorrectNaming()
    {
        var result = EnglishSubtitleLocator.GetJapaneseSrtPath(
            "/movies/The Matrix (1999)/The Matrix (1999).mkv");

        Assert.Equal(
            "/movies/The Matrix (1999)/The Matrix (1999).ja.srt",
            result);
    }

    [Fact]
    public void GetJapaneseSrtPath_Episode_CorrectNaming()
    {
        var result = EnglishSubtitleLocator.GetJapaneseSrtPath(
            "/tv/Breaking Bad/Season 01/Breaking Bad - S01E01 - Pilot.mkv");

        Assert.Equal(
            "/tv/Breaking Bad/Season 01/Breaking Bad - S01E01 - Pilot.ja.srt",
            result);
    }

    [Fact]
    public void FindEnglishSrt_WithEnTagged_ReturnsIt()
    {
        // Arrange: create temp directory with test files
        var tempDir = Path.Combine(Path.GetTempPath(), "jls_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            var mediaPath = Path.Combine(tempDir, "Movie.mkv");
            File.WriteAllText(mediaPath, "dummy");
            File.WriteAllText(Path.Combine(tempDir, "Movie.en.srt"), "1\n00:00:01,000 --> 00:00:02,000\nHello\n");
            File.WriteAllText(Path.Combine(tempDir, "Movie.ja.srt"), "1\n00:00:01,000 --> 00:00:02,000\nこんにちは\n");

            var result = _locator.FindEnglishSrt(mediaPath);

            Assert.NotNull(result);
            Assert.Contains(".en.srt", result);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void FindEnglishSrt_DefaultSrt_ReturnsIt()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "jls_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            var mediaPath = Path.Combine(tempDir, "Movie.mkv");
            File.WriteAllText(mediaPath, "dummy");
            File.WriteAllText(Path.Combine(tempDir, "Movie.srt"), "1\n00:00:01,000 --> 00:00:02,000\nHello\n");

            var result = _locator.FindEnglishSrt(mediaPath);

            Assert.NotNull(result);
            Assert.EndsWith(".srt", result);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void FindEnglishSrt_NoMatch_ReturnsNull()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "jls_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            var mediaPath = Path.Combine(tempDir, "Movie.mkv");
            File.WriteAllText(mediaPath, "dummy");
            // Only non-English subtitle
            File.WriteAllText(Path.Combine(tempDir, "Movie.fr.srt"), "1\n00:00:01,000 --> 00:00:02,000\nBonjour\n");

            var result = _locator.FindEnglishSrt(mediaPath);

            Assert.Null(result);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void FindEnglishSrt_PrefersEnTagOverDefault()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "jls_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            var mediaPath = Path.Combine(tempDir, "Movie.mkv");
            File.WriteAllText(mediaPath, "dummy");
            File.WriteAllText(Path.Combine(tempDir, "Movie.srt"), "default");
            File.WriteAllText(Path.Combine(tempDir, "Movie.en.srt"), "english tagged");

            var result = _locator.FindEnglishSrt(mediaPath);

            Assert.NotNull(result);
            Assert.Contains(".en.srt", result);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void JapaneseSrtExists_WhenPresent_ReturnsTrue()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "jls_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            var mediaPath = Path.Combine(tempDir, "Movie.mkv");
            File.WriteAllText(mediaPath, "dummy");
            File.WriteAllText(Path.Combine(tempDir, "Movie.ja.srt"), "japanese");

            Assert.True(_locator.JapaneseSrtExists(mediaPath));
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void JapaneseSrtExists_WhenAbsent_ReturnsFalse()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "jls_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            var mediaPath = Path.Combine(tempDir, "Movie.mkv");
            File.WriteAllText(mediaPath, "dummy");

            Assert.False(_locator.JapaneseSrtExists(mediaPath));
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }
}
