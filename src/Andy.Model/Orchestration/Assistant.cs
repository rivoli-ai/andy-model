
using System.Diagnostics;
using Andy.Model.Llm;
using Andy.Model.Model;
using Andy.Model.Tooling;

namespace Andy.Model.Orchestration;

#region Orchestration Engine

/// <summary>
/// Orchestrates: build context → call LLM → run tools → append results → (optionally) call LLM again.
/// </summary>
public sealed class Assistant
{
    private readonly Conversation _conversation;
    private readonly ToolRegistry _tools;
    private readonly ILlmProvider _llm;

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
    public event EventHandler<ErrorOccurredEventArgs>? ErrorOccurred;

    public Assistant(Conversation conversation, ToolRegistry tools, ILlmProvider llm)
    {
        _conversation = conversation;
        _tools = tools;
        _llm = llm;
    }

    public async Task<Message> RunTurnAsync(string userText, CancellationToken ct = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var toolCallsExecuted = 0;

        try
        {
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

            // 2) Build context and call LLM.
            var messages = _conversation.ToChronoMessages().ToArray();
            var tools = _tools.GetDeclaredTools();

            // Fire LLM request started event
            LlmRequestStarted?.Invoke(this, new LlmRequestStartedEventArgs
            {
                ConversationId = _conversation.Id,
                MessageCount = messages.Length,
                ToolCount = tools.Length,
                IsRetryAfterTools = false
            });

            var request = new LlmRequest { Messages = messages, Tools = tools };
            var response = await _llm.CompleteAsync(request, ct);
            turn.AssistantMessage = response.AssistantMessage;

            // Fire LLM response received event
            LlmResponseReceived?.Invoke(this, new LlmResponseReceivedEventArgs
            {
                ConversationId = _conversation.Id,
                Response = response.AssistantMessage,
                Usage = response.Usage,
                HasToolCalls = response.HasToolCalls
            });

            // 3) If tool calls present, execute all and append results.
            if (response.HasToolCalls)
            {
                foreach (var call in response.AssistantMessage.ToolCalls)
            {
                    // Validate tool call before execution
                    var toolDef = tools.FirstOrDefault(t => t.Name.Equals(call.Name, StringComparison.OrdinalIgnoreCase));
                    if (toolDef != null)
                    {
                        var validation = ToolCallValidator.Validate(call, toolDef);
                        if (!validation.IsValid)
                        {
                            // Fire validation failed event
                            ToolValidationFailed?.Invoke(this, new ToolValidationFailedEventArgs
                            {
                                ConversationId = _conversation.Id,
                                ToolCall = call,
                                ValidationErrors = validation.Errors.ToArray()
                            });

                            var validationError = ToolResult.FromObject(call.Id, call.Name,
                                new { error = "validation_failed", details = validation.Errors }, isError: true);
                            var toolMsg = new Message
                            {
                                Role = Role.Tool,
                                Content = validationError.ResultJson,
                                ToolResults = new List<ToolResult> { validationError },
                                Metadata = new Dictionary<string, object>
                                {
                                    ["tool_name"] = call.Name,
                                    ["tool_call_id"] = call.Id,
                                    ["validation_error"] = true
                                }
                            };
                            turn.ToolMessages.Add(toolMsg);
                            continue;
                        }
                    }

                    if (_tools.TryGet(call.Name, out var tool))
                    {
                        // Fire tool execution started event
                        ToolExecutionStarted?.Invoke(this, new ToolExecutionStartedEventArgs
                        {
                            ConversationId = _conversation.Id,
                            ToolCall = call,
                            ToolName = call.Name
                        });

                        var toolStopwatch = Stopwatch.StartNew();
                        ToolResult result;
                        try
                        {
                            result = await tool.ExecuteAsync(call, ct);
                            toolCallsExecuted++;
                        }
                        catch (ToolExecutionException ex)
                        {
                            result = ToolResult.FromObject(call.Id, call.Name,
                                new { error = ex.Message, tool_name = ex.ToolName, call_id = ex.CallId }, isError: true);
                        }
                        catch (Exception ex)
                        {
                            result = ToolResult.FromObject(call.Id, call.Name,
                                new { error = ex.Message, type = ex.GetType().Name }, isError: true);
                        }
                        toolStopwatch.Stop();

                        // Fire tool execution completed event
                        ToolExecutionCompleted?.Invoke(this, new ToolExecutionCompletedEventArgs
                        {
                            ConversationId = _conversation.Id,
                            ToolCall = call,
                            Result = result,
                            IsError = result.IsError,
                            Duration = toolStopwatch.Elapsed
                        });
                    
                    var toolMsg = new Message
                    {
                        Role = Role.Tool,
                        Content = result.ResultJson, // LLMs typically expect raw JSON string here
                        ToolResults = new List<ToolResult> { result },
                        Metadata = new Dictionary<string, object> 
                        { 
                            ["tool_name"] = call.Name, 
                            ["tool_call_id"] = call.Id,
                            ["is_error"] = result.IsError
                        }
                    };
                    turn.ToolMessages.Add(toolMsg);
                }
                    else
                    {
                        // Fire tool not found event
                        ToolNotFound?.Invoke(this, new ToolNotFoundEventArgs
                        {
                            ConversationId = _conversation.Id,
                            ToolName = call.Name,
                            CallId = call.Id,
                            AvailableTools = _tools.GetRegisteredToolNames().ToArray()
                        });

                        var notFound = ToolResult.FromObject(call.Id, call.Name,
                            new { error = "tool_not_found", available_tools = _tools.GetRegisteredToolNames() }, isError: true);
                    var toolMsg = new Message 
                    { 
                        Role = Role.Tool, 
                        Content = notFound.ResultJson, 
                        ToolResults = new List<ToolResult> { notFound },
                        Metadata = new Dictionary<string, object> 
                        { 
                            ["tool_name"] = call.Name, 
                            ["tool_call_id"] = call.Id,
                            ["tool_not_found"] = true
                        }
                    };
                    turn.ToolMessages.Add(toolMsg);
                }
            }

                // 4) After tools, rebuild context and get final assistant response.
                messages = _conversation.ToChronoMessages().ToArray();

                // Fire LLM request started event (retry after tools)
                LlmRequestStarted?.Invoke(this, new LlmRequestStartedEventArgs
                {
                    ConversationId = _conversation.Id,
                    MessageCount = messages.Length,
                    ToolCount = tools.Length,
                    IsRetryAfterTools = true
                });

                var request2 = new LlmRequest { Messages = messages, Tools = tools };
                var second = await _llm.CompleteAsync(request2, ct);

                // Fire LLM response received event
                LlmResponseReceived?.Invoke(this, new LlmResponseReceivedEventArgs
                {
                    ConversationId = _conversation.Id,
                    Response = second.AssistantMessage,
                    Usage = second.Usage,
                    HasToolCalls = false
                });
                // Update the assistant message content but preserve the tool calls from the first response
                turn.AssistantMessage = new Message
                {
                    Role = Role.Assistant,
                    Content = second.AssistantMessage.Content,
                    ToolCalls = response.AssistantMessage.ToolCalls, // Preserve the original tool calls
                    Metadata = second.AssistantMessage.Metadata,
                    Timestamp = second.AssistantMessage.Timestamp,
                    Id = second.AssistantMessage.Id,
                };
            }

            stopwatch.Stop();

            // Fire turn completed event
            TurnCompleted?.Invoke(this, new TurnCompletedEventArgs
            {
                ConversationId = _conversation.Id,
                AssistantMessage = turn.AssistantMessage!,
                ToolCallsExecuted = toolCallsExecuted,
                Duration = stopwatch.Elapsed
            });

            return turn.AssistantMessage!;
        }
        catch (Exception ex)
        {
            // Fire error occurred event
            ErrorOccurred?.Invoke(this, new ErrorOccurredEventArgs
            {
                ConversationId = _conversation.Id,
                Exception = ex,
                Context = "RunTurnAsync",
                IsCritical = true
            });
            throw;
        }
    }

    /// <summary>
    /// Run a streaming turn for real-time responses.
    /// </summary>
    public async IAsyncEnumerable<Message> RunTurnStreamAsync(string userText, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var toolCallsExecuted = 0;

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

        // 2) Build context and stream LLM response.
        var messages = _conversation.ToChronoMessages().ToArray();
        var tools = _tools.GetDeclaredTools();

        // Fire LLM request started event
        LlmRequestStarted?.Invoke(this, new LlmRequestStartedEventArgs
        {
            ConversationId = _conversation.Id,
            MessageCount = messages.Length,
            ToolCount = tools.Length,
            IsRetryAfterTools = false
        });
        
        var hasToolCalls = false;
        var toolCalls = new List<ToolCall>();
        
        var request = new LlmRequest { Messages = messages, Tools = tools };
        await foreach (var response in _llm.StreamCompleteAsync(request, ct))
        {
            var message = response.Delta;
            if (message?.ToolCalls.Any() == true)
            {
                hasToolCalls = true;
                toolCalls.AddRange(message.ToolCalls);
            }

            // Fire streaming token received event
            StreamingTokenReceived?.Invoke(this, new StreamingTokenReceivedEventArgs
            {
                ConversationId = _conversation.Id,
                Delta = message,
                IsComplete = response.IsComplete
            });

            yield return message;
        }

        // 3) If tool calls present, execute them and stream final response.
        if (hasToolCalls && toolCalls.Any())
        {
            // Execute tools (non-streaming for now)
            foreach (var call in toolCalls)
            {
                if (_tools.TryGet(call.Name, out var tool))
                {
                    // Fire tool execution started event
                    ToolExecutionStarted?.Invoke(this, new ToolExecutionStartedEventArgs
                    {
                        ConversationId = _conversation.Id,
                        ToolCall = call,
                        ToolName = call.Name
                    });

                    var toolStopwatch = Stopwatch.StartNew();
                    ToolResult result;
                    try
                    {
                        result = await tool.ExecuteAsync(call, ct);
                        toolCallsExecuted++;
                    }
                    catch (Exception ex)
                    {
                        result = ToolResult.FromObject(call.Id, call.Name,
                            new { error = ex.Message }, isError: true);
                    }
                    toolStopwatch.Stop();

                    // Fire tool execution completed event
                    ToolExecutionCompleted?.Invoke(this, new ToolExecutionCompletedEventArgs
                    {
                        ConversationId = _conversation.Id,
                        ToolCall = call,
                        Result = result,
                        IsError = result.IsError,
                        Duration = toolStopwatch.Elapsed
                    });
                    
                    var toolMsg = new Message
                    {
                        Role = Role.Tool,
                        Content = result.ResultJson,
                        ToolResults = new List<ToolResult> { result },
                        Metadata = new Dictionary<string, object> 
                        { 
                            ["tool_name"] = call.Name, 
                            ["tool_call_id"] = call.Id
                        }
                    };
                    turn.ToolMessages.Add(toolMsg);
                }
            }

            // Stream final response after tool execution
            messages = _conversation.ToChronoMessages().ToArray();

            // Fire LLM request started event (retry after tools)
            LlmRequestStarted?.Invoke(this, new LlmRequestStartedEventArgs
            {
                ConversationId = _conversation.Id,
                MessageCount = messages.Length,
                ToolCount = tools.Length,
                IsRetryAfterTools = true
            });

            var request2 = new LlmRequest { Messages = messages, Tools = tools };
            Message? lastMessage = null;
            await foreach (var response in _llm.StreamCompleteAsync(request2, ct))
            {
                if (response.Delta != null)
                {
                    lastMessage = response.Delta;

                    // Fire streaming token received event
                    StreamingTokenReceived?.Invoke(this, new StreamingTokenReceivedEventArgs
                    {
                        ConversationId = _conversation.Id,
                        Delta = response.Delta,
                        IsComplete = response.IsComplete
                    });

                    yield return response.Delta;
                }
            }

            // Update turn with final assistant message
            if (lastMessage != null)
                turn.AssistantMessage = lastMessage;
        }

        stopwatch.Stop();

        // Fire turn completed event
        TurnCompleted?.Invoke(this, new TurnCompletedEventArgs
        {
            ConversationId = _conversation.Id,
            AssistantMessage = turn.AssistantMessage ?? new Message { Role = Role.Assistant, Content = string.Empty },
            ToolCallsExecuted = toolCallsExecuted,
            Duration = stopwatch.Elapsed
        });
    }
}

#endregion