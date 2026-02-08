using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.JapaneseLearningSubtitles.Configuration;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.JapaneseLearningSubtitles.Providers;

/// <summary>
/// Translation provider using DeepL API.
/// </summary>
public class DeepLTranslationProvider : ITranslationProvider
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<DeepLTranslationProvider> _logger;
    private readonly PluginConfiguration _config;

    private const int BatchSize = 50; // DeepL supports up to 50 texts per request
    private const int MaxRetries = 3;

    /// <inheritdoc />
    public string Name => "DeepL";

    /// <summary>
    /// Initializes a new instance of the <see cref="DeepLTranslationProvider"/> class.
    /// </summary>
    public DeepLTranslationProvider(
        HttpClient httpClient,
        ILogger<DeepLTranslationProvider> logger,
        PluginConfiguration config)
    {
        _httpClient = httpClient;
        _logger = logger;
        _config = config;
    }

    private string BaseUrl => _config.DeepLApiKey.EndsWith(":fx", StringComparison.Ordinal)
        ? "https://api-free.deepl.com/v2"
        : "https://api.deepl.com/v2";

    /// <inheritdoc />
    public async Task<List<string>> TranslateBatchAsync(List<ContextCue> cues, CancellationToken ct)
    {
        var results = new List<string>(cues.Count);

        for (int batchStart = 0; batchStart < cues.Count; batchStart += BatchSize)
        {
            ct.ThrowIfCancellationRequested();

            var batch = cues.Skip(batchStart).Take(BatchSize).ToList();
            var texts = batch.Select(c => c.CurrentText).ToList();
            var batchResults = await TranslateTextsAsync(texts, ct).ConfigureAwait(false);
            results.AddRange(batchResults);
        }

        return results;
    }

    private async Task<List<string>> TranslateTextsAsync(List<string> texts, CancellationToken ct)
    {
        var formContent = new List<KeyValuePair<string, string>>
        {
            new("target_lang", "JA"),
            new("source_lang", "EN")
        };

        foreach (var text in texts)
        {
            formContent.Add(new("text", text));
        }

        for (int attempt = 0; attempt < MaxRetries; attempt++)
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, $"{BaseUrl}/translate");
                request.Headers.Add("Authorization", $"DeepL-Auth-Key {_config.DeepLApiKey}");
                request.Content = new FormUrlEncodedContent(formContent);

                var response = await _httpClient.SendAsync(request, ct).ConfigureAwait(false);

                if (!response.IsSuccessStatusCode)
                {
                    var errorBody = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                    _logger.LogWarning("DeepL API error (attempt {Attempt}): {Status} {Body}",
                        attempt + 1, response.StatusCode, errorBody);

                    if (attempt < MaxRetries - 1)
                    {
                        await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, attempt + 1)), ct).ConfigureAwait(false);
                        continue;
                    }

                    return Enumerable.Repeat(string.Empty, texts.Count).ToList();
                }

                var json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                var result = JsonSerializer.Deserialize<DeepLResponse>(json);

                if (result?.Translations != null)
                {
                    return result.Translations.Select(t => t.Text ?? string.Empty).ToList();
                }

                return Enumerable.Repeat(string.Empty, texts.Count).ToList();
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                _logger.LogError(ex, "DeepL translation error (attempt {Attempt})", attempt + 1);
                if (attempt < MaxRetries - 1)
                {
                    await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, attempt + 1)), ct).ConfigureAwait(false);
                }
            }
        }

        return Enumerable.Repeat(string.Empty, texts.Count).ToList();
    }

    /// <inheritdoc />
    public async Task<bool> ValidateAsync(CancellationToken ct)
    {
        if (string.IsNullOrEmpty(_config.DeepLApiKey))
        {
            return false;
        }

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"{BaseUrl}/usage");
            request.Headers.Add("Authorization", $"DeepL-Auth-Key {_config.DeepLApiKey}");

            var response = await _httpClient.SendAsync(request, ct).ConfigureAwait(false);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    private class DeepLResponse
    {
        [JsonPropertyName("translations")]
        public List<DeepLTranslation>? Translations { get; set; }
    }

    private class DeepLTranslation
    {
        [JsonPropertyName("text")]
        public string? Text { get; set; }
    }
}
