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
        var allMessages = Conversation.ToChronoMessages().ToList();
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

    public override async Task<bool> CompactConversationAsync()
    {
        var allMessages = Conversation.ToChronoMessages().ToList();

        if (allMessages.Count <= _windowSize)
        {
            return false;
        }

        // Messages that will fall out of the window
        var messagesToCompact = allMessages.SkipLast(_windowSize).ToList();

        if (messagesToCompact.Any())
        {
            // Create a summary of the messages being removed
            var summary = await CreateSummaryFromMessages(messagesToCompact);

            // Initialize summary queue if needed
            _summaryQueue ??= new Queue<Message>(3); // Keep last 3 summaries

            // Add to summary queue
            _summaryQueue.Enqueue(new Message
            {
                Role = Role.System,
                Content = summary,
                Timestamp = DateTimeOffset.UtcNow
            });

            // Keep only recent summaries
            while (_summaryQueue.Count > 3)
            {
                _summaryQueue.Dequeue();
            }

            return true;
        }

        return false;
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