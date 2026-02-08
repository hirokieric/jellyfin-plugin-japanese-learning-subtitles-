using System;
using System.ComponentModel.DataAnnotations;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.JapaneseLearningSubtitles.Services;
using MediaBrowser.Controller.Library;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.JapaneseLearningSubtitles.Api;

/// <summary>
/// REST API controller for on-demand Japanese subtitle generation.
/// </summary>
[ApiController]
[Route("JapaneseLearningSubtitles")]
public class JapaneseLearningSubtitlesController : ControllerBase
{
    private readonly ILibraryManager _libraryManager;
    private readonly SubtitleGenerationService _generationService;
    private readonly ILogger<JapaneseLearningSubtitlesController> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="JapaneseLearningSubtitlesController"/> class.
    /// </summary>
    public JapaneseLearningSubtitlesController(
        ILibraryManager libraryManager,
        SubtitleGenerationService generationService,
        ILogger<JapaneseLearningSubtitlesController> logger)
    {
        _libraryManager = libraryManager;
        _generationService = generationService;
        _logger = logger;
    }

    /// <summary>
    /// Serves the client-side JavaScript for the item detail button.
    /// No authentication required so the script tag in index.html can load it.
    /// </summary>
    [HttpGet("ClientScript")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult GetClientScript()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var resourceName = "Jellyfin.Plugin.JapaneseLearningSubtitles.Configuration.itemDetailButton.js";

        using var stream = assembly.GetManifestResourceStream(resourceName);
        if (stream == null)
        {
            return NotFound("Client script resource not found.");
        }

        using var reader = new StreamReader(stream);
        var script = reader.ReadToEnd();

        return Content(script, "application/javascript");
    }

    /// <summary>
    /// Generates Japanese subtitles for a specific item.
    /// </summary>
    /// <param name="itemId">The Jellyfin item ID.</param>
    /// <param name="overwrite">Whether to overwrite existing .ja.srt.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Generation result.</returns>
    [HttpPost("{itemId}/Generate")]
    [Authorize(Policy = "RequiresElevation")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<GenerateResponse>> Generate(
        [FromRoute, Required] Guid itemId,
        [FromQuery] bool overwrite = false,
        CancellationToken ct = default)
    {
        var item = _libraryManager.GetItemById(itemId);
        if (item == null)
        {
            return NotFound(new GenerateResponse
            {
                Success = false,
                Message = $"Item {itemId} not found."
            });
        }

        _logger.LogInformation("On-demand generation requested for: {Name} ({Id})", item.Name, itemId);

        try
        {
            var result = await _generationService.GenerateForItemAsync(item, overwrite, ct)
                .ConfigureAwait(false);

            return Ok(new GenerateResponse
            {
                Success = result.Success,
                Message = result.Message ?? (result.Success
                    ? $"Generated {result.CueCount} cues ({result.CoveragePercent:F1}% coverage)"
                    : "Generation failed."),
                Source = result.Source,
                OutputPath = result.OutputPath,
                CueCount = result.CueCount,
                CoveragePercent = result.CoveragePercent
            });
        }
        catch (OperationCanceledException)
        {
            return Ok(new GenerateResponse
            {
                Success = false,
                Message = "Generation was cancelled."
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating subtitles for {Name} ({Id})", item.Name, itemId);
            return StatusCode(500, new GenerateResponse
            {
                Success = false,
                Message = $"Internal error: {ex.Message}"
            });
        }
    }

    /// <summary>
    /// Gets the generation status for a specific item.
    /// </summary>
    /// <param name="itemId">The Jellyfin item ID.</param>
    /// <returns>Status information.</returns>
    [HttpGet("{itemId}/Status")]
    [Authorize(Policy = "RequiresElevation")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public ActionResult<StatusResponse> GetStatus(
        [FromRoute, Required] Guid itemId)
    {
        var item = _libraryManager.GetItemById(itemId);
        if (item == null)
        {
            return NotFound(new StatusResponse { Message = $"Item {itemId} not found." });
        }

        var status = _generationService.GetStatus(item);

        return Ok(new StatusResponse
        {
            HasEnglishSrt = status.HasEnglishSrt,
            HasJapaneseSrt = status.HasJapaneseSrt,
            LastGeneratedAt = status.LastGeneratedAt?.ToString("o"),
            Source = status.Source,
            CoveragePercent = status.CoveragePercent,
            Message = !status.HasEnglishSrt
                ? "No English subtitle found for this item."
                : status.HasJapaneseSrt
                    ? "Japanese subtitle is available."
                    : "Japanese subtitle not yet generated."
        });
    }
}

/// <summary>
/// Response model for the Generate endpoint.
/// </summary>
public class GenerateResponse
{
    /// <summary>Gets or sets whether generation succeeded.</summary>
    public bool Success { get; set; }

    /// <summary>Gets or sets a human-readable message.</summary>
    public string? Message { get; set; }

    /// <summary>Gets or sets the source used.</summary>
    public string? Source { get; set; }

    /// <summary>Gets or sets the output file path.</summary>
    public string? OutputPath { get; set; }

    /// <summary>Gets or sets the cue count.</summary>
    public int CueCount { get; set; }

    /// <summary>Gets or sets the coverage percentage.</summary>
    public double CoveragePercent { get; set; }
}

/// <summary>
/// Response model for the Status endpoint.
/// </summary>
public class StatusResponse
{
    /// <summary>Gets or sets whether English SRT exists.</summary>
    public bool HasEnglishSrt { get; set; }

    /// <summary>Gets or sets whether Japanese SRT exists.</summary>
    public bool HasJapaneseSrt { get; set; }

    /// <summary>Gets or sets when it was last generated.</summary>
    public string? LastGeneratedAt { get; set; }

    /// <summary>Gets or sets the source used.</summary>
    public string? Source { get; set; }

    /// <summary>Gets or sets the coverage percentage.</summary>
    public double? CoveragePercent { get; set; }

    /// <summary>Gets or sets a human-readable message.</summary>
    public string? Message { get; set; }
}
