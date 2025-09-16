namespace Andy.Model.Llm;

/// <summary>
/// LLM usage information (tokens, costs, etc.).
/// </summary>
public sealed class LlmUsage
{
    public int PromptTokens { get; init; }
    public int CompletionTokens { get; init; }
    public int TotalTokens { get; init; }
}