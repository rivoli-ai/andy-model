using System;
using Andy.Model.Llm;
using Andy.Model.Model;

namespace Andy.Model.Orchestration;

/// <summary>
/// Base class for Assistant events
/// </summary>
public abstract class AssistantEventArgs : EventArgs
{
    public DateTimeOffset Timestamp { get; } = DateTimeOffset.Now;
    public string ConversationId { get; init; } = string.Empty;
}

/// <summary>
/// Event raised when a turn starts
/// </summary>
public class TurnStartedEventArgs : AssistantEventArgs
{
    public string UserMessage { get; init; } = string.Empty;
    public int TurnNumber { get; init; }
}

/// <summary>
/// Event raised when a turn completes
/// </summary>
public class TurnCompletedEventArgs : AssistantEventArgs
{
    public Message AssistantMessage { get; init; } = null!;

    /// <summary>
    /// Number of tool calls that were actually invoked (i.e. dispatched to a
    /// registered tool), regardless of whether they succeeded or returned an error.
    /// Equivalent to <see cref="ToolCallsSucceeded"/> + <see cref="ToolCallsFailed"/>.
    /// Validation-rejected and missing-tool calls are excluded.
    /// </summary>
    public int ToolCallsExecuted { get; init; }

    /// <summary>Total tool calls the provider requested across every round of the turn.</summary>
    public int ToolCallsAttempted { get; init; }

    /// <summary>Tool calls that were invoked and returned a non-error result.</summary>
    public int ToolCallsSucceeded { get; init; }

    /// <summary>Tool calls that were invoked but threw or returned an error result.</summary>
    public int ToolCallsFailed { get; init; }

    /// <summary>Tool calls rejected by schema validation before invocation.</summary>
    public int ToolCallsValidationRejected { get; init; }

    /// <summary>Tool calls whose named tool was not found in the registry.</summary>
    public int ToolCallsNotFound { get; init; }

    /// <summary>Number of tool-execution rounds performed within the turn.</summary>
    public int ToolRounds { get; init; }

    public TimeSpan Duration { get; init; }
}

/// <summary>
/// Event raised when the bounded tool-call loop reaches its iteration limit while
/// the provider is still requesting tool calls.
/// </summary>
public class ToolIterationLimitReachedEventArgs : AssistantEventArgs
{
    public int MaxToolIterations { get; init; }

    /// <summary>The pending tool calls that were not executed because the limit was hit.</summary>
    public ToolCall[] PendingToolCalls { get; init; } = Array.Empty<ToolCall>();
}

/// <summary>
/// Event raised before calling the LLM
/// </summary>
public class LlmRequestStartedEventArgs : AssistantEventArgs
{
    public int MessageCount { get; init; }
    public int ToolCount { get; init; }
    public bool IsRetryAfterTools { get; init; }
}

/// <summary>
/// Event raised after LLM responds
/// </summary>
public class LlmResponseReceivedEventArgs : AssistantEventArgs
{
    public Message Response { get; init; } = null!;
    public LlmUsage? Usage { get; init; }
    public bool HasToolCalls { get; init; }
}

/// <summary>
/// Event raised when streaming tokens from LLM
/// </summary>
public class StreamingTokenReceivedEventArgs : AssistantEventArgs
{
    public Message Delta { get; init; } = null!;
    public bool IsComplete { get; init; }
}

/// <summary>
/// Event raised before executing a tool
/// </summary>
public class ToolExecutionStartedEventArgs : AssistantEventArgs
{
    public ToolCall ToolCall { get; init; } = null!;
    public string ToolName { get; init; } = string.Empty;
}

/// <summary>
/// Event raised after tool execution
/// </summary>
public class ToolExecutionCompletedEventArgs : AssistantEventArgs
{
    public ToolCall ToolCall { get; init; } = null!;
    public ToolResult Result { get; init; } = null!;
    public bool IsError { get; init; }
    public TimeSpan Duration { get; init; }
}

/// <summary>
/// Event raised when a tool is not found
/// </summary>
public class ToolNotFoundEventArgs : AssistantEventArgs
{
    public string ToolName { get; init; } = string.Empty;
    public string CallId { get; init; } = string.Empty;
    public string[] AvailableTools { get; init; } = Array.Empty<string>();
}

/// <summary>
/// Event raised when tool validation fails
/// </summary>
public class ToolValidationFailedEventArgs : AssistantEventArgs
{
    public ToolCall ToolCall { get; init; } = null!;
    public string[] ValidationErrors { get; init; } = Array.Empty<string>();
}

/// <summary>
/// Event raised when an error occurs
/// </summary>
public class ErrorOccurredEventArgs : AssistantEventArgs
{
    public Exception Exception { get; init; } = null!;
    public string Context { get; init; } = string.Empty;
    public bool IsCritical { get; init; }
}