using Andy.Model.Model;
using Andy.Model.Utils;

namespace Andy.Model.Conversation;

/// <summary>
/// Conversation manager that maintains a sliding window of recent messages.
/// Older messages are automatically compacted or summarized.
/// </summary>
public class SlidingWindowConversationManager : DefaultConversationManager
{
    private readonly int _windowSize;
    private readonly bool _preserveFirstMessage;
    private Queue<Message>? _summaryQueue;

    // High-water mark: number of leading messages already folded into a summary. Prevents
    // repeated compaction from re-summarizing the same messages into overlapping duplicates.
    private int _summarizedCount;

    public SlidingWindowConversationManager(
        int windowSize = 10,
        bool preserveFirstMessage = true,
        ConversationManagerOptions? options = null)
        : base(options)
    {
        _windowSize = windowSize;
        _preserveFirstMessage = preserveFirstMessage;
    }

    public SlidingWindowConversationManager(
        Model.Conversation conversation,
        int windowSize = 10,
        bool preserveFirstMessage = true,
        ConversationManagerOptions? options = null)
        : base(conversation, options)
    {
        _windowSize = windowSize;
        _preserveFirstMessage = preserveFirstMessage;
    }

    public override IEnumerable<Message> ExtractMessagesForNextTurn()
    {
        // Apply the same age/role filters as the base manager for consistent cross-manager semantics.
        var allMessages = ApplyFilters(Conversation.ToChronoMessages().ToList());
        var result = new List<Message>();

        // Preserve first message if it's a system message
        Message? firstMessage = null;
        if (_preserveFirstMessage && allMessages.Any())
        {
            firstMessage = allMessages.First();
            if (firstMessage.Role == Role.System)
            {
                result.Add(firstMessage);
                allMessages = allMessages.Skip(1).ToList();
            }
        }

        // Apply sliding window
        var windowMessages = allMessages.TakeLast(_windowSize).ToList();

        // Add any summaries from the queue
        if (_summaryQueue?.Any() == true)
        {
            result.Add(new Message
            {
                Role = Role.System,
                Content = "Previous context summary:\n" + string.Join("\n", _summaryQueue.Select(m => m.Content)),
                Timestamp = DateTimeOffset.UtcNow
            });
        }

        result.AddRange(windowMessages);
        return result;
    }

    public override void Reset()
    {
        base.Reset();
        _summaryQueue?.Clear();
        _summarizedCount = 0;
    }

    /// <summary>
    /// Fold the messages that have newly fallen out of the sliding window into a summary.
    /// This is a <em>context</em> compaction: physical history in <see cref="DefaultConversationManager.Conversation"/>
    /// is retained; only the context returned by <see cref="ExtractMessagesForNextTurn"/> is reduced.
    /// Already-summarized messages are never re-summarized, so repeated compaction does not
    /// produce overlapping duplicate summaries.
    /// </summary>
    public override async Task<bool> CompactConversationAsync()
    {
        var allMessages = Conversation.ToChronoMessages().ToList();
        var outsideWindow = Math.Max(0, allMessages.Count - _windowSize);

        // Only summarize messages that fell out of the window since the last compaction.
        if (outsideWindow <= _summarizedCount)
        {
            return false;
        }

        var newlyFallenOut = allMessages
            .Take(outsideWindow)
            .Skip(_summarizedCount)
            .ToList();

        if (newlyFallenOut.Count == 0)
        {
            return false;
        }

        var summary = await CreateSummaryFromMessages(newlyFallenOut);

        _summaryQueue ??= new Queue<Message>(3); // Keep last 3 summaries
        _summaryQueue.Enqueue(new Message
        {
            Role = Role.System,
            Content = summary,
            Timestamp = DateTimeOffset.UtcNow
        });

        while (_summaryQueue.Count > 3)
        {
            _summaryQueue.Dequeue();
        }

        _summarizedCount = outsideWindow;
        return true;
    }

    private async Task<string> CreateSummaryFromMessages(List<Message> messages)
    {
        // Group messages by role for better summary
        var userMessages = messages.Where(m => m.Role == Role.User).ToList();
        var assistantMessages = messages.Where(m => m.Role == Role.Assistant).ToList();
        var toolMessages = messages.Where(m => m.Role == Role.Tool).ToList();

        var summary = new List<string>();

        if (userMessages.Any())
        {
            summary.Add($"User asked about: {GetTopics(userMessages)}");
        }

        if (assistantMessages.Any())
        {
            var toolCallCount = assistantMessages.Sum(m => m.ToolCalls.Count);
            if (toolCallCount > 0)
            {
                summary.Add($"Assistant made {toolCallCount} tool calls");
            }
            summary.Add($"Assistant discussed: {GetTopics(assistantMessages)}");
        }

        if (toolMessages.Any())
        {
            summary.Add($"{toolMessages.Count} tool executions completed");
        }

        return await Task.FromResult(string.Join(". ", summary));
    }

    private string GetTopics(List<Message> messages)
    {
        // Extract key topics from messages (simplified version)
        var topics = new HashSet<string>();

        foreach (var msg in messages.Take(3)) // Sample first 3 messages
        {
            // Extract first few words as topic
            var words = msg.Content.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (words.Length > 0)
            {
                var topic = string.Join(" ", words.Take(Math.Min(5, words.Length)));
                if (topic.Length > 50)
                {
                    topic = topic.Substring(0, 47) + "...";
                }
                topics.Add(topic);
            }
        }

        return string.Join(", ", topics);
    }
}