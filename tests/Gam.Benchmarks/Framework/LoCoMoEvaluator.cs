using System.Text.RegularExpressions;

namespace Gam.Benchmarks.Framework;

/// <summary>
/// Evaluator for LoCoMo benchmark.
/// Implements F1 and BLEU-1 scoring matching the original LoCoMo paper methodology.
/// </summary>
public static class LoCoMoEvaluator
{
    private static readonly Regex PunctuationRegex = new(@"[^\w\s]", RegexOptions.Compiled);
    private static readonly Regex WhitespaceRegex = new(@"\s+", RegexOptions.Compiled);
    private static readonly Regex ArticlesRegex = new(@"(^|\s)(a|an|the)(\s|$)", RegexOptions.Compiled);

    /// <summary>
    /// Normalize text for comparison.
    /// - Lowercase
    /// - Remove punctuation
    /// - Remove articles (a, an, the)
    /// - Collapse whitespace
    /// </summary>
    public static string NormalizeText(string? text)
    {
        if (string.IsNullOrEmpty(text)) return "";
        
        var s = text.ToLowerInvariant().Trim();
        s = PunctuationRegex.Replace(s, " ");
        s = WhitespaceRegex.Replace(s, " ").Trim();
        s = ArticlesRegex.Replace(s, " ");
        s = WhitespaceRegex.Replace(s, " ").Trim();
        
        return s;
    }
    
    /// <summary>
    /// Tokenize normalized text into words.
    /// </summary>
    public static List<string> Tokenize(string? text)
    {
        var normalized = NormalizeText(text);
        return string.IsNullOrEmpty(normalized) 
            ? [] 
            : normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();
    }
    
    /// <summary>
    /// Compute token-level F1 score between prediction and gold answer.
    /// This is the primary metric used in LoCoMo benchmark.
    /// </summary>
    public static float ComputeF1Score(string prediction, string goldAnswer)
    {
        var predTokens = Tokenize(prediction);
        var goldTokens = Tokenize(goldAnswer);
        
        // Both empty = perfect match
        if (predTokens.Count == 0 && goldTokens.Count == 0)
            return 1.0f;
        
        // One empty = no match
        if (predTokens.Count == 0 || goldTokens.Count == 0)
            return 0.0f;
        
        // Count token overlap
        var goldCounts = goldTokens.GroupBy(t => t).ToDictionary(g => g.Key, g => g.Count());
        var predCounts = predTokens.GroupBy(t => t).ToDictionary(g => g.Key, g => g.Count());
        
        int overlap = 0;
        foreach (var (token, predCount) in predCounts)
        {
            if (goldCounts.TryGetValue(token, out int goldCount))
            {
                overlap += Math.Min(predCount, goldCount);
            }
        }
        
        if (overlap == 0) return 0.0f;
        
        float precision = (float)overlap / predTokens.Count;
        float recall = (float)overlap / goldTokens.Count;
        
        return 2 * precision * recall / (precision + recall);
    }
    
    /// <summary>
    /// Compute BLEU-1 score (unigram precision with brevity penalty).
    /// Secondary metric used in LoCoMo benchmark.
    /// </summary>
    public static float ComputeBleu1Score(string prediction, string goldAnswer)
    {
        var predTokens = Tokenize(prediction);
        var goldTokens = Tokenize(goldAnswer);
        
        if (predTokens.Count == 0) return 0.0f;
        
        // Count clipped unigram matches
        var goldCounts = goldTokens.GroupBy(t => t).ToDictionary(g => g.Key, g => g.Count());
        var predCounts = predTokens.GroupBy(t => t).ToDictionary(g => g.Key, g => g.Count());
        
        int clipped = 0;
        foreach (var (token, predCount) in predCounts)
        {
            if (goldCounts.TryGetValue(token, out int goldCount))
            {
                clipped += Math.Min(predCount, goldCount);
            }
        }
        
        float precision = (float)clipped / predTokens.Count;
        
        // Brevity penalty
        float bp = predTokens.Count >= goldTokens.Count 
            ? 1.0f 
            : (float)Math.Exp(1 - (double)goldTokens.Count / predTokens.Count);
        
        return bp * precision;
    }
    
    /// <summary>
    /// Compute both F1 and BLEU-1 scores.
    /// </summary>
    public static (float F1, float Bleu1) ComputeScores(string prediction, string goldAnswer)
    {
        return (ComputeF1Score(prediction, goldAnswer), ComputeBleu1Score(prediction, goldAnswer));
    }
}

/// <summary>
/// Result of evaluating a single LoCoMo question.
/// </summary>
public record LoCoMoQuestionResult
{
    public required string Question { get; init; }
    public required string GoldAnswer { get; init; }
    public required string PredictedAnswer { get; init; }
    public required LoCoMoCategory Category { get; init; }
    public required float F1Score { get; init; }
    public required float Bleu1Score { get; init; }
    public required TimeSpan ResearchDuration { get; init; }
    public required TimeSpan AnswerDuration { get; init; }
    public required int PagesRetrieved { get; init; }
    public required int IterationsPerformed { get; init; }
    public string? Error { get; init; }
}

/// <summary>
/// Aggregated results for a LoCoMo category.
/// </summary>
public record LoCoMoCategoryResult
{
    public required LoCoMoCategory Category { get; init; }
    public required int Count { get; init; }
    public required float AverageF1 { get; init; }
    public required float AverageBleu1 { get; init; }
    public required TimeSpan AverageQueryTime { get; init; }
}

/// <summary>
/// Complete results from running LoCoMo benchmark.
/// </summary>
public record LoCoMoBenchmarkResults
{
    public required string DatasetName { get; init; }
    public required string ConfigurationName { get; init; }
    public required int TotalSamples { get; init; }
    public required int TotalQuestions { get; init; }
    public required float OverallF1 { get; init; }
    public required float OverallBleu1 { get; init; }
    public required TimeSpan ConstructionTime { get; init; }
    public required TimeSpan RetrievalTime { get; init; }
    public required TimeSpan TotalTime { get; init; }
    public required List<LoCoMoCategoryResult> CategoryResults { get; init; }
    public required List<LoCoMoQuestionResult> QuestionResults { get; init; }
}
