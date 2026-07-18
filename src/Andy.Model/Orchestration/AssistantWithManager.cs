using System.Diagnostics;
using System.Runtime.CompilerServices;
using Andy.Model.Conversation;
using Andy.Model.Llm;
using Andy.Model.Model;
using Andy.Model.Tooling;
using Andy.Model.Utils;

namespace Andy.Model.Orchestration;

/// <summary>
/// Enhanced Assistant that uses <see cref="IConversationManager"/> for flexible
/// conversation handling. Behavior mirrors <see cref="Assistant"/> exactly: bounded
/// multi-step tool loops, complete protocol-ordered history, and identical streaming
/// and non-streaming semantics.
/// </summary>
public sealed class AssistantWithManager : IAssistantEventSink
{
    private readonly IConversationManager _conversationManager;
    private readonly ToolRegistry _tools;
    private readonly ILlmProvider _llm;
    private readonly AssistantOptions _options;
    private readonly ToolExecutionEngine _engine;

    // Events (same as original Assistant)
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

    /// <summary>
    /// Create assistant with a specific conversation manager.
    /// </summary>
    public AssistantWithManager(IConversationManager conversationManager, ToolRegistry tools, ILlmProvider llm, AssistantOptions? options = null)
    {
        _conversationManager = conversationManager ?? throw new ArgumentNullException(nameof(conversationManager));
        _tools = tools ?? throw new ArgumentNullException(nameof(tools));
        _llm = llm ?? throw new ArgumentNullException(nameof(llm));
        _options = options ?? AssistantOptions.Default;
        _options.Validate();
        _engine = new ToolExecutionEngine(_tools, _options, this);
    }

    /// <summary>
    /// Create assistant with default conversation manager.
    /// </summary>
    public AssistantWithManager(ToolRegistry tools, ILlmProvider llm, ConversationManagerOptions? options = null, AssistantOptions? assistantOptions = null)
        : this(new DefaultConversationManager(options), tools, llm, assistantOptions)
    {
    }

    /// <summary>
    /// Access the underlying conversation.
    /// </summary>
    public Model.Conversation Conversation => _conversationManager.Conversation;

    /// <summary>
    /// Access the conversation manager for advanced operations.
    /// </summary>
    public IConversationManager ConversationManager => _conversationManager;

    public async Task<Message> RunTurnAsync(string userText, CancellationToken ct = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var counts = new ToolOutcomeCounts();

        TurnStarted?.Invoke(this, new TurnStartedEventArgs
        {
            ConversationId = Conversation.Id,
            UserMessage = userText,
            TurnNumber = Conversation.Turns.Count + 1
        });

        var turn = new Turn
        {
            UserOrSystemMessage = new Message { Role = Role.User, Content = userText }
        };
        _conversationManager.AddTurn(turn);

        var tools = _tools.GetDeclaredTools();
        Message finalAssistant;

        try
        {
            while (true)
            {
                var messages = _conversationManager.ExtractMessagesForNextTurn().ToArray();

                LlmRequestStarted?.Invoke(this, new LlmRequestStartedEventArgs
                {
                    ConversationId = Conversation.Id,
                    MessageCount = messages.Length,
                    ToolCount = tools.Length,
                    IsRetryAfterTools = counts.Rounds > 0
                });

                var request = new LlmRequest { Messages = messages, Tools = tools };
                var response = await _llm.CompleteAsync(request, ct).ConfigureAwait(false);
                var assistantMessage = response.AssistantMessage;

                turn.AddAssistantMessage(assistantMessage);

                LlmResponseReceived?.Invoke(this, new LlmResponseReceivedEventArgs
                {
                    ConversationId = Conversation.Id,
                    Response = assistantMessage,
                    Usage = response.Usage,
                    HasToolCalls = response.HasToolCalls
                });

                if (!response.HasToolCalls)
                {
                    finalAssistant = assistantMessage;
                    break;
                }

                if (counts.Rounds >= _options.MaxToolIterations)
                {
                    if (_options.OnToolIterationLimit == ToolIterationLimitBehavior.Throw)
                    {
                        throw new ToolIterationLimitException(_options.MaxToolIterations);
                    }

                    ToolIterationLimitReached?.Invoke(this, new ToolIterationLimitReachedEventArgs
                    {
                        ConversationId = Conversation.Id,
                        MaxToolIterations = _options.MaxToolIterations,
                        PendingToolCalls = assistantMessage.ToolCalls.ToArray()
                    });
                    finalAssistant = assistantMessage;
                    break;
                }

                var roundCounts = await _engine.ExecuteRoundAsync(
                    assistantMessage.ToolCalls, tools, turn, Conversation.Id, ct).ConfigureAwait(false);
                counts.Add(roundCounts);
            }

            // Automatic compaction: single owner, awaited, honors AutoCompact and exceptions.
            await _conversationManager.CompactIfNeededAsync(ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke(this, new ErrorOccurredEventArgs
            {
                ConversationId = Conversation.Id,
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
    /// Run a streaming turn for real-time responses. See <see cref="Assistant.RunTurnStreamAsync"/>
    /// for the accumulation and persistence semantics, which are identical here.
    /// </summary>
    public async IAsyncEnumerable<Message> RunTurnStreamAsync(string userText, [EnumeratorCancellation] CancellationToken ct = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var counts = new ToolOutcomeCounts();

        TurnStarted?.Invoke(this, new TurnStartedEventArgs
        {
            ConversationId = Conversation.Id,
            UserMessage = userText,
            TurnNumber = Conversation.Turns.Count + 1
        });

        var turn = new Turn
        {
            UserOrSystemMessage = new Message { Role = Role.User, Content = userText }
        };
        _conversationManager.AddTurn(turn);

        var tools = _tools.GetDeclaredTools();
        Message finalAssistant = new() { Role = Role.Assistant, Content = string.Empty };

        while (true)
        {
            var messages = _conversationManager.ExtractMessagesForNextTurn().ToArray();

            LlmRequestStarted?.Invoke(this, new LlmRequestStartedEventArgs
            {
                ConversationId = Conversation.Id,
                MessageCount = messages.Length,
                ToolCount = tools.Length,
                IsRetryAfterTools = counts.Rounds > 0
            });

            var request = new LlmRequest { Messages = messages, Tools = tools };
            var accumulator = new StreamingResponseAccumulator();

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
                            ConversationId = Conversation.Id,
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
                            ConversationId = Conversation.Id,
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

            var assistantMessage = accumulator.Build();
            turn.AddAssistantMessage(assistantMessage);
            finalAssistant = assistantMessage;

            LlmResponseReceived?.Invoke(this, new LlmResponseReceivedEventArgs
            {
                ConversationId = Conversation.Id,
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
                        ConversationId = Conversation.Id,
                        Exception = new ToolIterationLimitException(_options.MaxToolIterations),
                        Context = "RunTurnStreamAsync",
                        IsCritical = true
                    });
                    throw new ToolIterationLimitException(_options.MaxToolIterations);
                }

                ToolIterationLimitReached?.Invoke(this, new ToolIterationLimitReachedEventArgs
                {
                    ConversationId = Conversation.Id,
                    MaxToolIterations = _options.MaxToolIterations,
                    PendingToolCalls = assistantMessage.ToolCalls.ToArray()
                });
                break;
            }

            var roundCounts = await _engine.ExecuteRoundAsync(
                assistantMessage.ToolCalls, tools, turn, Conversation.Id, ct).ConfigureAwait(false);
            counts.Add(roundCounts);
        }

        await _conversationManager.CompactIfNeededAsync(ct).ConfigureAwait(false);

        stopwatch.Stop();
        RaiseTurnCompleted(finalAssistant, counts, stopwatch.Elapsed);
    }

    /// <summary>
    /// Get a summary of the conversation.
    /// </summary>
    public async Task<string> GetConversationSummaryAsync()
    {
        return await _conversationManager.GetConversationSummaryAsync();
    }

    /// <summary>
    /// Manually trigger conversation compaction.
    /// </summary>
    public async Task<bool> CompactConversationAsync()
    {
        return await _conversationManager.CompactConversationAsync();
    }

    /// <summary>
    /// Get conversation statistics.
    /// </summary>
    public ConversationStats GetConversationStats()
    {
        return _conversationManager.GetStatistics();
    }

    private void RaiseTurnCompleted(Message finalAssistant, in ToolOutcomeCounts counts, TimeSpan duration)
    {
        TurnCompleted?.Invoke(this, new TurnCompletedEventArgs
        {
            ConversationId = Conversation.Id,
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
