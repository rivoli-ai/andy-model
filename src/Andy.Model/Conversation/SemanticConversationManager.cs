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
        var allMessages = Conversation.ToChronoMessages().ToList();

        // Score all messages
        ScoreMessages(allMessages);

        // Select messages based on importance
        var selectedMessages = SelectImportantMessages(allMessages);

        return selectedMessages;
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
        var options = base._options ?? new ConversationManagerOptions();

        // Sort messages by importance score
        var sortedMessages = messages
            .OrderByDescending(m => _messageImportanceScores.GetValueOrDefault(m.Id, 0))
            .ToList();

        var result = new List<Message>();
        var tokenCount = 0;
        var messageCount = 0;

        // Always include the first system message if present
        var firstSystem = messages.FirstOrDefault(m => m.Role == Role.System);
        if (firstSystem != null)
        {
            result.Add(firstSystem);
            tokenCount += EstimateTokens(firstSystem.Content);
            messageCount++;
        }

        // Add messages by importance until we hit limits
        foreach (var message in sortedMessages)
        {
            if (message == firstSystem) continue; // Already added

            var msgTokens = EstimateTokens(message.Content);

            // Check limits
            if (tokenCount + msgTokens > options.MaxTokens)
                break;
            if (messageCount >= options.MaxRecentMessages)
                break;

            result.Add(message);
            tokenCount += msgTokens;
            messageCount++;
        }

        // Ensure messages are in chronological order
        result = result.OrderBy(m => m.Timestamp).ToList();

        // If we have tool calls without their results, add the results
        if (options.PreserveToolCallPairs)
        {
            result = EnsureToolCallPairs(messages, result);
        }

        return result;
    }

    protected virtual List<Message> EnsureToolCallPairs(List<Message> allMessages, List<Message> selectedMessages)
    {
        var toolCallIds = new HashSet<string>();

        // Find all tool calls in selected messages
        foreach (var msg in selectedMessages)
        {
            foreach (var toolCall in msg.ToolCalls)
            {
                toolCallIds.Add(toolCall.Id);
            }
        }

        // Add missing tool results
        foreach (var msg in allMessages)
        {
            if (msg.Role == Role.Tool && !selectedMessages.Contains(msg))
            {
                if (msg.ToolResults.Any(tr => toolCallIds.Contains(tr.CallId)))
                {
                    selectedMessages.Add(msg);
                }
            }
        }

        return selectedMessages.OrderBy(m => m.Timestamp).ToList();
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