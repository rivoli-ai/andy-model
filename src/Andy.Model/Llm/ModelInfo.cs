namespace Andy.Model.Llm;

/// <summary>
/// Information about an available model from an LLM provider.
/// </summary>
public class ModelInfo
{
    /// <summary>
    /// The model identifier used for API calls.
    /// </summary>
    public required string Id { get; init; }

    /// <summary>
    /// Human-readable name of the model.
    /// </summary>
    public string? Name { get; init; }

    /// <summary>
    /// Description of the model's capabilities.
    /// </summary>
    public string? Description { get; init; }

    /// <summary>
    /// Maximum number of tokens the model can process.
    /// </summary>
    public int? MaxTokens { get; init; }

    /// <summary>
    /// Whether the model supports function/tool calling.
    /// </summary>
    public bool SupportsFunctions { get; init; }

    /// <summary>
    /// Whether the model supports streaming responses.
    /// </summary>
    public bool SupportsStreaming { get; init; } = true;

    /// <summary>
    /// The date when this model was released or last updated.
    /// </summary>
    public DateTimeOffset? UpdatedAt { get; init; }

    /// <summary>
    /// Additional metadata about the model.
    /// </summary>
    public Dictionary<string, object> Metadata { get; init; } = new();

    // Additional properties for backward compatibility

    /// <summary>
    /// The provider of this model (e.g., "OpenAI", "Azure", "Ollama").
    /// </summary>
    public string? Provider { get; init; }

    /// <summary>
    /// Model family (e.g., "gpt-4", "claude", "llama").
    /// </summary>
    public string? Family { get; init; }

    /// <summary>
    /// Parameter size (e.g., "7B", "13B", "175B").
    /// </summary>
    public string? ParameterSize { get; init; }

    /// <summary>
    /// Whether the model supports vision/image inputs.
    /// </summary>
    public bool SupportsVision { get; init; }

    /// <summary>
    /// Date when the model was created (alias for UpdatedAt).
    /// </summary>
    public DateTimeOffset? Created => UpdatedAt;

    /// <summary>
    /// Whether this is a fine-tuned model.
    /// </summary>
    public bool IsFineTuned { get; init; }
}