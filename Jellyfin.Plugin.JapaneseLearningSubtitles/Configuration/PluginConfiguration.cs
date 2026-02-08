using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.JapaneseLearningSubtitles.Configuration;

/// <summary>
/// Plugin configuration model.
/// </summary>
public class PluginConfiguration : BasePluginConfiguration
{
    /// <summary>
    /// Gets or sets the OpenSubtitles username/account.
    /// </summary>
    public string OpenSubtitlesUsername { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the OpenSubtitles password.
    /// </summary>
    public string OpenSubtitlesPassword { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the OpenSubtitles API key.
    /// </summary>
    public string OpenSubtitlesApiKey { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the translation provider.
    /// </summary>
    public TranslationProvider TranslationProvider { get; set; } = TranslationProvider.OpenAI;

    /// <summary>
    /// Gets or sets the OpenAI API key.
    /// </summary>
    public string OpenAIApiKey { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the OpenAI model name.
    /// </summary>
    public string OpenAIModel { get; set; } = "gpt-4o-mini";

    /// <summary>
    /// Gets or sets the DeepL API key (if DeepL provider selected).
    /// </summary>
    public string DeepLApiKey { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the custom HTTP translation endpoint.
    /// </summary>
    public string CustomTranslationEndpoint { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the scan scope.
    /// </summary>
    public ScanScope ScanScope { get; set; } = ScanScope.Both;

    /// <summary>
    /// Gets or sets a value indicating whether to overwrite existing .ja.srt files.
    /// </summary>
    public bool OverwriteExisting { get; set; } = false;

    /// <summary>
    /// Gets or sets the maximum parallel processing count.
    /// </summary>
    public int MaxParallel { get; set; } = 2;

    /// <summary>
    /// Gets or sets the library IDs to scan (empty = all libraries).
    /// </summary>
    public string[] LibraryIds { get; set; } = [];

    /// <summary>
    /// Gets or sets the alignment confidence threshold (0.0 - 1.0).
    /// Below this, fallback to translation for that cue.
    /// </summary>
    public double AlignmentConfidenceThreshold { get; set; } = 0.3;

    /// <summary>
    /// Gets or sets the maximum Japanese characters per subtitle line.
    /// </summary>
    public int MaxJapaneseCharsPerLine { get; set; } = 42;

    /// <summary>
    /// Gets or sets the maximum subtitle lines.
    /// </summary>
    public int MaxSubtitleLines { get; set; } = 2;
}

/// <summary>
/// Translation provider options.
/// </summary>
public enum TranslationProvider
{
    /// <summary>OpenAI API (default, best balance).</summary>
    OpenAI = 0,

    /// <summary>DeepL API.</summary>
    DeepL = 1,

    /// <summary>Custom HTTP endpoint.</summary>
    CustomHTTP = 2
}

/// <summary>
/// Scan scope options.
/// </summary>
public enum ScanScope
{
    /// <summary>Both movies and series.</summary>
    Both = 0,

    /// <summary>Movies only.</summary>
    MoviesOnly = 1,

    /// <summary>Series only.</summary>
    SeriesOnly = 2
}
