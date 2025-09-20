namespace Andy.Model.Model;

/// <summary>
/// Simple message type. Content is plain text; ToolCalls/ToolResults
/// are carried separately to stay vendor-agnostic.
/// </summary>
public sealed class Message
{
    public Role Role { get; init; }
    public string Content { get; init; } = string.Empty;

    // When the assistant asks to call tools, it attaches one or more tool calls.
    public List<ToolCall> ToolCalls { get; init; } = new();

    // When tools produce results, they surface as ToolResult messages
    // (with Role = Tool) and optionally attach metadata.
    public List<ToolResult> ToolResults { get; init; } = new();

    // Arbitrary tags/metadata for routing, grounding, etc.
    public Dictionary<string, object> Metadata { get; init; } = new();

    // Timestamp for ordering/debugging
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;

    // Message ID for referencing
    public string Id { get; init; } = Guid.NewGuid().ToString("N");

    // Tool call ID for tool response messages (when Role = Tool)
    public string? ToolCallId { get; init; }

    /// <summary>
    /// Parts-based message content (for backward compatibility).
    /// Returns TextPart for Content, ToolCallPart for ToolCalls, etc.
    /// </summary>
    public List<MessagePart> Parts
    {
        get
        {
            var parts = new List<MessagePart>();

            // Add text content if present
            if (!string.IsNullOrEmpty(Content))
            {
                parts.Add(new TextPart(Content));
            }

            // Add tool calls if present
            foreach (var toolCall in ToolCalls)
            {
                parts.Add(new ToolCallPart(toolCall));
            }

            // Add tool results if present
            foreach (var toolResult in ToolResults)
            {
                parts.Add(new ToolResponsePart(toolResult));
            }

            return parts;
        }
    }

    public override string ToString() => $"[{Role}] {Content}";
}