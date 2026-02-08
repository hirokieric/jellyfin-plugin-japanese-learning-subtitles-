using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.JapaneseLearningSubtitles.Providers;

/// <summary>
/// Client for the OpenSubtitles REST API (v2006+).
/// Handles authentication, search, and download of subtitle files.
/// </summary>
public class OpenSubtitlesClient : IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<OpenSubtitlesClient> _logger;
    private string? _authToken;
    private DateTime _tokenExpiry = DateTime.MinValue;

    private const string BaseUrl = "https://api.opensubtitles.com/api/v1";
    private const int MaxRetries = 3;
    private static readonly TimeSpan InitialBackoff = TimeSpan.FromSeconds(2);

    /// <summary>
    /// Initializes a new instance of the <see cref="OpenSubtitlesClient"/> class.
    /// </summary>
    public OpenSubtitlesClient(HttpClient httpClient, ILogger<OpenSubtitlesClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    /// <summary>
    /// Authenticates with OpenSubtitles and obtains a session token.
    /// </summary>
    public async Task<bool> LoginAsync(string username, string password, string apiKey, CancellationToken ct)
    {
        if (!string.IsNullOrEmpty(_authToken) && DateTime.UtcNow < _tokenExpiry)
        {
            return true; // Token still valid
        }

        _httpClient.DefaultRequestHeaders.Clear();
        _httpClient.DefaultRequestHeaders.Add("Api-Key", apiKey);
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        _httpClient.DefaultRequestHeaders.Add("User-Agent", "JellyfinJapaneseLearningSubtitles v1.0");

        var loginPayload = new { username, password };
        var content = new StringContent(
            JsonSerializer.Serialize(loginPayload),
            Encoding.UTF8,
            "application/json");

        try
        {
            var response = await ExecuteWithRetryAsync(
                () => _httpClient.PostAsync($"{BaseUrl}/login", content, ct),
                ct).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                _logger.LogError("OpenSubtitles login failed: {Status} {Body}", response.StatusCode, errorBody);
                return false;
            }

            var json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            var loginResult = JsonSerializer.Deserialize<LoginResponse>(json);

            if (loginResult?.Token == null)
            {
                _logger.LogError("OpenSubtitles login returned no token");
                return false;
            }

            _authToken = loginResult.Token;
            _tokenExpiry = DateTime.UtcNow.AddHours(23); // Tokens typically last 24h
            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _authToken);

            _logger.LogInformation("OpenSubtitles login successful");
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "OpenSubtitles login exception");
            return false;
        }
    }

    /// <summary>
    /// Searches for Japanese subtitles for the given media identifiers.
    /// </summary>
    /// <returns>List of subtitle search results, or empty if none found.</returns>
    public async Task<List<SubtitleSearchResult>> SearchJapaneseSubtitlesAsync(
        string? imdbId,
        int? tmdbId,
        string? title,
        int? year,
        string? parentImdbId,
        int? seasonNumber,
        int? episodeNumber,
        CancellationToken ct)
    {
        var queryParams = new List<string>
        {
            "languages=ja",
            "order_by=download_count",
            "order_direction=desc"
        };

        // Prefer IMDb ID
        if (!string.IsNullOrEmpty(imdbId))
        {
            // Strip "tt" prefix if present for the API
            var cleanImdbId = imdbId.StartsWith("tt", StringComparison.OrdinalIgnoreCase)
                ? imdbId : $"tt{imdbId}";
            queryParams.Add($"imdb_id={Uri.EscapeDataString(cleanImdbId)}");
        }
        else if (tmdbId.HasValue)
        {
            queryParams.Add($"tmdb_id={tmdbId.Value}");
        }
        else if (!string.IsNullOrEmpty(title))
        {
            queryParams.Add($"query={Uri.EscapeDataString(title)}");
            if (year.HasValue)
            {
                queryParams.Add($"year={year.Value}");
            }
        }
        else
        {
            _logger.LogWarning("No identifiers available for OpenSubtitles search");
            return new List<SubtitleSearchResult>();
        }

        // For TV episodes
        if (!string.IsNullOrEmpty(parentImdbId))
        {
            var cleanParentId = parentImdbId.StartsWith("tt", StringComparison.OrdinalIgnoreCase)
                ? parentImdbId : $"tt{parentImdbId}";
            queryParams.Add($"parent_imdb_id={Uri.EscapeDataString(cleanParentId)}");
        }

        if (seasonNumber.HasValue)
        {
            queryParams.Add($"season_number={seasonNumber.Value}");
        }

        if (episodeNumber.HasValue)
        {
            queryParams.Add($"episode_number={episodeNumber.Value}");
        }

        var url = $"{BaseUrl}/subtitles?{string.Join("&", queryParams)}";

        try
        {
            var response = await ExecuteWithRetryAsync(
                () => _httpClient.GetAsync(url, ct),
                ct).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                _logger.LogWarning("OpenSubtitles search failed: {Status} {Body}", response.StatusCode, errorBody);
                return new List<SubtitleSearchResult>();
            }

            var json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            var searchResponse = JsonSerializer.Deserialize<SearchResponse>(json);

            if (searchResponse?.Data == null || searchResponse.Data.Count == 0)
            {
                _logger.LogDebug("No Japanese subtitles found on OpenSubtitles");
                return new List<SubtitleSearchResult>();
            }

            var results = searchResponse.Data
                .Where(d => d.Attributes?.Files != null && d.Attributes.Files.Count > 0)
                .Select(d =>
                {
                    var attrs = d.Attributes!;
                    var file = attrs.Files![0]!;
                    return new SubtitleSearchResult
                    {
                        FileId = file.FileId,
                        FileName = file.FileName ?? "unknown.srt",
                        DownloadCount = attrs.DownloadCount,
                        Format = attrs.Format ?? "srt"
                    };
                })
                .ToList();

            _logger.LogInformation("Found {Count} Japanese subtitle results on OpenSubtitles", results.Count);
            return results;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "OpenSubtitles search exception");
            return new List<SubtitleSearchResult>();
        }
    }

    /// <summary>
    /// Downloads a subtitle file by its file ID.
    /// </summary>
    /// <returns>The subtitle content as a string, or null on failure.</returns>
    public async Task<string?> DownloadSubtitleAsync(int fileId, CancellationToken ct)
    {
        var downloadPayload = new { file_id = fileId };
        var content = new StringContent(
            JsonSerializer.Serialize(downloadPayload),
            Encoding.UTF8,
            "application/json");

        try
        {
            // Step 1: Get download link
            var response = await ExecuteWithRetryAsync(
                () => _httpClient.PostAsync($"{BaseUrl}/download", content, ct),
                ct).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                _logger.LogWarning("OpenSubtitles download request failed: {Status} {Body}", response.StatusCode, errorBody);
                return null;
            }

            var json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            var downloadResponse = JsonSerializer.Deserialize<DownloadResponse>(json);

            if (string.IsNullOrEmpty(downloadResponse?.Link))
            {
                _logger.LogWarning("OpenSubtitles download returned no link");
                return null;
            }

            // Step 2: Download the actual file
            var fileResponse = await ExecuteWithRetryAsync(
                () => _httpClient.GetAsync(downloadResponse.Link, ct),
                ct).ConfigureAwait(false);

            if (!fileResponse.IsSuccessStatusCode)
            {
                _logger.LogWarning("Failed to download subtitle file from {Link}", downloadResponse.Link);
                return null;
            }

            var subtitleContent = await fileResponse.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

            _logger.LogInformation("Downloaded subtitle file ({Length} chars)", subtitleContent.Length);
            return subtitleContent;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "OpenSubtitles download exception");
            return null;
        }
    }

    /// <summary>
    /// Executes an HTTP request with exponential backoff retry.
    /// </summary>
    private async Task<HttpResponseMessage> ExecuteWithRetryAsync(
        Func<Task<HttpResponseMessage>> requestFactory,
        CancellationToken ct)
    {
        var backoff = InitialBackoff;

        for (int attempt = 0; attempt < MaxRetries; attempt++)
        {
            var response = await requestFactory().ConfigureAwait(false);

            if (response.StatusCode == HttpStatusCode.TooManyRequests ||
                response.StatusCode >= HttpStatusCode.InternalServerError)
            {
                if (attempt < MaxRetries - 1)
                {
                    _logger.LogWarning(
                        "OpenSubtitles returned {Status}, retrying in {Delay}s (attempt {Attempt}/{Max})",
                        response.StatusCode, backoff.TotalSeconds, attempt + 1, MaxRetries);

                    await Task.Delay(backoff, ct).ConfigureAwait(false);
                    backoff *= 2; // Exponential backoff
                    continue;
                }
            }

            return response;
        }

        // Should not reach here, but just in case
        return await requestFactory().ConfigureAwait(false);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _httpClient.Dispose();
    }

    // --- JSON response models ---

    private class LoginResponse
    {
        [JsonPropertyName("token")]
        public string? Token { get; set; }
    }

    private class SearchResponse
    {
        [JsonPropertyName("data")]
        public List<SearchDataItem>? Data { get; set; }
    }

    private class SearchDataItem
    {
        [JsonPropertyName("attributes")]
        public SearchAttributes? Attributes { get; set; }
    }

    private class SearchAttributes
    {
        [JsonPropertyName("download_count")]
        public int DownloadCount { get; set; }

        [JsonPropertyName("format")]
        public string? Format { get; set; }

        [JsonPropertyName("files")]
        public List<SearchFile>? Files { get; set; }
    }

    private class SearchFile
    {
        [JsonPropertyName("file_id")]
        public int FileId { get; set; }

        [JsonPropertyName("file_name")]
        public string? FileName { get; set; }
    }

    private class DownloadResponse
    {
        [JsonPropertyName("link")]
        public string? Link { get; set; }
    }
}

/// <summary>
/// Represents a subtitle search result from OpenSubtitles.
/// </summary>
public class SubtitleSearchResult
{
    /// <summary>Gets or sets the file ID for download.</summary>
    public int FileId { get; set; }

    /// <summary>Gets or sets the file name.</summary>
    public string FileName { get; set; } = string.Empty;

    /// <summary>Gets or sets the download count (popularity metric).</summary>
    public int DownloadCount { get; set; }

    /// <summary>Gets or sets the subtitle format.</summary>
    public string Format { get; set; } = "srt";
}
