using System;
using System.IO;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.JapaneseLearningSubtitles.Services;

/// <summary>
/// Injects a &lt;script&gt; tag into Jellyfin's index.html to load the
/// item detail page button on every page navigation.
/// This is necessary because GetPages() only serves plugin config pages,
/// not inject scripts into the main Jellyfin web client.
/// </summary>
public class ClientScriptInjector
{
    private readonly ILogger<ClientScriptInjector> _logger;

    /// <summary>
    /// The marker comment used to identify our injected script block.
    /// </summary>
    private const string MarkerComment = "<!-- JapaneseLearningSubtitles Plugin -->";

    /// <summary>
    /// The script tag that loads the client-side button JS from our API endpoint.
    /// </summary>
    private const string ScriptTag =
        MarkerComment + "\n"
        + "<script src=\"/JapaneseLearningSubtitles/ClientScript\" defer></script>\n"
        + "<!-- /JapaneseLearningSubtitles Plugin -->";

    /// <summary>
    /// Initializes a new instance of the <see cref="ClientScriptInjector"/> class.
    /// </summary>
    public ClientScriptInjector(ILogger<ClientScriptInjector> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Injects the script tag into the Jellyfin web client's index.html.
    /// </summary>
    /// <param name="webPath">Path to the Jellyfin web client directory.</param>
    public void Inject(string webPath)
    {
        var indexPath = Path.Combine(webPath, "index.html");

        if (!File.Exists(indexPath))
        {
            _logger.LogWarning("index.html not found at {Path}, cannot inject client script", indexPath);
            return;
        }

        try
        {
            var html = File.ReadAllText(indexPath);

            // Already injected?
            if (html.Contains(MarkerComment))
            {
                _logger.LogDebug("Client script already injected into index.html");
                return;
            }

            // Insert before </body>
            var closingBodyIndex = html.LastIndexOf("</body>", StringComparison.OrdinalIgnoreCase);
            if (closingBodyIndex < 0)
            {
                _logger.LogWarning("Could not find </body> in index.html, cannot inject");
                return;
            }

            var modified = html.Insert(closingBodyIndex, ScriptTag + "\n");
            File.WriteAllText(indexPath, modified);

            _logger.LogInformation("Successfully injected client script into index.html");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to inject client script into index.html");
        }
    }

    /// <summary>
    /// Removes the injected script tag from index.html (cleanup on disable/uninstall).
    /// </summary>
    /// <param name="webPath">Path to the Jellyfin web client directory.</param>
    public void Remove(string webPath)
    {
        var indexPath = Path.Combine(webPath, "index.html");

        if (!File.Exists(indexPath))
        {
            return;
        }

        try
        {
            var html = File.ReadAllText(indexPath);

            if (!html.Contains(MarkerComment))
            {
                return; // Nothing to remove
            }

            // Remove the injected block (marker comment + script + closing marker + surrounding newlines)
            var pattern = @"\s*" + Regex.Escape(MarkerComment) + @".*?" + Regex.Escape("<!-- /JapaneseLearningSubtitles Plugin -->") + @"\s*";
            var cleaned = Regex.Replace(html, pattern, "\n", RegexOptions.Singleline);
            File.WriteAllText(indexPath, cleaned);

            _logger.LogInformation("Removed client script from index.html");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to remove client script from index.html");
        }
    }
}
