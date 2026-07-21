using System;
using System.Collections.Generic;
using System.Threading;
using Andy.Model.Llm;
using Andy.Model.Model;
using Andy.Model.Orchestration;
using Andy.Model.Tooling;

namespace Andy.Model.Tests.Orchestration;

/// <summary>
/// Adapts either <see cref="Assistant"/> or <see cref="AssistantWithManager"/> to a
/// common surface and records lifecycle events, so a single test body can assert
/// identical behavior across both implementations.
/// </summary>
internal sealed class AssistantHarness
{
    public required Model.Conversation Conversation { get; init; }
    public required Func<string, CancellationToken, Task<Message>> RunTurnAsync { get; init; }
    public required Func<string, CancellationToken, IAsyncEnumerable<Message>> RunTurnStreamAsync { get; init; }

    public List<LlmResponseReceivedEventArgs> LlmResponses { get; } = new();
    public List<TurnCompletedEventArgs> TurnCompletions { get; } = new();
    public List<ToolIterationLimitReachedEventArgs> IterationLimits { get; } = new();
    public List<ErrorOccurredEventArgs> Errors { get; } = new();
    public List<ToolExecutionCompletedEventArgs> ToolCompletions { get; } = new();
    public List<ToolValidationFailedEventArgs> ValidationFailures { get; } = new();
    public List<ToolNotFoundEventArgs> NotFounds { get; } = new();

    public static AssistantHarness Create(string kind, ToolRegistry tools, ILlmProvider llm, AssistantOptions? options = null)
    {
        if (kind == "Assistant")
        {
            var conversation = new Model.Conversation();
            var a = new Assistant(conversation, tools, llm, options);
            var harness = new AssistantHarness
            {
                Conversation = conversation,
                RunTurnAsync = a.RunTurnAsync,
                RunTurnStreamAsync = a.RunTurnStreamAsync
            };
            a.LlmResponseReceived += (_, e) => harness.LlmResponses.Add(e);
            a.TurnCompleted += (_, e) => harness.TurnCompletions.Add(e);
            a.ToolIterationLimitReached += (_, e) => harness.IterationLimits.Add(e);
            a.ErrorOccurred += (_, e) => harness.Errors.Add(e);
            a.ToolExecutionCompleted += (_, e) => harness.ToolCompletions.Add(e);
            a.ToolValidationFailed += (_, e) => harness.ValidationFailures.Add(e);
            a.ToolNotFound += (_, e) => harness.NotFounds.Add(e);
            return harness;
        }

        if (kind == "AssistantWithManager")
        {
            var a = new AssistantWithManager(tools, llm, assistantOptions: options);
            var harness = new AssistantHarness
            {
                Conversation = a.Conversation,
                RunTurnAsync = a.RunTurnAsync,
                RunTurnStreamAsync = a.RunTurnStreamAsync
            };
            a.LlmResponseReceived += (_, e) => harness.LlmResponses.Add(e);
            a.TurnCompleted += (_, e) => harness.TurnCompletions.Add(e);
            a.ToolIterationLimitReached += (_, e) => harness.IterationLimits.Add(e);
            a.ErrorOccurred += (_, e) => harness.Errors.Add(e);
            a.ToolExecutionCompleted += (_, e) => harness.ToolCompletions.Add(e);
            a.ToolValidationFailed += (_, e) => harness.ValidationFailures.Add(e);
            a.ToolNotFound += (_, e) => harness.NotFounds.Add(e);
            return harness;
        }

        throw new ArgumentException($"Unknown assistant kind: {kind}", nameof(kind));
    }
}
