using System.Text.Json;
using Andy.Model.Model;

namespace Andy.Model.Utils;

#region Utilities

/// <summary>
/// Extension methods for conversation analysis.
/// </summary>
public static class ConversationExtensions
{
    /// <summary>
    /// Get conversation statistics.
    /// </summary>
    public static ConversationStats GetStats(this Model.Conversation conversation)
    {
        var messages = conversation.ToChronoMessages().ToArray();

        return new ConversationStats
        {
            TotalTurns = conversation.Turns.Count,
            TotalMessages = messages.Length,
            UserMessages = messages.Count(m => m.Role == Role.User),
            AssistantMessages = messages.Count(m => m.Role == Role.Assistant),
            ToolMessages = messages.Count(m => m.Role == Role.Tool),
            SystemMessages = messages.Count(m => m.Role == Role.System),
            ToolCalls = messages.Sum(m => m.ToolCalls.Count),
            ToolResults = messages.Sum(m => m.ToolResults.Count),
            ToolErrors = messages.Sum(m => m.ToolResults.Count(tr => tr.IsError)),
            FirstMessageAt = messages.FirstOrDefault()?.Timestamp,
            LastMessageAt = messages.LastOrDefault()?.Timestamp
        };
    }

    /// <summary>
    /// Get a summary of the conversation.
    /// </summary>
    public static string GetSummary(this Model.Conversation conversation, int maxLength = 500)
    {
        var messages = conversation.ToChronoMessages().ToArray();
        var summary = new System.Text.StringBuilder();

        foreach (var message in messages.TakeLast(10)) // Last 10 messages
        {
            var role = message.Role.ToString().ToLower();
            var content = message.Content.Length > 100
                ? message.Content.Substring(0, 100) + "..."
                : message.Content;

            summary.AppendLine($"{role}: {content}");
        }

        var result = summary.ToString();
        return result.Length > maxLength
            ? result.Substring(0, maxLength) + "..."
            : result;
    }

    /// <summary>
    /// Export conversation to JSON. The format losslessly captures turns, message ids/roles,
    /// tool calls, tool results, metadata, cache control, and conversation state; see
    /// <see cref="ConversationSerialization"/> for the format and its state/metadata type policy.
    /// </summary>
    public static string ToJson(this Model.Conversation conversation)
    {
        return ConversationSerialization.Serialize(conversation);
    }

    /// <summary>
    /// Import a conversation previously produced by <see cref="ToJson"/>. Throws
    /// <see cref="InvalidOperationException"/> with an actionable message for malformed,
    /// null, or version-incompatible payloads.
    /// </summary>
    public static Model.Conversation FromJson(string json)
    {
        return ConversationSerialization.Deserialize(json);
    }
}

#endregion