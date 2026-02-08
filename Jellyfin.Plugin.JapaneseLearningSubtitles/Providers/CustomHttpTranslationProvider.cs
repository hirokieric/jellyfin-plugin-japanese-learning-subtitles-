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
/// Translation provider using a custom HTTP endpoint.
/// Expects POST with JSON body: {"texts": [...], "source": "en", "target": "ja"}
/// Returns JSON body: {"translations": [...]}
/// </summary>
public class CustomHttpTranslationProvider : ITranslationProvider
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<CustomHttpTranslationProvider> _logger;
    private readonly PluginConfiguration _config;

    private const int BatchSize = 50;
    private const int MaxRetries = 3;

    /// <inheritdoc />
    public string Name => "CustomHTTP";

    /// <summary>
    /// Initializes a new instance of the <see cref="CustomHttpTranslationProvider"/> class.
    /// </summary>
    public CustomHttpTranslationProvider(
        HttpClient httpClient,
        ILogger<CustomHttpTranslationProvider> logger,
        PluginConfiguration config)
    {
        _httpClient = httpClient;
        _logger = logger;
        _config = config;
    }

    /// <inheritdoc />
    public async Task<List<string>> TranslateBatchAsync(List<ContextCue> cues, CancellationToken ct)
    {
        var results = new List<string>(cues.Count);

        for (int batchStart = 0; batchStart < cues.Count; batchStart += BatchSize)
        {
            ct.ThrowIfCancellationRequested();

            var batch = cues.Skip(batchStart).Take(BatchSize).ToList();
            var texts = batch.Select(c => c.CurrentText).ToList();

            var requestBody = new
            {
                texts,
                source = "en",
                target = "ja"
            };

            var json = JsonSerializer.Serialize(requestBody);

            for (int attempt = 0; attempt < MaxRetries; attempt++)
            {
                try
                {
                    var content = new StringContent(json, Encoding.UTF8, "application/json");
                    var response = await _httpClient.PostAsync(_config.CustomTranslationEndpoint, content, ct)
                        .ConfigureAwait(false);

                    if (!response.IsSuccessStatusCode)
                    {
                        _logger.LogWarning("Custom translation endpoint error: {Status}", response.StatusCode);
                        if (attempt < MaxRetries - 1)
                        {
                            await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, attempt + 1)), ct)
                                .ConfigureAwait(false);
                            continue;
                        }

                        results.AddRange(Enumerable.Repeat(string.Empty, texts.Count));
                        break;
                    }

                    var responseJson = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                    var result = JsonSerializer.Deserialize<CustomTranslationResponse>(responseJson);

                    if (result?.Translations != null && result.Translations.Count == texts.Count)
                    {
                        results.AddRange(result.Translations);
                    }
                    else
                    {
                        _logger.LogWarning("Custom endpoint returned mismatched count");
                        results.AddRange(Enumerable.Repeat(string.Empty, texts.Count));
                    }

                    break;
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Custom translation error (attempt {Attempt})", attempt + 1);
                    if (attempt == MaxRetries - 1)
                    {
                        results.AddRange(Enumerable.Repeat(string.Empty, texts.Count));
                    }
                    else
                    {
                        await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, attempt + 1)), ct)
                            .ConfigureAwait(false);
                    }
                }
            }
        }

        return results;
    }

    /// <inheritdoc />
    public async Task<bool> ValidateAsync(CancellationToken ct)
    {
        if (string.IsNullOrEmpty(_config.CustomTranslationEndpoint))
        {
            return false;
        }

        try
        {
            var uri = new Uri(_config.CustomTranslationEndpoint);
            return uri.Scheme is "http" or "https";
        }
        catch
        {
            return false;
        }
    }

    private class CustomTranslationResponse
    {
        [JsonPropertyName("translations")]
        public List<string>? Translations { get; set; }
    }
}
