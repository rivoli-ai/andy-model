using System.Text.Json;
using Andy.Model.Llm;
using Andy.Model.Model;
using Andy.Model.Utils;

namespace Andy.Model.Examples;

/// <summary>
/// A minimal demo LLM provider that interprets the user's last message.
/// - If it contains the word "calc", it emits a tool call to Calculator.
/// - Otherwise, it echoes a generic assistant message.
/// This lets you exercise the orchestration without any vendor SDKs.
/// </summary>
public sealed class DemoLlmClient : ILlmProvider
{
    public string Name => "Demo Provider";

    public Task<LlmResponse> CompleteAsync(LlmRequest request, CancellationToken cancellationToken = default)
    {
        var context = request.Messages;
        var declaredTools = request.Tools;
        var lastUser = context.LastOrDefault(m => m.Role == Role.User)?.Content ?? string.Empty;

        // If we see tool messages with results, produce a final answer that references them.
        var lastTool = context.LastOrDefault(m => m.Role == Role.Tool);
        if (lastTool != null)
        {
            var reply = new Message
            {
                Role = Role.Assistant,
                Content = $"Tool result received: {Truncate(lastTool.Content, 160)}"
            };
            return Task.FromResult(new LlmResponse { AssistantMessage = reply });
        }

        if (lastUser.Contains("calc", StringComparison.OrdinalIgnoreCase))
        {
            // Emit a tool call asking the Calculator to evaluate an expression.
            var call = new ToolCall
            {
                Id = Guid.NewGuid().ToString(),
                Name = "calculator",
                ArgumentsJson = JsonSerializer.Serialize(new { expression = "2+2*3" }, JsonOptions.Default)
            };
            var reply = new Message
            {
                Role = Role.Assistant,
                Content = "Calling calculator...",
                ToolCalls = new List<ToolCall> { call }
            };
            return Task.FromResult(new LlmResponse { AssistantMessage = reply });
        }
        else
        {
            var reply = new Message
            {
                Role = Role.Assistant,
                Content = "Hello! (demo LLM): Ask me to `calc` something to see tool calling."
            };
            return Task.FromResult(new LlmResponse { AssistantMessage = reply });
        }
    }

    public async IAsyncEnumerable<LlmStreamResponse> StreamCompleteAsync(LlmRequest request, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        // For demo purposes, just yield the non-streaming response
        var response = await CompleteAsync(request, cancellationToken);
        yield return new LlmStreamResponse
        {
            Delta = response.AssistantMessage,
            IsComplete = true,
            Usage = response.Usage
        };
    }

    private static string Truncate(string s, int max)
    {
        if (string.IsNullOrEmpty(s) || s.Length <= max) return s;
        return s.Substring(0, Math.Max(0, max - 3)) + "...";
    }

    public Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default)
    {
        // Demo provider is always available
        return Task.FromResult(true);
    }

    public Task<IEnumerable<ModelInfo>> ListModelsAsync(CancellationToken cancellationToken = default)
    {
        // Return a single demo model
        var models = new[]
        {
            new ModelInfo
            {
                Id = "demo-model-1",
                Name = "Demo Model",
                Description = "A demonstration model for testing",
                MaxTokens = 4096,
                SupportsFunctions = true,
                SupportsStreaming = true,
                UpdatedAt = DateTimeOffset.UtcNow
            }
        };

        return Task.FromResult<IEnumerable<ModelInfo>>(models);
    }
}