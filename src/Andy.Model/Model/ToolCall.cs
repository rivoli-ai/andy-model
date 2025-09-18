using System.Text.Json;

namespace Andy.Model.Model;

/// <summary>
/// Tool call emitted by an LLM in an assistant message.
/// </summary>
public sealed class ToolCall
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public string Name { get; init; } = string.Empty;

    // Raw JSON arguments; keeps fidelity across models.
    public string ArgumentsJson { get; init; } = "{}";

    /// <summary>
    /// Arguments as string (alias for ArgumentsJson for backward compatibility).
    /// </summary>
    public string Arguments
    {
        get => ArgumentsJson;
        init => ArgumentsJson = value;
    }

    public JsonElement ArgumentsAsJsonElement()
    {
        return JsonDocument.Parse(string.IsNullOrWhiteSpace(ArgumentsJson) ? "{}" : ArgumentsJson).RootElement.Clone();
    }
}