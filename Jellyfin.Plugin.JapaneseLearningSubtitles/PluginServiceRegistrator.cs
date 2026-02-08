using Jellyfin.Plugin.JapaneseLearningSubtitles.Providers;
using Jellyfin.Plugin.JapaneseLearningSubtitles.ScheduledTasks;
using Jellyfin.Plugin.JapaneseLearningSubtitles.Services;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Plugins;
using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.DependencyInjection;

namespace Jellyfin.Plugin.JapaneseLearningSubtitles;

/// <summary>
/// Registers plugin services with the DI container.
/// </summary>
public class PluginServiceRegistrator : IPluginServiceRegistrator
{
    /// <inheritdoc />
    public void RegisterServices(IServiceCollection serviceCollection, IServerApplicationHost applicationHost)
    {
        serviceCollection.AddHttpClient("OpenSubtitles");
        serviceCollection.AddHttpClient("OpenAI");
        serviceCollection.AddHttpClient("DeepL");
        serviceCollection.AddHttpClient("CustomTranslation");

        serviceCollection.AddSingleton<TranslationProviderFactory>();
        serviceCollection.AddSingleton<SubtitleGenerationService>();
        serviceCollection.AddSingleton<IScheduledTask, GenerateJapaneseLearningSubtitlesTask>();
    }
}
