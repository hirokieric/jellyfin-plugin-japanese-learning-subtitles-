using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.JapaneseLearningSubtitles.Services;

/// <summary>
/// Injects the client-side script into Jellyfin's index.html when the server starts,
/// and removes it on shutdown. Uses <see cref="IHostedService"/> (IServerEntryPoint is removed in Jellyfin 10.9+).
/// </summary>
public class ClientScriptEntryPoint : IHostedService
{
    private readonly ClientScriptInjector _injector;
    private readonly ILogger<ClientScriptEntryPoint> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="ClientScriptEntryPoint"/> class.
    /// </summary>
    public ClientScriptEntryPoint(
        ClientScriptInjector injector,
        ILogger<ClientScriptEntryPoint> logger)
    {
        _injector = injector;
        _logger = logger;
    }

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        var webPath = Plugin.Instance?.WebPath;
        if (string.IsNullOrEmpty(webPath))
        {
            _logger.LogWarning("WebPath is not available, skipping client script injection");
            return Task.CompletedTask;
        }

        _injector.Inject(webPath);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken)
    {
        var webPath = Plugin.Instance?.WebPath;
        if (!string.IsNullOrEmpty(webPath))
        {
            _injector.Remove(webPath);
        }

        return Task.CompletedTask;
    }
}
