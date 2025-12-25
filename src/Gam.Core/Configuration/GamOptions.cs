using Gam.Core.Prompts;

namespace Gam.Core.Configuration;

/// <summary>
/// Root configuration for GAM.NET.
/// Bind to "Gam" section in appsettings.json.
/// </summary>
public class GamOptions
{
    public const string SectionName = "Gam";
    
    /// <summary>
    /// Provider to use: "OpenAI", "AzureOpenAI", or "Ollama"
    /// </summary>
    public string Provider { get; set; } = "OpenAI";
    
    public OpenAIOptions OpenAI { get; set; } = new();
    public AzureOpenAIOptions AzureOpenAI { get; set; } = new();
    public OllamaOptions Ollama { get; set; } = new();
    public ResearchSettings Research { get; set; } = new();
    public StorageSettings Storage { get; set; } = new();
    public TtlSettings Ttl { get; set; } = new();
    public PromptOptions Prompts { get; set; } = new();
}

/// <summary>
/// OpenAI provider configuration.
/// </summary>
public class OpenAIOptions
{
    public string ApiKey { get; set; } = "";
    public string Model { get; set; } = "gpt-4o";
    public string EmbeddingModel { get; set; } = "text-embedding-3-small";
    public int EmbeddingDimensions { get; set; } = 1536;
}

/// <summary>
/// Azure OpenAI provider configuration.
/// </summary>
public class AzureOpenAIOptions
{
    public string Endpoint { get; set; } = "";
    public string ApiKey { get; set; } = "";
    public string ChatDeployment { get; set; } = "";
    public string EmbeddingDeployment { get; set; } = "";
    public int EmbeddingDimensions { get; set; } = 1536;
}

/// <summary>
/// Ollama local provider configuration.
/// </summary>
public class OllamaOptions
{
    public string BaseUrl { get; set; } = "http://localhost:11434";
    public string Model { get; set; } = "llama3.2";
    public string EmbeddingModel { get; set; } = "nomic-embed-text";
    public int EmbeddingDimensions { get; set; } = 768;
}

/// <summary>
/// Research agent settings.
/// </summary>
public class ResearchSettings
{
    public int MaxIterations { get; set; } = 5;
    public int MaxPagesPerIteration { get; set; } = 10;
    public int MaxContextTokens { get; set; } = 8000;
    public float MinRelevanceScore { get; set; } = 0.3f;
}

/// <summary>
/// Storage settings.
/// </summary>
public class StorageSettings
{
    public string ConnectionString { get; set; } = "";
    public int EmbeddingDimensions { get; set; } = 1536;
}

/// <summary>
/// TTL (Time-To-Live) settings for automatic memory cleanup.
/// </summary>
public class TtlSettings
{
    /// <summary>
    /// Whether TTL cleanup is enabled. Default: false
    /// </summary>
    public bool Enabled { get; set; } = false;

    /// <summary>
    /// Maximum age of memories in days before they are deleted.
    /// Default: 30
    /// </summary>
    public int MaxAgeDays { get; set; } = 30;

    /// <summary>
    /// How often to run the cleanup job in hours.
    /// Default: 1
    /// </summary>
    public int CleanupIntervalHours { get; set; } = 1;
}
