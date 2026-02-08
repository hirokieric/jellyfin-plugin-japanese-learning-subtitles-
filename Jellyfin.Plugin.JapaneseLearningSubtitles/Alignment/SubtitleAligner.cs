using System;
using System.Collections.Generic;
using System.Linq;
using Jellyfin.Plugin.JapaneseLearningSubtitles.Srt;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.JapaneseLearningSubtitles.Alignment;

/// <summary>
/// Aligns Japanese subtitles (from OpenSubtitles) to English subtitle timecodes
/// using dynamic programming sequence alignment.
/// </summary>
public class SubtitleAligner
{
    private readonly ILogger<SubtitleAligner> _logger;
    private readonly double _confidenceThreshold;

    // Cost weights for the DP alignment
    private const double TimeProximityWeight = 0.4;
    private const double LengthRatioWeight = 0.3;
    private const double SequencePositionWeight = 0.3;

    // Penalty for skipping cues
    private const double SkipPenalty = 0.6;
    private const double MergePenalty = 0.15;

    /// <summary>
    /// Initializes a new instance of the <see cref="SubtitleAligner"/> class.
    /// </summary>
    public SubtitleAligner(ILogger<SubtitleAligner> logger, double confidenceThreshold = 0.3)
    {
        _logger = logger;
        _confidenceThreshold = confidenceThreshold;
    }

    /// <summary>
    /// Aligns Japanese cues to English cues, producing one Japanese text per English cue.
    /// </summary>
    /// <param name="enCues">English subtitle cues (target timecodes).</param>
    /// <param name="jpCues">Japanese subtitle cues (source text to align).</param>
    /// <returns>Alignment result with one entry per English cue.</returns>
    public AlignmentResult Align(List<SubtitleCue> enCues, List<SubtitleCue> jpCues)
    {
        int n = enCues.Count;
        int m = jpCues.Count;

        _logger.LogInformation("Aligning {JpCount} JP cues to {EnCount} EN cues", m, n);

        if (n == 0)
        {
            return new AlignmentResult { Entries = new List<AlignmentEntry>() };
        }

        if (m == 0)
        {
            // No JP cues at all — everything needs translation
            return CreateEmptyResult(enCues);
        }

        // Compute the similarity matrix
        var similarity = ComputeSimilarityMatrix(enCues, jpCues);

        // Run DP alignment
        var mapping = RunDPAlignment(n, m, similarity);

        // Build result
        var entries = new List<AlignmentEntry>(n);
        int matchedCount = 0;

        for (int i = 0; i < n; i++)
        {
            var enCue = enCues[i];
            var mappedJpIndices = mapping[i];

            string jpText;
            double confidence;

            if (mappedJpIndices.Count > 0)
            {
                // Join text from all mapped JP cues
                var jpTexts = mappedJpIndices
                    .OrderBy(j => j)
                    .Select(j => SrtParser.StripTags(jpCues[j].Text).Trim())
                    .Where(t => !string.IsNullOrWhiteSpace(t))
                    .ToList();

                jpText = string.Join("\n", jpTexts);

                // Confidence = average similarity of mapped pairs
                confidence = mappedJpIndices.Average(j => similarity[i, j]);
                matchedCount++;
            }
            else
            {
                jpText = string.Empty;
                confidence = 0.0;
            }

            entries.Add(new AlignmentEntry
            {
                EnCueIndex = i,
                StartMs = enCue.StartMs,
                EndMs = enCue.EndMs,
                EnglishText = enCue.Text,
                JapaneseText = jpText,
                Confidence = confidence,
                NeedsTranslation = string.IsNullOrWhiteSpace(jpText) || confidence < _confidenceThreshold
            });
        }

        _logger.LogInformation(
            "Alignment complete: {Matched}/{Total} EN cues matched, {NeedTranslation} need translation",
            matchedCount, n, entries.Count(e => e.NeedsTranslation));

        return new AlignmentResult { Entries = entries };
    }

    /// <summary>
    /// Computes the NxM similarity matrix between EN and JP cues.
    /// </summary>
    private double[,] ComputeSimilarityMatrix(List<SubtitleCue> enCues, List<SubtitleCue> jpCues)
    {
        int n = enCues.Count;
        int m = jpCues.Count;
        var sim = new double[n, m];

        // Pre-compute total durations for normalization
        long enTotalDuration = enCues.Last().EndMs - enCues.First().StartMs;
        long jpTotalDuration = jpCues.Last().EndMs - jpCues.First().StartMs;

        if (enTotalDuration <= 0) enTotalDuration = 1;
        if (jpTotalDuration <= 0) jpTotalDuration = 1;

        for (int i = 0; i < n; i++)
        {
            for (int j = 0; j < m; j++)
            {
                sim[i, j] = ComputeCueSimilarity(
                    enCues[i], jpCues[j],
                    i, j, n, m,
                    enTotalDuration, jpTotalDuration);
            }
        }

        return sim;
    }

    /// <summary>
    /// Computes similarity between a single EN cue and a single JP cue.
    /// </summary>
    private double ComputeCueSimilarity(
        SubtitleCue enCue, SubtitleCue jpCue,
        int enIdx, int jpIdx, int enTotal, int jpTotal,
        long enTotalDuration, long jpTotalDuration)
    {
        // 1. Time proximity score
        double enRelativePos = (double)(enCue.StartMs - 0) / enTotalDuration;
        double jpRelativePos = (double)(jpCue.StartMs - 0) / jpTotalDuration;
        double timeScore = 1.0 - Math.Min(1.0, Math.Abs(enRelativePos - jpRelativePos) * 3.0);
        timeScore = Math.Max(0, timeScore);

        // 2. Duration/length ratio score
        double enDuration = Math.Max(enCue.DurationMs, 100);
        double jpDuration = Math.Max(jpCue.DurationMs, 100);
        double durationRatio = Math.Min(enDuration, jpDuration) / Math.Max(enDuration, jpDuration);

        // Text length ratio (EN chars vs JP chars, accounting for JP being denser)
        int enLen = TextNormalizer.NormalizeEnglish(enCue.Text).Length;
        int jpLen = TextNormalizer.NormalizeJapanese(jpCue.Text).Length;
        // JP is roughly 2-3x denser than EN, so normalize
        double adjustedJpLen = jpLen * 2.5;
        double lengthRatio = enLen > 0 && adjustedJpLen > 0
            ? Math.Min(enLen, adjustedJpLen) / Math.Max(enLen, adjustedJpLen)
            : 0.5;

        double lengthScore = (durationRatio + lengthRatio) / 2.0;

        // 3. Sequence position score (monotonicity preference)
        double enPos = enTotal > 1 ? (double)enIdx / (enTotal - 1) : 0.5;
        double jpPos = jpTotal > 1 ? (double)jpIdx / (jpTotal - 1) : 0.5;
        double positionScore = 1.0 - Math.Min(1.0, Math.Abs(enPos - jpPos) * 2.0);
        positionScore = Math.Max(0, positionScore);

        // Weighted combination
        double score = (TimeProximityWeight * timeScore)
                     + (LengthRatioWeight * lengthScore)
                     + (SequencePositionWeight * positionScore);

        return Math.Max(0, Math.Min(1.0, score));
    }

    /// <summary>
    /// Runs DP sequence alignment to find optimal mapping from EN cues to JP cues.
    /// </summary>
    /// <returns>For each EN index, a list of JP indices mapped to it.</returns>
    private List<List<int>> RunDPAlignment(int n, int m, double[,] similarity)
    {
        // dp[i,j] = best score for aligning EN[0..i-1] with JP[0..j-1]
        var dp = new double[n + 1, m + 1];
        var backtrack = new (int prevI, int prevJ, DPOp op)[n + 1, m + 1];

        // Initialize: skipping costs
        for (int i = 1; i <= n; i++)
        {
            dp[i, 0] = dp[i - 1, 0] - SkipPenalty;
            backtrack[i, 0] = (i - 1, 0, DPOp.SkipEn);
        }

        for (int j = 1; j <= m; j++)
        {
            dp[0, j] = dp[0, j - 1] - SkipPenalty;
            backtrack[0, j] = (0, j - 1, DPOp.SkipJp);
        }

        // Fill DP table
        for (int i = 1; i <= n; i++)
        {
            for (int j = 1; j <= m; j++)
            {
                double bestScore = double.NegativeInfinity;
                var bestOp = DPOp.Match;
                int bestPrevI = i - 1, bestPrevJ = j - 1;

                // Option 1: Match EN[i-1] with JP[j-1]
                double matchScore = dp[i - 1, j - 1] + similarity[i - 1, j - 1];
                if (matchScore > bestScore)
                {
                    bestScore = matchScore;
                    bestOp = DPOp.Match;
                    bestPrevI = i - 1;
                    bestPrevJ = j - 1;
                }

                // Option 2: Skip EN cue (no JP match)
                double skipEnScore = dp[i - 1, j] - SkipPenalty;
                if (skipEnScore > bestScore)
                {
                    bestScore = skipEnScore;
                    bestOp = DPOp.SkipEn;
                    bestPrevI = i - 1;
                    bestPrevJ = j;
                }

                // Option 3: Skip JP cue (extra JP, not used)
                double skipJpScore = dp[i, j - 1] - SkipPenalty;
                if (skipJpScore > bestScore)
                {
                    bestScore = skipJpScore;
                    bestOp = DPOp.SkipJp;
                    bestPrevI = i;
                    bestPrevJ = j - 1;
                }

                // Option 4: Merge — EN[i-1] matched with JP[j-2] and JP[j-1] (2 JP → 1 EN)
                if (j >= 2)
                {
                    double mergeScore = dp[i - 1, j - 2]
                        + (similarity[i - 1, j - 2] + similarity[i - 1, j - 1]) / 2.0
                        - MergePenalty;
                    if (mergeScore > bestScore)
                    {
                        bestScore = mergeScore;
                        bestOp = DPOp.MergeJp;
                        bestPrevI = i - 1;
                        bestPrevJ = j - 2;
                    }
                }

                // Option 5: Split — JP[j-1] covers EN[i-2] and EN[i-1] (1 JP → 2 EN)
                if (i >= 2)
                {
                    double splitScore = dp[i - 2, j - 1]
                        + (similarity[i - 2, j - 1] + similarity[i - 1, j - 1]) / 2.0
                        - MergePenalty;
                    if (splitScore > bestScore)
                    {
                        bestScore = splitScore;
                        bestOp = DPOp.SplitEn;
                        bestPrevI = i - 2;
                        bestPrevJ = j - 1;
                    }
                }

                dp[i, j] = bestScore;
                backtrack[i, j] = (bestPrevI, bestPrevJ, bestOp);
            }
        }

        // Backtrack to find mapping
        var mapping = new List<List<int>>(n);
        for (int i = 0; i < n; i++)
        {
            mapping.Add(new List<int>());
        }

        int ci = n, cj = m;
        while (ci > 0 || cj > 0)
        {
            if (ci == 0 && cj == 0) break;

            var (prevI, prevJ, op) = backtrack[ci, cj];

            switch (op)
            {
                case DPOp.Match:
                    // EN[ci-1] ↔ JP[cj-1]
                    mapping[ci - 1].Add(cj - 1);
                    break;

                case DPOp.SkipEn:
                    // EN[ci-1] has no JP match
                    break;

                case DPOp.SkipJp:
                    // JP[cj-1] is unused
                    break;

                case DPOp.MergeJp:
                    // EN[ci-1] ↔ JP[cj-2] + JP[cj-1]
                    mapping[ci - 1].Add(cj - 2);
                    mapping[ci - 1].Add(cj - 1);
                    break;

                case DPOp.SplitEn:
                    // JP[cj-1] ↔ EN[ci-2] + EN[ci-1]
                    mapping[ci - 2].Add(cj - 1);
                    mapping[ci - 1].Add(cj - 1);
                    break;
            }

            ci = prevI;
            cj = prevJ;
        }

        return mapping;
    }

    private static AlignmentResult CreateEmptyResult(List<SubtitleCue> enCues)
    {
        return new AlignmentResult
        {
            Entries = enCues.Select((c, i) => new AlignmentEntry
            {
                EnCueIndex = i,
                StartMs = c.StartMs,
                EndMs = c.EndMs,
                EnglishText = c.Text,
                JapaneseText = string.Empty,
                Confidence = 0.0,
                NeedsTranslation = true
            }).ToList()
        };
    }

    private enum DPOp
    {
        Match,
        SkipEn,
        SkipJp,
        MergeJp,
        SplitEn
    }
}

/// <summary>
/// Result of subtitle alignment.
/// </summary>
public class AlignmentResult
{
    /// <summary>Gets or sets the list of alignment entries (one per EN cue).</summary>
    public List<AlignmentEntry> Entries { get; set; } = new();

    /// <summary>Gets the percentage of EN cues that have Japanese text.</summary>
    public double CoveragePercent =>
        Entries.Count > 0
            ? Entries.Count(e => !string.IsNullOrWhiteSpace(e.JapaneseText)) * 100.0 / Entries.Count
            : 0;
}

/// <summary>
/// A single entry in the alignment result.
/// </summary>
public class AlignmentEntry
{
    /// <summary>Gets or sets the index into the original EN cue list.</summary>
    public int EnCueIndex { get; set; }

    /// <summary>Gets or sets the start time in milliseconds (from EN cue).</summary>
    public long StartMs { get; set; }

    /// <summary>Gets or sets the end time in milliseconds (from EN cue).</summary>
    public long EndMs { get; set; }

    /// <summary>Gets or sets the English text.</summary>
    public string EnglishText { get; set; } = string.Empty;

    /// <summary>Gets or sets the aligned Japanese text.</summary>
    public string JapaneseText { get; set; } = string.Empty;

    /// <summary>Gets or sets the alignment confidence (0.0 - 1.0).</summary>
    public double Confidence { get; set; }

    /// <summary>Gets or sets a value indicating whether this cue needs translation fallback.</summary>
    public bool NeedsTranslation { get; set; }
}
