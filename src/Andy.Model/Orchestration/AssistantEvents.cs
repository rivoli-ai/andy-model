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
    public int ToolCallsExecuted { get; init; }
    public TimeSpan Duration { get; init; }
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