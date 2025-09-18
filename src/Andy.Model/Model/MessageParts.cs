using System.Text.Json;
using Andy.Model.Llm;

namespace Andy.Model.Model;

/// <summary>
/// Base class for message parts.
/// </summary>
public abstract class MessagePart
{
    public abstract string Type { get; }
}

/// <summary>
/// Text content part of a message.
/// </summary>
public sealed class TextPart : MessagePart
{
    public override string Type => "text";
    public string Text { get; init; } = string.Empty;

    public TextPart() { }
    public TextPart(string text) { Text = text; }

    public override string ToString() => Text;
}

/// <summary>
/// Tool call part of a message.
/// </summary>
public sealed class ToolCallPart : MessagePart
{
    public override string Type => "tool_call";
    public ToolCall ToolCall { get; init; } = new();

    public ToolCallPart() { }
    public ToolCallPart(ToolCall toolCall) { ToolCall = toolCall; }
}

/// <summary>
/// Tool response part of a message.
/// </summary>
public sealed class ToolResponsePart : MessagePart
{
    public override string Type => "tool_response";
    public ToolResult ToolResult { get; init; } = new();

    public ToolResponsePart() { }
    public ToolResponsePart(ToolResult toolResult) { ToolResult = toolResult; }
}

/// <summary>
/// Image part of a message (for multimodal models).
/// </summary>
public sealed class ImagePart : MessagePart
{
    public override string Type => "image";
    public string ImageUrl { get; init; } = string.Empty;
    public string? MimeType { get; init; }
    public byte[]? ImageData { get; init; }
}

/// <summary>
/// Token usage information (for backward compatibility).
/// </summary>
public sealed class TokenUsage
{
    public int PromptTokens { get; init; }
    public int CompletionTokens { get; init; }
    public int TotalTokens { get; init; }

    public static implicit operator TokenUsage(LlmUsage usage)
    {
        return new TokenUsage
        {
            PromptTokens = usage.PromptTokens,
            CompletionTokens = usage.CompletionTokens,
            TotalTokens = usage.TotalTokens
        };
    }
}