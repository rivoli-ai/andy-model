using System.Text.Json;

namespace Andy.Model.Model;

/// <summary>
/// Function call representation (backward compatibility for tool calls).
/// </summary>
public sealed class FunctionCall
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public string Name { get; init; } = string.Empty;
    public string Arguments { get; init; } = "{}";

    /// <summary>
    /// Parse arguments as JsonElement.
    /// </summary>
    public JsonElement ArgumentsAsJsonElement()
    {
        return JsonDocument.Parse(string.IsNullOrWhiteSpace(Arguments) ? "{}" : Arguments).RootElement.Clone();
    }

    /// <summary>
    /// Convert to ToolCall.
    /// </summary>
    public ToolCall ToToolCall()
    {
        return new ToolCall
        {
            Id = Id,
            Name = Name,
            ArgumentsJson = Arguments
        };
    }
}