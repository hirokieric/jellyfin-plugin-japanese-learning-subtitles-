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
/// Translation provider using OpenAI API (default provider).
/// Translates English subtitle cues to natural Japanese.
/// </summary>
public class OpenAITranslationProvider : ITranslationProvider
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<OpenAITranslationProvider> _logger;
    private readonly PluginConfiguration _config;

    private const string ApiUrl = "https://api.openai.com/v1/chat/completions";
    private const int BatchSize = 20; // Cues per API call for efficiency
    private const int MaxRetries = 3;

    /// <inheritdoc />
    public string Name => "OpenAI";

    private static readonly string SystemPrompt = @"You are a professional subtitle translator specializing in English to Japanese translation for movies and TV shows.

Rules:
- Translate naturally for Japanese subtitle display
- Keep translations concise (suitable for subtitle readability)
- Maximum 42 Japanese characters per line, maximum 2 lines
- Preserve proper nouns (character names, brand names, place names) in katakana or as-is
- Use natural spoken Japanese tone (casual/conversational, not formal written style)
- For music markers (♪) or sound effects, keep them as-is or use appropriate Japanese equivalents
- If text is empty or just punctuation, return empty string
- Return ONLY the Japanese translation, nothing else

You will receive subtitles in batches. Each line is numbered. Return translations in the same order, one per line, with the same numbering.";

    /// <summary>
    /// Initializes a new instance of the <see cref="OpenAITranslationProvider"/> class.
    /// </summary>
    public OpenAITranslationProvider(
        HttpClient httpClient,
        ILogger<OpenAITranslationProvider> logger,
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

        // Process in smaller batches
        for (int batchStart = 0; batchStart < cues.Count; batchStart += BatchSize)
        {
            ct.ThrowIfCancellationRequested();

            var batch = cues.Skip(batchStart).Take(BatchSize).ToList();
            var batchResults = await TranslateSingleBatchAsync(batch, ct).ConfigureAwait(false);
            results.AddRange(batchResults);
        }

        return results;
    }

    private async Task<List<string>> TranslateSingleBatchAsync(List<ContextCue> batch, CancellationToken ct)
    {
        // Build the user prompt with context
        var sb = new StringBuilder();
        sb.AppendLine("Translate the following English subtitle lines to Japanese. Return ONLY the numbered translations.");
        sb.AppendLine();

        for (int i = 0; i < batch.Count; i++)
        {
            var cue = batch[i];

            // Provide context as comments
            if (!string.IsNullOrWhiteSpace(cue.PreviousText))
            {
                sb.AppendLine($"  [context-before: {cue.PreviousText}]");
            }

            sb.AppendLine($"{i + 1}. {cue.CurrentText}");

            if (!string.IsNullOrWhiteSpace(cue.NextText))
            {
                sb.AppendLine($"  [context-after: {cue.NextText}]");
            }
        }

        var requestBody = new
        {
            model = _config.OpenAIModel,
            messages = new object[]
            {
                new { role = "system", content = SystemPrompt },
                new { role = "user", content = sb.ToString() }
            },
            temperature = 0.3,
            max_tokens = batch.Count * 100 // Rough estimate
        };

        var json = JsonSerializer.Serialize(requestBody);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        for (int attempt = 0; attempt < MaxRetries; attempt++)
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, ApiUrl);
                request.Content = new StringContent(json, Encoding.UTF8, "application/json");
                request.Headers.Add("Authorization", $"Bearer {_config.OpenAIApiKey}");

                var response = await _httpClient.SendAsync(request, ct).ConfigureAwait(false);

                if (!response.IsSuccessStatusCode)
                {
                    var errorBody = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                    _logger.LogWarning(
                        "OpenAI API error (attempt {Attempt}): {Status} {Body}",
                        attempt + 1, response.StatusCode, errorBody);

                    if (attempt < MaxRetries - 1)
                    {
                        await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, attempt + 1)), ct).ConfigureAwait(false);
                        continue;
                    }

                    // Return empty translations on final failure
                    return Enumerable.Repeat(string.Empty, batch.Count).ToList();
                }

                var responseJson = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                var apiResponse = JsonSerializer.Deserialize<ChatCompletionResponse>(responseJson);

                var responseText = apiResponse?.Choices?.FirstOrDefault()?.Message?.Content ?? string.Empty;
                return ParseNumberedResponse(responseText, batch.Count);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "OpenAI translation error (attempt {Attempt})", attempt + 1);
                if (attempt < MaxRetries - 1)
                {
                    await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, attempt + 1)), ct).ConfigureAwait(false);
                }
            }
        }

        return Enumerable.Repeat(string.Empty, batch.Count).ToList();
    }

    /// <summary>
    /// Parses numbered response lines (e.g., "1. 日本語テキスト") into an ordered list.
    /// </summary>
    private List<string> ParseNumberedResponse(string responseText, int expectedCount)
    {
        var results = new string[expectedCount];
        for (int i = 0; i < expectedCount; i++)
        {
            results[i] = string.Empty;
        }

        var lines = responseText.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (string.IsNullOrWhiteSpace(trimmed)) continue;

            // Try to parse "N. text" or "N) text" format
            int dotIndex = trimmed.IndexOf('.');
            int parenIndex = trimmed.IndexOf(')');
            int separatorIndex = -1;

            if (dotIndex > 0 && dotIndex < 5) separatorIndex = dotIndex;
            else if (parenIndex > 0 && parenIndex < 5) separatorIndex = parenIndex;

            if (separatorIndex > 0 &&
                int.TryParse(trimmed[..separatorIndex].Trim(), out int num) &&
                num >= 1 && num <= expectedCount)
            {
                results[num - 1] = trimmed[(separatorIndex + 1)..].Trim();
            }
        }

        return results.ToList();
    }

    /// <inheritdoc />
    public async Task<bool> ValidateAsync(CancellationToken ct)
    {
        if (string.IsNullOrEmpty(_config.OpenAIApiKey))
        {
            _logger.LogWarning("OpenAI API key is not configured");
            return false;
        }

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, "https://api.openai.com/v1/models");
            request.Headers.Add("Authorization", $"Bearer {_config.OpenAIApiKey}");

            var response = await _httpClient.SendAsync(request, ct).ConfigureAwait(false);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "OpenAI validation failed");
            return false;
        }
    }

    // --- Response models ---

    private class ChatCompletionResponse
    {
        [JsonPropertyName("choices")]
        public List<Choice>? Choices { get; set; }
    }

    private class Choice
    {
        [JsonPropertyName("message")]
        public Message? Message { get; set; }
    }

    private class Message
    {
        [JsonPropertyName("content")]
        public string? Content { get; set; }
    }
}
