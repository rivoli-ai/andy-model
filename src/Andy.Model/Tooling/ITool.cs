using Andy.Model.Model;

namespace Andy.Model.Tooling;

/// <summary>
/// Implement to provide tool execution. Arguments arrive as JsonElement.
/// </summary>
public interface ITool
{
    ToolDeclaration Definition { get; }
    Task<ToolResult> ExecuteAsync(ToolCall call, CancellationToken ct = default);
}