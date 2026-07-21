using System.Diagnostics;
using Andy.Model.Model;
using Andy.Model.Tooling;

namespace Andy.Model.Orchestration;

/// <summary>
/// Aggregated tool-call outcome counts for a turn. See
/// <see cref="TurnCompletedEventArgs"/> for the semantics of each field.
/// </summary>
internal struct ToolOutcomeCounts
{
    public int Attempted;
    public int Succeeded;
    public int Failed;
    public int ValidationRejected;
    public int NotFound;
    public int Rounds;

    public readonly int Executed => Succeeded + Failed;

    public void Add(in ToolOutcomeCounts other)
    {
        Attempted += other.Attempted;
        Succeeded += other.Succeeded;
        Failed += other.Failed;
        ValidationRejected += other.ValidationRejected;
        NotFound += other.NotFound;
        Rounds += other.Rounds;
    }
}

/// <summary>
/// Sink through which the shared <see cref="ToolExecutionEngine"/> raises the
/// tool-lifecycle events owned by each concrete assistant. Keeping this behind an
/// interface lets both <see cref="Assistant"/> and <see cref="AssistantWithManager"/>
/// share one execution path so their behavior is identical.
/// </summary>
internal interface IAssistantEventSink
{
    void RaiseToolExecutionStarted(ToolExecutionStartedEventArgs e);
    void RaiseToolExecutionCompleted(ToolExecutionCompletedEventArgs e);
    void RaiseToolNotFound(ToolNotFoundEventArgs e);
    void RaiseToolValidationFailed(ToolValidationFailedEventArgs e);
}

/// <summary>
/// Shared implementation of per-round tool execution: schema validation, dispatch,
/// error/cancellation handling, event raising, and outcome counting. Used by both
/// assistant implementations to guarantee identical streaming and non-streaming behavior.
/// </summary>
internal sealed class ToolExecutionEngine
{
    private readonly ToolRegistry _tools;
    private readonly AssistantOptions _options;
    private readonly IAssistantEventSink _sink;

    public ToolExecutionEngine(ToolRegistry tools, AssistantOptions options, IAssistantEventSink sink)
    {
        _tools = tools;
        _options = options;
        _sink = sink;
    }

    /// <summary>
    /// Execute every tool call in <paramref name="calls"/>, appending one tool-result
    /// message per call to <paramref name="turn"/> in order. Raises lifecycle events
    /// and returns the outcome counts for this round.
    /// </summary>
    /// <remarks>
    /// Cancellation tied to <paramref name="ct"/> propagates immediately (it is never
    /// converted into a tool-error result). An <see cref="OperationCanceledException"/>
    /// that is <em>not</em> associated with <paramref name="ct"/> (for example a tool's
    /// own internal timeout) is treated as an ordinary tool error.
    /// </remarks>
    public async Task<ToolOutcomeCounts> ExecuteRoundAsync(
        IReadOnlyList<ToolCall> calls,
        ToolDeclaration[] tools,
        Turn turn,
        string conversationId,
        CancellationToken ct)
    {
        var counts = new ToolOutcomeCounts { Rounds = 1 };

        foreach (var call in calls)
        {
            ct.ThrowIfCancellationRequested();
            counts.Attempted++;

            // 1) Schema validation against the tool declaration.
            var toolDef = tools.FirstOrDefault(t => t.Name.Equals(call.Name, StringComparison.OrdinalIgnoreCase));
            if (toolDef != null)
            {
                var validation = ToolCallValidator.Validate(call, toolDef);
                if (!validation.IsValid)
                {
                    counts.ValidationRejected++;
                    _sink.RaiseToolValidationFailed(new ToolValidationFailedEventArgs
                    {
                        ConversationId = conversationId,
                        ToolCall = call,
                        ValidationErrors = validation.Errors.ToArray()
                    });

                    var validationError = ToolResult.FromObject(call.Id, call.Name,
                        new { error = "validation_failed", tool_name = call.Name, call_id = call.Id, details = validation.Errors },
                        isError: true);
                    turn.AddToolMessage(BuildToolMessage(call, validationError, extraMetadata: ("validation_error", true)));
                    continue;
                }
            }

            // 2) Dispatch to the registered tool, if any.
            if (_tools.TryGet(call.Name, out var tool))
            {
                _sink.RaiseToolExecutionStarted(new ToolExecutionStartedEventArgs
                {
                    ConversationId = conversationId,
                    ToolCall = call,
                    ToolName = call.Name
                });

                var toolStopwatch = Stopwatch.StartNew();
                ToolResult result;
                try
                {
                    result = await tool.ExecuteAsync(call, ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    // Cancellation of the active token: propagate immediately.
                    throw;
                }
                catch (ToolExecutionException ex)
                {
                    result = BuildErrorResult(call, ex, ex.GetType().Name);
                }
                catch (OperationCanceledException ex)
                {
                    // Unrelated cancellation (e.g. a tool's own timeout token): treat as an ordinary tool error.
                    result = BuildErrorResult(call, ex, ex.GetType().Name);
                }
                catch (Exception ex)
                {
                    result = BuildErrorResult(call, ex, ex.GetType().Name);
                }
                toolStopwatch.Stop();

                if (result.IsError) counts.Failed++;
                else counts.Succeeded++;

                _sink.RaiseToolExecutionCompleted(new ToolExecutionCompletedEventArgs
                {
                    ConversationId = conversationId,
                    ToolCall = call,
                    Result = result,
                    IsError = result.IsError,
                    Duration = toolStopwatch.Elapsed
                });

                turn.AddToolMessage(BuildToolMessage(call, result, extraMetadata: ("is_error", result.IsError)));
            }
            else
            {
                counts.NotFound++;
                _sink.RaiseToolNotFound(new ToolNotFoundEventArgs
                {
                    ConversationId = conversationId,
                    ToolName = call.Name,
                    CallId = call.Id,
                    AvailableTools = _tools.GetRegisteredToolNames().ToArray()
                });

                var notFound = ToolResult.FromObject(call.Id, call.Name,
                    new { error = "tool_not_found", tool_name = call.Name, call_id = call.Id, available_tools = _tools.GetRegisteredToolNames() },
                    isError: true);
                turn.AddToolMessage(BuildToolMessage(call, notFound, extraMetadata: ("tool_not_found", true)));
            }
        }

        return counts;
    }

    private ToolResult BuildErrorResult(ToolCall call, Exception ex, string exceptionType)
    {
        object payload = _options.IncludeExceptionDetailsInToolErrors
            ? new { error = "tool_execution_failed", tool_name = call.Name, call_id = call.Id, exception_type = exceptionType, message = ex.Message }
            : new { error = "tool_execution_failed", tool_name = call.Name, call_id = call.Id };
        return ToolResult.FromObject(call.Id, call.Name, payload, isError: true);
    }

    private static Message BuildToolMessage(ToolCall call, ToolResult result, (string Key, object Value) extraMetadata)
    {
        return new Message
        {
            Role = Role.Tool,
            Content = result.ResultJson,
            ToolCallId = call.Id,
            ToolResults = new List<ToolResult> { result },
            Metadata = new Dictionary<string, object>
            {
                ["tool_name"] = call.Name,
                ["tool_call_id"] = call.Id,
                [extraMetadata.Key] = extraMetadata.Value
            }
        };
    }
}
