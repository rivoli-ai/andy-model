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
        _options.Validate();
        _conversation = new Model.Conversation();
        _compressor = compressor;
    }

    public DefaultConversationManager(Model.Conversation conversation, ConversationManagerOptions? options = null, ICompressor? compressor = null)
    {
        _options = options ?? new ConversationManagerOptions();
        _options.Validate();
        _conversation = conversation ?? throw new ArgumentNullException(nameof(conversation));
        _compressor = compressor;
    }

    public Model.Conversation Conversation => _conversation;

    public virtual void AddTurn(Turn turn)
    {
        // AddTurn never triggers compaction on its own; automatic compaction is owned
        // solely by CompactIfNeededAsync (invoked and awaited by the orchestrator).
        _conversation.AddTurn(turn);
    }

    public virtual async Task<bool> CompactIfNeededAsync(CancellationToken ct = default)
    {
        if (!_options.AutoCompact)
        {
            return false;
        }

        if (!ShouldCompact())
        {
            return false;
        }

        return await CompactConversationAsync().ConfigureAwait(false);
    }

    public virtual IEnumerable<Message> ExtractMessagesForNextTurn()
    {
        var allMessages = _conversation.ToChronoMessages().ToList();

        // Always apply filters first (age, role filters)
        var messages = ApplyFilters(allMessages);

        if (_options.CompressionStrategy != CompressionStrategy.None)
        {
            messages = ApplyCompression(messages);
        }

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

        // Age filter, with an exemption for messages flagged by PreserveMetadataKeys.
        var filtered = messages.Where(m => m.Timestamp > cutoffTime || HasPreservedMetadata(m)).ToList();

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

    /// <summary>True if the message carries any of the configured <see cref="ConversationManagerOptions.PreserveMetadataKeys"/>.</summary>
    protected bool HasPreservedMetadata(Message message)
    {
        if (_options.PreserveMetadataKeys.Count == 0)
        {
            return false;
        }

        return message.Metadata.Keys.Any(_options.PreserveMetadataKeys.Contains);
    }

    /// <summary>
    /// Expand <paramref name="keep"/> so that tool calls and their matching tool results are
    /// retained (or would be removed) as complete protocol units in both directions: keeping a
    /// tool-call assistant message pulls in its result messages, and keeping a tool result pulls
    /// in the assistant message that issued the call. Order is unaffected (callers materialize in
    /// index order).
    /// </summary>
    protected static void EnsureCompleteToolUnits(IReadOnlyList<Message> all, bool[] keep)
    {
        var callToAssistant = new Dictionary<string, int>();
        var callToResults = new Dictionary<string, List<int>>();
        for (int i = 0; i < all.Count; i++)
        {
            foreach (var tc in all[i].ToolCalls)
            {
                callToAssistant[tc.Id] = i;
            }
            foreach (var tr in all[i].ToolResults)
            {
                if (!callToResults.TryGetValue(tr.CallId, out var list))
                {
                    list = new List<int>();
                    callToResults[tr.CallId] = list;
                }
                list.Add(i);
            }
        }

        var changed = true;
        while (changed)
        {
            changed = false;
            for (int i = 0; i < all.Count; i++)
            {
                if (!keep[i]) continue;

                foreach (var tc in all[i].ToolCalls)
                {
                    if (callToResults.TryGetValue(tc.Id, out var results))
                    {
                        foreach (var r in results)
                        {
                            if (!keep[r]) { keep[r] = true; changed = true; }
                        }
                    }
                }

                foreach (var tr in all[i].ToolResults)
                {
                    if (callToAssistant.TryGetValue(tr.CallId, out var parent) && !keep[parent])
                    {
                        keep[parent] = true;
                        changed = true;
                    }
                }
            }
        }
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
        if (messages.Count == 0)
        {
            return messages;
        }

        var recentCount = Math.Min(_options.MaxRecentMessages, messages.Count);
        var recentStart = messages.Count - recentCount;

        var keep = new bool[messages.Count];

        // Always keep the recent window.
        for (int i = recentStart; i < messages.Count; i++)
        {
            keep[i] = true;
        }

        // Always keep the first system message.
        var firstSystem = messages.FindIndex(m => m.Role == Role.System);
        if (firstSystem >= 0)
        {
            keep[firstSystem] = true;
        }

        // Always keep messages flagged by PreserveMetadataKeys.
        for (int i = 0; i < messages.Count; i++)
        {
            if (HasPreservedMetadata(messages[i]))
            {
                keep[i] = true;
            }
        }

        // Keep older assistant messages that carry tool calls (their results are pulled in below).
        if (_options.PreserveToolCallPairs)
        {
            for (int i = 0; i < recentStart; i++)
            {
                if (messages[i].ToolCalls.Any())
                {
                    keep[i] = true;
                }
            }

            // Retain/remove tool calls and results as complete units in both directions.
            EnsureCompleteToolUnits(messages, keep);
        }

        // Materialize in original chronological order.
        var result = new List<Message>();
        for (int i = 0; i < messages.Count; i++)
        {
            if (keep[i]) result.Add(messages[i]);
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

    /// <summary>
    /// Trim <paramref name="messages"/> so that the estimated token total does not exceed
    /// <see cref="ConversationManagerOptions.MaxTokens"/>, preserving chronological order.
    /// </summary>
    /// <remarks>
    /// Policy:
    /// <list type="bullet">
    /// <item>When <see cref="ConversationManagerOptions.IncludeSystemMessages"/> is set and a
    /// system message is present, the first system message is always preserved. If that system
    /// message alone exceeds the budget, it is still preserved and the returned context may
    /// exceed the budget (documented system-preservation exception).</item>
    /// <item>The most recent contiguous run of messages that fits the remaining budget is kept.</item>
    /// <item>Indivisible-message exception: if no non-system message fits, the single most recent
    /// message is included whole even though it exceeds the budget, so the current turn is never
    /// silently dropped.</item>
    /// <item>Token estimation includes tool-call names/arguments and tool-result payloads via
    /// <see cref="EstimateMessageTokens"/>.</item>
    /// </list>
    /// </remarks>
    protected virtual List<Message> ApplyTokenLimit(List<Message> messages)
    {
        if (_compressor != null)
        {
            return _compressor.Compress(messages, _options.MaxTokens);
        }

        if (messages.Count == 0)
        {
            return messages;
        }

        var budget = _options.MaxTokens;
        var estimates = messages.Select(EstimateMessageTokens).ToArray();

        if (estimates.Sum() <= budget)
        {
            return messages; // already within budget; order preserved
        }

        var keep = new bool[messages.Count];
        var running = 0;

        // Always preserve the first system message when configured to include system messages.
        var systemIndex = -1;
        if (_options.IncludeSystemMessages)
        {
            systemIndex = messages.FindIndex(m => m.Role == Role.System);
            if (systemIndex >= 0)
            {
                keep[systemIndex] = true;
                running += estimates[systemIndex];
            }
        }

        // Keep the most recent contiguous run that fits the remaining budget.
        for (int i = messages.Count - 1; i >= 0; i--)
        {
            if (i == systemIndex)
            {
                continue;
            }

            if (running + estimates[i] <= budget)
            {
                keep[i] = true;
                running += estimates[i];
            }
            else
            {
                break;
            }
        }

        // Indivisible-message exception: never drop the current turn entirely.
        var anyNonSystemKept = false;
        for (int i = 0; i < messages.Count; i++)
        {
            if (i != systemIndex && keep[i]) { anyNonSystemKept = true; break; }
        }
        if (!anyNonSystemKept)
        {
            var newest = messages.Count - 1;
            if (newest == systemIndex) newest--;
            if (newest >= 0) keep[newest] = true;
        }

        // Keep tool calls and their results together so the trimmed context stays
        // protocol-valid. Completing a unit at the trim boundary may add the parent
        // assistant message and thus marginally exceed the token budget; this is a
        // deliberate, documented exception in favor of a valid message sequence.
        if (_options.PreserveToolCallPairs)
        {
            EnsureCompleteToolUnits(messages, keep);
        }

        var result = new List<Message>();
        for (int i = 0; i < messages.Count; i++)
        {
            if (keep[i]) result.Add(messages[i]);
        }
        return result;
    }

    protected virtual async Task<string> CreateSummaryAsync(List<Turn> turns)
    {
        // This is a placeholder - in a real implementation, you might use an LLM to generate summaries.
        // Derive counts from the ordered protocol sequence so both legacy and orchestration-produced
        // turns are counted correctly.
        var messageCount = turns.Sum(t => t.EnumerateMessages().Count());
        var toolResultCount = turns.Sum(t => t.EnumerateMessages().Count(m => m.Role == Role.Tool));

        var summary = $"Summary of {turns.Count} turns ({messageCount} messages, {toolResultCount} tool calls):\n";

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

            var turnToolCount = turn.EnumerateMessages().Count(m => m.Role == Role.Tool);
            if (turnToolCount > 0)
            {
                summary += $"  Assistant used {turnToolCount} tools\n";
            }
        }

        return await Task.FromResult(summary);
    }

    protected virtual int EstimateTokens(string content)
    {
        // Simple estimation: ~4 characters per token
        return Math.Max(1, content.Length / 4);
    }

    /// <summary>
    /// Estimate the token cost of a whole message, including tool-call names and JSON
    /// arguments and tool-result payloads, not just the text content. Override to plug in
    /// a model-specific tokenizer.
    /// </summary>
    protected virtual int EstimateMessageTokens(Message message)
    {
        var tokens = EstimateTokens(message.Content);

        foreach (var call in message.ToolCalls)
        {
            tokens += EstimateTokens(call.Name);
            tokens += EstimateTokens(call.ArgumentsJson);
        }

        foreach (var toolResult in message.ToolResults)
        {
            tokens += EstimateTokens(toolResult.Name);
            tokens += EstimateTokens(toolResult.ResultJson);
        }

        return tokens;
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