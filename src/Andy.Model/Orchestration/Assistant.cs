using System.Diagnostics;
using System.Runtime.CompilerServices;
using Andy.Model.Llm;
using Andy.Model.Model;
using Andy.Model.Tooling;

namespace Andy.Model.Orchestration;

#region Orchestration Engine

/// <summary>
/// Orchestrates: build context → call LLM → run tools → append results → call LLM again,
/// looping until the provider returns no further tool calls (bounded by
/// <see cref="AssistantOptions.MaxToolIterations"/>). Every user, assistant tool-call,
/// tool-result, and final-assistant message is retained as a distinct message in
/// provider-valid protocol order.
/// </summary>
public sealed class Assistant : IAssistantEventSink
{
    private readonly Model.Conversation _conversation;
    private readonly ToolRegistry _tools;
    private readonly ILlmProvider _llm;
    private readonly AssistantOptions _options;
    private readonly ToolExecutionEngine _engine;

    // Events
    public event EventHandler<TurnStartedEventArgs>? TurnStarted;
    public event EventHandler<TurnCompletedEventArgs>? TurnCompleted;
    public event EventHandler<LlmRequestStartedEventArgs>? LlmRequestStarted;
    public event EventHandler<LlmResponseReceivedEventArgs>? LlmResponseReceived;
    public event EventHandler<StreamingTokenReceivedEventArgs>? StreamingTokenReceived;
    public event EventHandler<ToolExecutionStartedEventArgs>? ToolExecutionStarted;
    public event EventHandler<ToolExecutionCompletedEventArgs>? ToolExecutionCompleted;
    public event EventHandler<ToolNotFoundEventArgs>? ToolNotFound;
    public event EventHandler<ToolValidationFailedEventArgs>? ToolValidationFailed;
    public event EventHandler<ToolIterationLimitReachedEventArgs>? ToolIterationLimitReached;
    public event EventHandler<ErrorOccurredEventArgs>? ErrorOccurred;

    public Assistant(Model.Conversation conversation, ToolRegistry tools, ILlmProvider llm, AssistantOptions? options = null)
    {
        _conversation = conversation;
        _tools = tools;
        _llm = llm;
        _options = options ?? AssistantOptions.Default;
        _options.Validate();
        _engine = new ToolExecutionEngine(_tools, _options, this);
    }

    /// <summary>The conversation this assistant appends turns to.</summary>
    public Model.Conversation Conversation => _conversation;

    public async Task<Message> RunTurnAsync(string userText, CancellationToken ct = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var counts = new ToolOutcomeCounts();

        // Fire turn started event
        TurnStarted?.Invoke(this, new TurnStartedEventArgs
        {
            ConversationId = _conversation.Id,
            UserMessage = userText,
            TurnNumber = _conversation.Turns.Count + 1
        });

        // 1) Record user message as a new turn.
        var turn = new Turn
        {
            UserOrSystemMessage = new Message { Role = Role.User, Content = userText }
        };
        _conversation.AddTurn(turn);

        var tools = _tools.GetDeclaredTools();
        Message finalAssistant;

        try
        {
            // 2) Bounded LLM → tools loop.
            while (true)
            {
                var messages = _conversation.ToChronoMessages().ToArray();

                LlmRequestStarted?.Invoke(this, new LlmRequestStartedEventArgs
                {
                    ConversationId = _conversation.Id,
                    MessageCount = messages.Length,
                    ToolCount = tools.Length,
                    IsRetryAfterTools = counts.Rounds > 0
                });

                var request = new LlmRequest { Messages = messages, Tools = tools };
                var response = await _llm.CompleteAsync(request, ct).ConfigureAwait(false);
                var assistantMessage = response.AssistantMessage;

                // Store the assistant message (with any tool calls) in protocol order.
                turn.AddAssistantMessage(assistantMessage);

                LlmResponseReceived?.Invoke(this, new LlmResponseReceivedEventArgs
                {
                    ConversationId = _conversation.Id,
                    Response = assistantMessage,
                    Usage = response.Usage,
                    HasToolCalls = response.HasToolCalls
                });

                if (!response.HasToolCalls)
                {
                    finalAssistant = assistantMessage;
                    break;
                }

                // Tool calls pending — enforce the iteration limit before executing more.
                if (counts.Rounds >= _options.MaxToolIterations)
                {
                    if (_options.OnToolIterationLimit == ToolIterationLimitBehavior.Throw)
                    {
                        throw new ToolIterationLimitException(_options.MaxToolIterations);
                    }

                    ToolIterationLimitReached?.Invoke(this, new ToolIterationLimitReachedEventArgs
                    {
                        ConversationId = _conversation.Id,
                        MaxToolIterations = _options.MaxToolIterations,
                        PendingToolCalls = assistantMessage.ToolCalls.ToArray()
                    });
                    finalAssistant = assistantMessage;
                    break;
                }

                var roundCounts = await _engine.ExecuteRoundAsync(
                    assistantMessage.ToolCalls, tools, turn, _conversation.Id, ct).ConfigureAwait(false);
                counts.Add(roundCounts);
            }
        }
        catch (OperationCanceledException)
        {
            // Cancellation terminates orchestration; it is never treated as an error result.
            throw;
        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke(this, new ErrorOccurredEventArgs
            {
                ConversationId = _conversation.Id,
                Exception = ex,
                Context = "RunTurnAsync",
                IsCritical = true
            });
            throw;
        }

        stopwatch.Stop();
        RaiseTurnCompleted(finalAssistant, counts, stopwatch.Elapsed);
        return finalAssistant;
    }

    /// <summary>
    /// Run a streaming turn for real-time responses. Text deltas are accumulated into a
    /// single persisted assistant message; fragmented tool-call deltas are merged by call
    /// identity. The assistant tool-call message is stored before tool results and before
    /// the follow-up request, and the accumulated final response is stored after tool results.
    /// </summary>
    public async IAsyncEnumerable<Message> RunTurnStreamAsync(string userText, [EnumeratorCancellation] CancellationToken ct = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var counts = new ToolOutcomeCounts();

        TurnStarted?.Invoke(this, new TurnStartedEventArgs
        {
            ConversationId = _conversation.Id,
            UserMessage = userText,
            TurnNumber = _conversation.Turns.Count + 1
        });

        var turn = new Turn
        {
            UserOrSystemMessage = new Message { Role = Role.User, Content = userText }
        };
        _conversation.AddTurn(turn);

        var tools = _tools.GetDeclaredTools();
        Message finalAssistant = new() { Role = Role.Assistant, Content = string.Empty };

        while (true)
        {
            var messages = _conversation.ToChronoMessages().ToArray();

            LlmRequestStarted?.Invoke(this, new LlmRequestStartedEventArgs
            {
                ConversationId = _conversation.Id,
                MessageCount = messages.Length,
                ToolCount = tools.Length,
                IsRetryAfterTools = counts.Rounds > 0
            });

            var request = new LlmRequest { Messages = messages, Tools = tools };
            var accumulator = new StreamingResponseAccumulator();

            // Consume the stream. Errors fire the same lifecycle event as non-streaming.
            var enumerator = _llm.StreamCompleteAsync(request, ct).GetAsyncEnumerator(ct);
            try
            {
                while (true)
                {
                    bool moved;
                    try
                    {
                        moved = await enumerator.MoveNextAsync().ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        ErrorOccurred?.Invoke(this, new ErrorOccurredEventArgs
                        {
                            ConversationId = _conversation.Id,
                            Exception = ex,
                            Context = "RunTurnStreamAsync",
                            IsCritical = true
                        });
                        throw;
                    }

                    if (!moved) break;

                    var chunk = enumerator.Current;
                    if (chunk.Delta != null)
                    {
                        accumulator.AddDelta(chunk.Delta);
                        StreamingTokenReceived?.Invoke(this, new StreamingTokenReceivedEventArgs
                        {
                            ConversationId = _conversation.Id,
                            Delta = chunk.Delta,
                            IsComplete = chunk.IsComplete
                        });
                        yield return chunk.Delta;
                    }
                }
            }
            finally
            {
                await enumerator.DisposeAsync().ConfigureAwait(false);
            }

            // Persist the accumulated assistant message before any tool execution.
            var assistantMessage = accumulator.Build();
            turn.AddAssistantMessage(assistantMessage);
            finalAssistant = assistantMessage;

            LlmResponseReceived?.Invoke(this, new LlmResponseReceivedEventArgs
            {
                ConversationId = _conversation.Id,
                Response = assistantMessage,
                Usage = null,
                HasToolCalls = accumulator.HasToolCalls
            });

            if (!accumulator.HasToolCalls)
            {
                break;
            }

            if (counts.Rounds >= _options.MaxToolIterations)
            {
                if (_options.OnToolIterationLimit == ToolIterationLimitBehavior.Throw)
                {
                    ErrorOccurred?.Invoke(this, new ErrorOccurredEventArgs
                    {
                        ConversationId = _conversation.Id,
                        Exception = new ToolIterationLimitException(_options.MaxToolIterations),
                        Context = "RunTurnStreamAsync",
                        IsCritical = true
                    });
                    throw new ToolIterationLimitException(_options.MaxToolIterations);
                }

                ToolIterationLimitReached?.Invoke(this, new ToolIterationLimitReachedEventArgs
                {
                    ConversationId = _conversation.Id,
                    MaxToolIterations = _options.MaxToolIterations,
                    PendingToolCalls = assistantMessage.ToolCalls.ToArray()
                });
                break;
            }

            var roundCounts = await _engine.ExecuteRoundAsync(
                assistantMessage.ToolCalls, tools, turn, _conversation.Id, ct).ConfigureAwait(false);
            counts.Add(roundCounts);
        }

        stopwatch.Stop();
        RaiseTurnCompleted(finalAssistant, counts, stopwatch.Elapsed);
    }

    private void RaiseTurnCompleted(Message finalAssistant, in ToolOutcomeCounts counts, TimeSpan duration)
    {
        TurnCompleted?.Invoke(this, new TurnCompletedEventArgs
        {
            ConversationId = _conversation.Id,
            AssistantMessage = finalAssistant,
            ToolCallsExecuted = counts.Executed,
            ToolCallsAttempted = counts.Attempted,
            ToolCallsSucceeded = counts.Succeeded,
            ToolCallsFailed = counts.Failed,
            ToolCallsValidationRejected = counts.ValidationRejected,
            ToolCallsNotFound = counts.NotFound,
            ToolRounds = counts.Rounds,
            Duration = duration
        });
    }

    // IAssistantEventSink — forwards shared-engine events to this instance's public events.
    void IAssistantEventSink.RaiseToolExecutionStarted(ToolExecutionStartedEventArgs e) => ToolExecutionStarted?.Invoke(this, e);
    void IAssistantEventSink.RaiseToolExecutionCompleted(ToolExecutionCompletedEventArgs e) => ToolExecutionCompleted?.Invoke(this, e);
    void IAssistantEventSink.RaiseToolNotFound(ToolNotFoundEventArgs e) => ToolNotFound?.Invoke(this, e);
    void IAssistantEventSink.RaiseToolValidationFailed(ToolValidationFailedEventArgs e) => ToolValidationFailed?.Invoke(this, e);
}

#endregion
