using Andy.Model.Model;

namespace Andy.Model.Llm;

/// <summary>
/// Streaming response chunk from an LLM completion.
/// </summary>
public sealed class LlmStreamResponse
{
    /// <summary>
    /// Partial message content for this chunk.
    /// </summary>
    public Message? Delta { get; init; }

    /// <summary>
    /// Whether this is the final chunk in the stream.
    /// </summary>
    public bool IsComplete { get; init; }

    /// <summary>
    /// Usage information (typically only available in the final chunk).
    /// </summary>
    public LlmUsage? Usage { get; init; }

    /// <summary>
    /// Any error that occurred during streaming.
    /// </summary>
    public string? Error { get; init; }

    /// <summary>
    /// The reason the stream finished (e.g., "stop", "tool_calls", "length").
    /// </summary>
    public string? FinishReason { get; init; }

    // Convenience properties for backward compatibility

    /// <summary>
    /// Text delta content (delegates to Delta.Content if Delta exists).
    /// </summary>
    public string? TextDelta => Delta?.Content;

    /// <summary>
    /// Current tool call being streamed (if any).
    /// </summary>
    public ToolCall? ToolCall => Delta?.ToolCalls?.FirstOrDefault();

    /// <summary>
    /// Current function call being streamed (backward compatibility).
    /// </summary>
    public FunctionCall? FunctionCall => ToolCall != null
        ? new FunctionCall
        {
            Id = ToolCall.Id,
            Name = ToolCall.Name,
            Arguments = ToolCall.ArgumentsJson
        }
        : null;
}