using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Jellyfin.Plugin.JapaneseLearningSubtitles.Providers;

/// <summary>
/// Represents a context cue sent to the translator for better naturalness.
/// </summary>
public class ContextCue
{
    /// <summary>Gets or sets the previous cue text (for context).</summary>
    public string? PreviousText { get; set; }

    /// <summary>Gets or sets the current cue text to translate.</summary>
    public string CurrentText { get; set; } = string.Empty;

    /// <summary>Gets or sets the next cue text (for context).</summary>
    public string? NextText { get; set; }

    /// <summary>Gets or sets the cue index for tracking.</summary>
    public int CueIndex { get; set; }
}

/// <summary>
/// Interface for translation providers (EN → JA).
/// </summary>
public interface ITranslationProvider
{
    /// <summary>
    /// Gets the provider name.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Translates a batch of context cues from English to Japanese.
    /// </summary>
    /// <param name="cues">The cues to translate (each with prev/current/next context).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>List of translated Japanese strings, one per input cue in order.</returns>
    Task<List<string>> TranslateBatchAsync(List<ContextCue> cues, CancellationToken ct);

    /// <summary>
    /// Validates that the provider configuration is complete and credentials work.
    /// </summary>
    Task<bool> ValidateAsync(CancellationToken ct);
}
