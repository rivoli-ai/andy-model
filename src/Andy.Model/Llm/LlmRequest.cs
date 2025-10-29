using Andy.Model.Model;
using Andy.Model.Tooling;

namespace Andy.Model.Llm;

/// <summary>
/// Request structure for LLM completions.
/// </summary>
public sealed class LlmRequest
{
    /// <summary>
    /// The conversation context messages.
    /// </summary>
    public required IReadOnlyList<Message> Messages { get; init; }

    /// <summary>
    /// Available tools/functions that can be called.
    /// </summary>
    public IReadOnlyList<ToolDeclaration> Tools { get; init; } = Array.Empty<ToolDeclaration>();

    /// <summary>
    /// Optional configuration for the request.
    /// </summary>
    public LlmClientConfig? Config { get; init; }

    /// <summary>
    /// System prompt to prepend to the conversation.
    /// </summary>
    public string? SystemPrompt { get; init; }

    // Convenience properties that delegate to Config for backward compatibility

    /// <summary>
    /// Model to use for completion (delegates to Config.Model).
    /// </summary>
    public string Model => Config?.Model ?? string.Empty;

    /// <summary>
    /// Temperature for sampling (delegates to Config.Temperature).
    /// Returns null if not configured, allowing models to use their own defaults.
    /// </summary>
    public decimal? Temperature => Config?.Temperature;

    /// <summary>
    /// Maximum tokens to generate (delegates to Config.MaxTokens).
    /// </summary>
    public int MaxTokens => Config?.MaxTokens ?? 4000;

    /// <summary>
    /// Top-p sampling parameter (delegates to Config.TopP).
    /// </summary>
    public decimal TopP => Config?.TopP ?? 1.0m;
}