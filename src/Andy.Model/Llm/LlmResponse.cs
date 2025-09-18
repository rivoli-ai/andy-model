using Andy.Model.Model;

namespace Andy.Model.Llm;

/// <summary>
/// Response from LLM, which may contain an assistant message and/or tool calls.
/// </summary>
public sealed class LlmResponse
{
    public Message AssistantMessage { get; init; } = new() { Role = Role.Assistant, Content = string.Empty };
    public bool HasToolCalls => AssistantMessage.ToolCalls.Count > 0;

    // Optional: Usage information
    public LlmUsage? Usage { get; init; }

    /// <summary>
    /// The reason the completion finished (e.g., "stop", "tool_calls", "length").
    /// </summary>
    public string? FinishReason { get; init; }

    /// <summary>
    /// The model that generated this response.
    /// </summary>
    public string? Model { get; init; }

    /// <summary>
    /// Optional metadata for the response.
    /// </summary>
    public Dictionary<string, object> Metadata { get; init; } = new();

    // Convenience properties for backward compatibility

    /// <summary>
    /// Content of the assistant message (delegates to AssistantMessage.Content).
    /// </summary>
    public string Content => AssistantMessage.Content;

    /// <summary>
    /// Tool calls from the assistant message (delegates to AssistantMessage.ToolCalls).
    /// </summary>
    public List<ToolCall> ToolCalls => AssistantMessage.ToolCalls;

    /// <summary>
    /// Token usage information (backward compatibility alias).
    /// </summary>
    public LlmUsage? TokensUsed => Usage;

    /// <summary>
    /// Function calls (maps tool calls for backward compatibility).
    /// </summary>
    public List<FunctionCall> FunctionCalls =>
        AssistantMessage.ToolCalls.Select(tc => new FunctionCall
        {
            Id = tc.Id,
            Name = tc.Name,
            Arguments = tc.ArgumentsJson
        }).ToList();
}