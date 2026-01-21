using System.Text.Json.Serialization;

namespace Gam.Benchmarks.Framework;

/// <summary>
/// LoCoMo (Long-term Conversational Memory) dataset models.
/// Based on: https://github.com/snap-research/locomo
/// </summary>

public class LoCoMoDataset
{
    public List<LoCoMoSample> Samples { get; set; } = [];
}

public class LoCoMoSample
{
    [JsonPropertyName("sample_id")]
    public string? SampleId { get; set; }
    
    [JsonPropertyName("conversation")]
    public LoCoMoConversation Conversation { get; set; } = new();
    
    [JsonPropertyName("qa")]
    public List<LoCoMoQuestion> Questions { get; set; } = [];
}

public class LoCoMoConversation
{
    [JsonPropertyName("speaker_a")]
    public string SpeakerA { get; set; } = "";
    
    [JsonPropertyName("speaker_b")]
    public string SpeakerB { get; set; } = "";
    
    /// <summary>
    /// Sessions are stored as session_1, session_2, etc. with corresponding
    /// session_1_date_time timestamps. Use GetSessions() to extract them.
    /// </summary>
    [JsonExtensionData]
    public Dictionary<string, object>? ExtensionData { get; set; }
}

public class LoCoMoSession
{
    public int Index { get; set; }
    public string DateTime { get; set; } = "";
    public List<LoCoMoTurn> Turns { get; set; } = [];
    public string? Summary { get; set; }
}

public class LoCoMoTurn
{
    [JsonPropertyName("speaker")]
    public string Speaker { get; set; } = "";
    
    [JsonPropertyName("dia_id")]
    public string DialogId { get; set; } = "";
    
    [JsonPropertyName("text")]
    public string Text { get; set; } = "";
    
    [JsonPropertyName("img_url")]
    public List<string>? ImageUrls { get; set; }
    
    [JsonPropertyName("blip_caption")]
    public string? ImageCaption { get; set; }
}

public class LoCoMoQuestion
{
    [JsonPropertyName("question")]
    public string Question { get; set; } = "";
    
    [JsonPropertyName("answer")]
    public object? Answer { get; set; }  // Can be string or number
    
    [JsonPropertyName("category")]
    public int Category { get; set; }
    
    [JsonPropertyName("evidence")]
    public List<string>? Evidence { get; set; }
    
    [JsonPropertyName("adversarial_answer")]
    public string? AdversarialAnswer { get; set; }
    
    /// <summary>Get answer as string regardless of JSON type</summary>
    public string AnswerText => Answer?.ToString() ?? "";
}

/// <summary>
/// LoCoMo QA categories as defined in the paper.
/// </summary>
public enum LoCoMoCategory
{
    /// <summary>Single turn lookup - answer from one dialogue turn</summary>
    SingleHop = 1,
    
    /// <summary>Time-based reasoning - "When did X happen?"</summary>
    Temporal = 2,
    
    /// <summary>Multi-hop reasoning - connect multiple turns</summary>
    MultiHop = 3,
    
    /// <summary>General knowledge combined with memory</summary>
    Knowledge = 4,
    
    /// <summary>Adversarial - tests for hallucination (swapped names)</summary>
    Adversarial = 5
}

public static class LoCoMoCategoryExtensions
{
    public static string ToDisplayName(this LoCoMoCategory category) => category switch
    {
        LoCoMoCategory.SingleHop => "SingleHop",
        LoCoMoCategory.Temporal => "Temporal",
        LoCoMoCategory.MultiHop => "MultiHop",
        LoCoMoCategory.Knowledge => "Knowledge",
        LoCoMoCategory.Adversarial => "Adversarial",
        _ => "Unknown"
    };
}
