namespace Andy.Model.Conversation;

/// <summary>
/// Configuration options for conversation management strategies.
/// </summary>
public class ConversationManagerOptions
{
    /// <summary>
    /// Maximum number of tokens to include in the context.
    /// </summary>
    public int MaxTokens { get; set; } = 4000;

    /// <summary>
    /// Maximum number of recent messages to preserve.
    /// </summary>
    public int MaxRecentMessages { get; set; } = 20;

    /// <summary>
    /// Maximum age of messages to include in context.
    /// </summary>
    public TimeSpan MaxMessageAge { get; set; } = TimeSpan.FromHours(24);

    /// <summary>
    /// Whether to preserve tool call and result pairs when compacting.
    /// </summary>
    public bool PreserveToolCallPairs { get; set; } = true;

    /// <summary>
    /// Whether to include system messages in the context.
    /// </summary>
    public bool IncludeSystemMessages { get; set; } = true;

    /// <summary>
    /// Whether to include tool messages in the context.
    /// </summary>
    public bool IncludeToolMessages { get; set; } = true;

    /// <summary>
    /// Strategy for compressing conversation history.
    /// </summary>
    public CompressionStrategy CompressionStrategy { get; set; } = CompressionStrategy.Smart;

    /// <summary>
    /// Threshold for automatic compaction (number of turns).
    /// </summary>
    public int CompactionThreshold { get; set; } = 50;

    /// <summary>
    /// Whether to automatically compact when threshold is reached.
    /// </summary>
    public bool AutoCompact { get; set; } = true;

    /// <summary>
    /// Custom metadata to preserve during compaction.
    /// </summary>
    public HashSet<string> PreserveMetadataKeys { get; set; } = new();
}

/// <summary>
/// Compression strategy for conversation management.
/// </summary>
public enum CompressionStrategy
{
    /// <summary>
    /// No compression - include all messages.
    /// </summary>
    None,

    /// <summary>
    /// Simple truncation - keep only recent messages.
    /// </summary>
    Simple,

    /// <summary>
    /// Smart compression - preserve important context while reducing tokens.
    /// </summary>
    Smart,

    /// <summary>
    /// Semantic compression - use embeddings to preserve semantically relevant messages.
    /// </summary>
    Semantic,

    /// <summary>
    /// Summary-based - replace old messages with summaries.
    /// </summary>
    Summary
}