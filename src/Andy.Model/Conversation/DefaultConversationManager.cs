using Andy.Model.Model;
using Andy.Model.Utils;

namespace Andy.Model.Conversation;

/// <summary>
/// Default implementation of IConversationManager with configurable strategies.
/// </summary>
public class DefaultConversationManager : IConversationManager
{
    protected readonly ConversationManagerOptions _options;
    private readonly Model.Conversation _conversation;
    private readonly ICompressor? _compressor;

    public DefaultConversationManager(ConversationManagerOptions? options = null, ICompressor? compressor = null)
    {
        _options = options ?? new ConversationManagerOptions();
        _conversation = new Model.Conversation();
        _compressor = compressor;
    }

    public DefaultConversationManager(Model.Conversation conversation, ConversationManagerOptions? options = null, ICompressor? compressor = null)
    {
        _options = options ?? new ConversationManagerOptions();
        _conversation = conversation ?? throw new ArgumentNullException(nameof(conversation));
        _compressor = compressor;
    }

    public Model.Conversation Conversation => _conversation;

    public virtual void AddTurn(Turn turn)
    {
        _conversation.AddTurn(turn);

        if (_options.AutoCompact && ShouldCompact())
        {
            _ = CompactConversationAsync().ConfigureAwait(false);
        }
    }

    public virtual IEnumerable<Message> ExtractMessagesForNextTurn()
    {
        var allMessages = _conversation.ToChronoMessages().ToList();

        if (_options.CompressionStrategy == CompressionStrategy.None)
        {
            return allMessages;
        }

        var messages = ApplyFilters(allMessages);
        messages = ApplyCompression(messages);
        messages = ApplyTokenLimit(messages);

        return messages;
    }

    public virtual async Task<bool> CompactConversationAsync()
    {
        if (!ShouldCompact())
        {
            return false;
        }

        var turns = _conversation.Turns.ToList();
        var turnsToKeep = _options.MaxRecentMessages / 2; // Approximate turns from messages

        if (turns.Count <= turnsToKeep)
        {
            return false;
        }

        // Get summary of older turns if using summary strategy
        if (_options.CompressionStrategy == CompressionStrategy.Summary)
        {
            var olderTurns = turns.Take(turns.Count - turnsToKeep).ToList();
            var summary = await CreateSummaryAsync(olderTurns);

            // Store summary in conversation state
            _conversation.SetState("conversation_summary", summary);
        }

        // For now, we don't actually remove turns (preserving history)
        // but mark them as compacted in state
        _conversation.SetState("last_compaction", DateTimeOffset.UtcNow.ToString("O"));
        _conversation.SetState("compacted_turn_count", (turns.Count - turnsToKeep).ToString());

        return true;
    }

    public virtual async Task<string> GetConversationSummaryAsync()
    {
        var existingSummary = _conversation.GetState<string>("conversation_summary");
        var turns = _conversation.Turns.ToList();

        if (string.IsNullOrEmpty(existingSummary))
        {
            return await CreateSummaryAsync(turns);
        }

        // Get turns since last summary
        var compactedCountStr = _conversation.GetState<string>("compacted_turn_count");
        var compactedCount = int.TryParse(compactedCountStr, out var count) ? count : 0;
        var newTurns = turns.Skip(compactedCount).ToList();

        if (newTurns.Any())
        {
            var newSummary = await CreateSummaryAsync(newTurns);
            return $"{existingSummary}\n\n{newSummary}";
        }

        return existingSummary;
    }

    public virtual bool ShouldCompact()
    {
        return _conversation.Turns.Count > _options.CompactionThreshold;
    }

    public virtual void Reset()
    {
        _conversation.ClearState();
        // Note: We don't clear turns to preserve conversation history
    }

    public virtual ConversationStats GetStatistics()
    {
        return _conversation.GetStats();
    }

    protected virtual List<Message> ApplyFilters(List<Message> messages)
    {
        var cutoffTime = DateTimeOffset.UtcNow - _options.MaxMessageAge;
        var filtered = messages.Where(m => m.Timestamp > cutoffTime).ToList();

        if (!_options.IncludeSystemMessages)
        {
            filtered = filtered.Where(m => m.Role != Role.System).ToList();
        }

        if (!_options.IncludeToolMessages)
        {
            filtered = filtered.Where(m => m.Role != Role.Tool).ToList();
        }

        return filtered;
    }

    protected virtual List<Message> ApplyCompression(List<Message> messages)
    {
        switch (_options.CompressionStrategy)
        {
            case CompressionStrategy.Simple:
                return messages.TakeLast(_options.MaxRecentMessages).ToList();

            case CompressionStrategy.Smart:
                return ApplySmartCompression(messages);

            case CompressionStrategy.Summary:
                return ApplySummaryCompression(messages);

            case CompressionStrategy.Semantic:
                // Would require embeddings - placeholder for now
                return ApplySmartCompression(messages);

            default:
                return messages;
        }
    }

    protected virtual List<Message> ApplySmartCompression(List<Message> messages)
    {
        var result = new List<Message>();
        var recentCount = Math.Min(_options.MaxRecentMessages, messages.Count);

        // Always keep recent messages
        var recentMessages = messages.TakeLast(recentCount).ToList();
        result.AddRange(recentMessages);

        // For older messages, keep important ones
        var olderMessages = messages.SkipLast(recentCount).ToList();

        if (_options.PreserveToolCallPairs)
        {
            // Keep messages with tool calls and their results
            var toolCallIds = new HashSet<string>();
            foreach (var msg in olderMessages)
            {
                foreach (var toolCall in msg.ToolCalls)
                {
                    toolCallIds.Add(toolCall.Id);
                }
            }

            foreach (var msg in olderMessages)
            {
                // Keep if it has tool calls
                if (msg.ToolCalls.Any())
                {
                    result.Insert(0, msg);
                }
                // Keep if it's a tool result for a known call
                else if (msg.Role == Role.Tool && msg.ToolResults.Any(tr => toolCallIds.Contains(tr.CallId)))
                {
                    result.Insert(0, msg);
                }
            }
        }

        // Keep first system message if present
        var firstSystem = messages.FirstOrDefault(m => m.Role == Role.System);
        if (firstSystem != null && !result.Contains(firstSystem))
        {
            result.Insert(0, firstSystem);
        }

        return result;
    }

    protected virtual List<Message> ApplySummaryCompression(List<Message> messages)
    {
        var result = new List<Message>();

        // Add summary if it exists
        var summary = _conversation.GetState<string>("conversation_summary");
        if (!string.IsNullOrEmpty(summary))
        {
            result.Add(new Message
            {
                Role = Role.System,
                Content = $"Previous conversation summary:\n{summary}",
                Timestamp = DateTimeOffset.UtcNow
            });
        }

        // Add recent messages
        var recentCount = Math.Min(_options.MaxRecentMessages, messages.Count);
        result.AddRange(messages.TakeLast(recentCount));

        return result;
    }

    protected virtual List<Message> ApplyTokenLimit(List<Message> messages)
    {
        if (_compressor != null)
        {
            return _compressor.Compress(messages, _options.MaxTokens);
        }

        // Simple token estimation if no compressor provided
        var totalTokens = messages.Sum(m => EstimateTokens(m.Content));

        if (totalTokens <= _options.MaxTokens)
        {
            return messages;
        }

        // Remove messages from the middle, keeping first and last
        var result = new List<Message>();
        var firstSystem = messages.FirstOrDefault(m => m.Role == Role.System);
        if (firstSystem != null)
        {
            result.Add(firstSystem);
            messages = messages.Skip(1).ToList();
        }

        // Keep adding from the end until we hit the limit
        for (int i = messages.Count - 1; i >= 0; i--)
        {
            var msg = messages[i];
            var msgTokens = EstimateTokens(msg.Content);

            if (totalTokens - msgTokens > _options.MaxTokens)
            {
                totalTokens -= msgTokens;
                continue;
            }

            result.Insert(firstSystem != null ? 1 : 0, msg);
        }

        return result;
    }

    protected virtual async Task<string> CreateSummaryAsync(List<Turn> turns)
    {
        // This is a placeholder - in a real implementation, you might use an LLM to generate summaries
        var messageCount = turns.Sum(t => 1 + (t.AssistantMessage != null ? 1 : 0) + t.ToolMessages.Count);
        var toolCallCount = turns.Sum(t => t.ToolMessages.Count);

        var summary = $"Summary of {turns.Count} turns ({messageCount} messages, {toolCallCount} tool calls):\n";

        // Extract key points from each turn
        foreach (var turn in turns.Take(5)) // Summarize first 5 turns as example
        {
            if (turn.UserOrSystemMessage != null)
            {
                var preview = turn.UserOrSystemMessage.Content.Length > 100
                    ? turn.UserOrSystemMessage.Content.Substring(0, 100) + "..."
                    : turn.UserOrSystemMessage.Content;
                summary += $"- User: {preview}\n";
            }

            if (turn.AssistantMessage != null && turn.ToolMessages.Any())
            {
                summary += $"  Assistant used {turn.ToolMessages.Count} tools\n";
            }
        }

        return await Task.FromResult(summary);
    }

    protected virtual int EstimateTokens(string content)
    {
        // Simple estimation: ~4 characters per token
        return Math.Max(1, content.Length / 4);
    }
}

/// <summary>
/// Interface for custom compression implementations.
/// </summary>
public interface ICompressor
{
    /// <summary>
    /// Compress messages to fit within a token budget.
    /// </summary>
    List<Message> Compress(List<Message> messages, int maxTokens);
}