using Andy.Model.Model;

namespace Andy.Model.Conversation;

/// <summary>
/// Conversation manager that uses semantic importance to determine which messages to keep.
/// Preserves messages based on their relevance to the current context.
/// </summary>
public class SemanticConversationManager : DefaultConversationManager
{
    private readonly HashSet<string> _importantKeywords = new();
    private readonly Dictionary<string, double> _messageImportanceScores = new();

    public SemanticConversationManager(ConversationManagerOptions? options = null)
        : base(options)
    {
        InitializeDefaultKeywords();
    }

    public SemanticConversationManager(Model.Conversation conversation, ConversationManagerOptions? options = null)
        : base(conversation, options)
    {
        InitializeDefaultKeywords();
    }

    /// <summary>
    /// Add keywords that indicate important messages to preserve.
    /// </summary>
    public void AddImportantKeywords(params string[] keywords)
    {
        foreach (var keyword in keywords)
        {
            _importantKeywords.Add(keyword.ToLowerInvariant());
        }
    }

    public override IEnumerable<Message> ExtractMessagesForNextTurn()
    {
        // Apply the same age/role filters as the base manager for consistent cross-manager semantics.
        var allMessages = ApplyFilters(Conversation.ToChronoMessages().ToList());

        // Score all messages
        ScoreMessages(allMessages);

        // Select messages based on importance
        var selectedMessages = SelectImportantMessages(allMessages);

        return selectedMessages;
    }

    public override void Reset()
    {
        base.Reset();
        _messageImportanceScores.Clear();
    }

    protected virtual void ScoreMessages(List<Message> messages)
    {
        _messageImportanceScores.Clear();

        for (int i = 0; i < messages.Count; i++)
        {
            var message = messages[i];
            var score = CalculateImportanceScore(message, i, messages.Count);
            _messageImportanceScores[message.Id] = score;
        }
    }

    protected virtual double CalculateImportanceScore(Message message, int position, int totalMessages)
    {
        double score = 0.0;

        // Recency score (more recent = higher score)
        double recencyScore = (double)position / totalMessages;
        score += recencyScore * 0.3;

        // Role-based scoring
        switch (message.Role)
        {
            case Role.System:
                score += 0.8; // System messages are usually important
                break;
            case Role.User:
                score += 0.6; // User messages provide context
                break;
            case Role.Assistant:
                score += 0.4; // Assistant responses
                if (message.ToolCalls.Any())
                {
                    score += 0.3; // Tool calls are important
                }
                break;
            case Role.Tool:
                score += 0.5; // Tool results are valuable
                break;
        }

        // Keyword scoring
        var contentLower = message.Content.ToLowerInvariant();
        foreach (var keyword in _importantKeywords)
        {
            if (contentLower.Contains(keyword))
            {
                score += 0.2;
            }
        }

        // Length scoring (longer messages might contain more information)
        if (message.Content.Length > 500)
        {
            score += 0.1;
        }

        // Tool call importance
        if (message.ToolCalls.Any())
        {
            score += 0.2 * message.ToolCalls.Count;
        }

        // Error messages are important
        if (message.Metadata?.ContainsKey("is_error") == true ||
            contentLower.Contains("error") ||
            contentLower.Contains("exception"))
        {
            score += 0.4;
        }

        // First and last messages bonus
        if (position == 0 || position == totalMessages - 1)
        {
            score += 0.3;
        }

        return Math.Min(1.0, score); // Cap at 1.0
    }

    protected virtual List<Message> SelectImportantMessages(List<Message> messages)
    {
        var options = base._options;
        var keep = new bool[messages.Count];
        var tokenCount = 0;
        var messageCount = 0;

        // Always include the first system message if present.
        var firstSystem = messages.FindIndex(m => m.Role == Role.System);
        if (firstSystem >= 0)
        {
            keep[firstSystem] = true;
            tokenCount += EstimateTokens(messages[firstSystem].Content);
            messageCount++;
        }

        // Always include messages flagged by PreserveMetadataKeys.
        for (int i = 0; i < messages.Count; i++)
        {
            if (!keep[i] && HasPreservedMetadata(messages[i]))
            {
                keep[i] = true;
                tokenCount += EstimateTokens(messages[i].Content);
                messageCount++;
            }
        }

        // Candidate indices ordered by importance (highest first).
        var candidates = Enumerable.Range(0, messages.Count)
            .Where(i => !keep[i])
            .OrderByDescending(i => _messageImportanceScores.GetValueOrDefault(messages[i].Id, 0))
            .ToList();

        foreach (var i in candidates)
        {
            if (messageCount >= options.MaxRecentMessages)
            {
                break; // message-count cap reached
            }

            var msgTokens = EstimateTokens(messages[i].Content);
            if (tokenCount + msgTokens > options.MaxTokens)
            {
                // Skip this oversized candidate but keep considering smaller ones.
                continue;
            }

            keep[i] = true;
            tokenCount += msgTokens;
            messageCount++;
        }

        // Retain/remove tool calls and their results as complete units in both directions.
        // Completing a unit may add its counterpart even past the caps above; this is the same
        // documented, bounded trade-off used by the base manager in favor of a protocol-valid
        // sequence.
        if (options.PreserveToolCallPairs)
        {
            EnsureCompleteToolUnits(messages, keep);
        }

        // Materialize in chronological order.
        var result = new List<Message>();
        for (int i = 0; i < messages.Count; i++)
        {
            if (keep[i]) result.Add(messages[i]);
        }
        return result;
    }

    private void InitializeDefaultKeywords()
    {
        // Add default important keywords
        _importantKeywords.UnionWith(new[]
        {
            "important", "critical", "remember", "note", "key", "summary",
            "goal", "objective", "requirement", "constraint", "deadline",
            "error", "warning", "issue", "problem", "solution", "fix"
        });
    }
}