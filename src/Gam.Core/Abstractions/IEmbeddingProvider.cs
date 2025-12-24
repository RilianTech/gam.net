namespace Gam.Core.Abstractions;

/// <summary>
/// Abstraction for generating embeddings.
/// </summary>
public interface IEmbeddingProvider
{
    /// <summary>
    /// Generate embedding for a single text.
    /// </summary>
    Task<float[]> EmbedAsync(string text, CancellationToken ct = default);
    
    /// <summary>
    /// Generate embeddings for multiple texts (batched).
    /// </summary>
    Task<IReadOnlyList<float[]>> EmbedBatchAsync(
        IEnumerable<string> texts, 
        CancellationToken ct = default);
    
    /// <summary>
    /// Embedding dimension (e.g., 1536 for OpenAI ada-002).
    /// </summary>
    int Dimensions { get; }
    
    /// <summary>
    /// Model identifier.
    /// </summary>
    string Model { get; }
}
