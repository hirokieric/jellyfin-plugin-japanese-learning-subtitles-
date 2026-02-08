using System;
using System.Net.Http;
using Jellyfin.Plugin.JapaneseLearningSubtitles.Configuration;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.JapaneseLearningSubtitles.Providers;

/// <summary>
/// Factory for creating the configured translation provider.
/// </summary>
public class TranslationProviderFactory
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILoggerFactory _loggerFactory;

    /// <summary>
    /// Initializes a new instance of the <see cref="TranslationProviderFactory"/> class.
    /// </summary>
    public TranslationProviderFactory(IHttpClientFactory httpClientFactory, ILoggerFactory loggerFactory)
    {
        _httpClientFactory = httpClientFactory;
        _loggerFactory = loggerFactory;
    }

    /// <summary>
    /// Creates a translation provider based on plugin configuration.
    /// </summary>
    public ITranslationProvider Create(PluginConfiguration config)
    {
        return config.TranslationProvider switch
        {
            TranslationProvider.OpenAI => new OpenAITranslationProvider(
                _httpClientFactory.CreateClient("OpenAI"),
                _loggerFactory.CreateLogger<OpenAITranslationProvider>(),
                config),

            TranslationProvider.DeepL => new DeepLTranslationProvider(
                _httpClientFactory.CreateClient("DeepL"),
                _loggerFactory.CreateLogger<DeepLTranslationProvider>(),
                config),

            TranslationProvider.CustomHTTP => new CustomHttpTranslationProvider(
                _httpClientFactory.CreateClient("CustomTranslation"),
                _loggerFactory.CreateLogger<CustomHttpTranslationProvider>(),
                config),

            _ => throw new InvalidOperationException($"Unknown translation provider: {config.TranslationProvider}")
        };
    }
}
