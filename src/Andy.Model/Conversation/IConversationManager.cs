using Andy.Model.Model;
using Andy.Model.Utils;

namespace Andy.Model.Conversation;

/// <summary>
/// Manages conversations with flexible strategies for context building,
/// message extraction, and conversation compaction.
/// </summary>
public interface IConversationManager
{
    /// <summary>
    /// The underlying conversation being managed.
    /// </summary>
    Model.Conversation Conversation { get; }

    /// <summary>
    /// Add a turn to the managed conversation.
    /// </summary>
    void AddTurn(Turn turn);

    /// <summary>
    /// Extract messages for the next LLM request based on the configured strategy.
    /// This may involve compression, filtering, or reordering.
    /// </summary>
    /// <returns>The messages to send to the LLM for the next turn.</returns>
    IEnumerable<Message> ExtractMessagesForNextTurn();

    /// <summary>
    /// Compact the conversation history to reduce memory usage or token count.
    /// This operation may be irreversible depending on the strategy.
    /// </summary>
    /// <returns>True if compaction occurred, false if not needed.</returns>
    Task<bool> CompactConversationAsync();

    /// <summary>
    /// Single, awaited entry point for automatic (orchestration-driven) compaction.
    /// Performs compaction only when the <c>AutoCompact</c> option is enabled and
    /// <see cref="ShouldCompact"/> returns true. This is the sole owner of automatic
    /// compaction; <see cref="AddTurn"/> never triggers compaction on its own.
    /// </summary>
    /// <returns>True if compaction occurred, false otherwise.</returns>
    Task<bool> CompactIfNeededAsync(CancellationToken ct = default);

    /// <summary>
    /// Get a summary of the conversation up to this point.
    /// Useful for context switching or long-running conversations.
    /// </summary>
    Task<string> GetConversationSummaryAsync();

    /// <summary>
    /// Determine if the conversation should be compacted based on current state.
    /// </summary>
    bool ShouldCompact();

    /// <summary>
    /// Reset the conversation manager state while preserving configuration.
    /// </summary>
    void Reset();

    /// <summary>
    /// Get statistics about the managed conversation.
    /// </summary>
    ConversationStats GetStatistics();
}