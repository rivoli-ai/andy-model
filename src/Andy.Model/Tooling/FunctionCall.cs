namespace Andy.Model.Tooling;

/// <summary>
/// Represents a function call request (backward compatibility type).
/// </summary>
public sealed class FunctionCall
{
    /// <summary>
    /// Unique identifier for this function call.
    /// </summary>
    public string Id { get; init; } = string.Empty;

    /// <summary>
    /// Name of the function to call.
    /// </summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>
    /// Arguments as a JSON string.
    /// </summary>
    public string Arguments { get; init; } = "{}";

    /// <summary>
    /// Alias for Arguments (for consistency with ToolCall).
    /// </summary>
    public string ArgumentsJson
    {
        get => Arguments;
        init => Arguments = value;
    }
}