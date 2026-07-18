using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Andy.Model.Model;
using Andy.Model.Orchestration;
using Andy.Model.Tooling;
using Xunit;

namespace Andy.Model.Tests.Orchestration;

/// <summary>
/// Behavior-focused regression tests exercising both <see cref="Assistant"/> and
/// <see cref="AssistantWithManager"/> through the same scenarios to guarantee identical
/// behavior (issues #8, #9, #12, #13).
/// </summary>
public class AssistantOrchestrationTests
{
    public static IEnumerable<object[]> Kinds => new[]
    {
        new object[] { "Assistant" },
        new object[] { "AssistantWithManager" }
    };

    // ---- #12 / #8: complete, protocol-ordered history ----

    [Theory]
    [MemberData(nameof(Kinds))]
    public async Task NoToolCalls_StoresUserThenAssistant(string kind)
    {
        var llm = new ScriptedLlmProvider().EnqueueText("Hello there");
        var harness = AssistantHarness.Create(kind, new ToolRegistry(), llm);

        var final = await harness.RunTurnAsync("Hi", CancellationToken.None);

        Assert.Equal("Hello there", final.Content);
        var chrono = harness.Conversation.ToChronoMessages().ToList();
        Assert.Equal(2, chrono.Count);
        Assert.Equal(Role.User, chrono[0].Role);
        Assert.Equal(Role.Assistant, chrono[1].Role);
        Assert.Equal("Hello there", chrono[1].Content);
    }

    [Theory]
    [MemberData(nameof(Kinds))]
    public async Task SingleToolRound_StoresFullProtocolSequence(string kind)
    {
        var llm = new ScriptedLlmProvider()
            .EnqueueToolCall("record", "call-1", new { x = 1 })
            .EnqueueText("All done");
        var tools = new ToolRegistry();
        var recorder = new RecordingTool();
        tools.Register(recorder);

        var harness = AssistantHarness.Create(kind, tools, llm);

        var final = await harness.RunTurnAsync("Do it", CancellationToken.None);

        Assert.Equal("All done", final.Content);
        Assert.Equal(1, recorder.Invocations);

        var chrono = harness.Conversation.ToChronoMessages().ToList();
        // user -> assistant(tool call) -> tool result -> assistant(final)
        Assert.Equal(4, chrono.Count);
        Assert.Equal(Role.User, chrono[0].Role);
        Assert.Equal(Role.Assistant, chrono[1].Role);
        Assert.Single(chrono[1].ToolCalls);
        Assert.Equal(Role.Tool, chrono[2].Role);
        Assert.Equal("call-1", chrono[2].ToolResults[0].CallId);
        Assert.Equal(Role.Assistant, chrono[3].Role);
        Assert.Equal("All done", chrono[3].Content);
        Assert.Empty(chrono[3].ToolCalls);
    }

    [Theory]
    [MemberData(nameof(Kinds))]
    public async Task MultipleSequentialToolRounds_RetainsEveryMessageInOrder(string kind)
    {
        var llm = new ScriptedLlmProvider()
            .EnqueueToolCall("record", "call-1", new { step = 1 })
            .EnqueueToolCall("record", "call-2", new { step = 2 })
            .EnqueueText("Finished");
        var tools = new ToolRegistry();
        var recorder = new RecordingTool();
        tools.Register(recorder);

        var harness = AssistantHarness.Create(kind, tools, llm);

        var final = await harness.RunTurnAsync("Go", CancellationToken.None);

        Assert.Equal("Finished", final.Content);
        Assert.Equal(2, recorder.Invocations);

        var chrono = harness.Conversation.ToChronoMessages().ToList();
        var roles = chrono.Select(m => m.Role).ToList();
        Assert.Equal(new[]
        {
            Role.User,
            Role.Assistant, // tool call 1
            Role.Tool,      // result 1
            Role.Assistant, // tool call 2
            Role.Tool,      // result 2
            Role.Assistant  // final
        }, roles);
    }

    [Theory]
    [MemberData(nameof(Kinds))]
    public async Task ParallelToolCallsInOneRound_ProduceTwoToolResults(string kind)
    {
        var llm = new ScriptedLlmProvider()
            .EnqueueCompletion(_ =>
            {
                var r = Responses.ToolCall("record", "a", new { n = 1 });
                r.AssistantMessage.ToolCalls.Add(new ToolCall { Id = "b", Name = "record", ArgumentsJson = "{}" });
                return r;
            })
            .EnqueueText("done");
        var tools = new ToolRegistry();
        var recorder = new RecordingTool();
        tools.Register(recorder);

        var harness = AssistantHarness.Create(kind, tools, llm);
        await harness.RunTurnAsync("both", CancellationToken.None);

        Assert.Equal(2, recorder.Invocations);
        var toolMessages = harness.Conversation.ToChronoMessages().Where(m => m.Role == Role.Tool).ToList();
        Assert.Equal(2, toolMessages.Count);
    }

    [Theory]
    [MemberData(nameof(Kinds))]
    public async Task LlmResponseReceived_ReportsActualHasToolCalls(string kind)
    {
        var llm = new ScriptedLlmProvider()
            .EnqueueToolCall("record", "call-1", new { })
            .EnqueueText("final");
        var tools = new ToolRegistry();
        tools.Register(new RecordingTool());
        var harness = AssistantHarness.Create(kind, tools, llm);

        await harness.RunTurnAsync("hi", CancellationToken.None);

        Assert.Equal(2, harness.LlmResponses.Count);
        Assert.True(harness.LlmResponses[0].HasToolCalls);
        Assert.False(harness.LlmResponses[1].HasToolCalls);
    }

    // ---- #8: iteration limit ----

    [Theory]
    [MemberData(nameof(Kinds))]
    public async Task IterationLimit_StopsAndFiresEvent(string kind)
    {
        // Always returns a tool call; limit of 2 rounds.
        var llm = new ScriptedLlmProvider();
        for (int i = 0; i < 10; i++) llm.EnqueueToolCall("record", $"call-{i}", new { i });
        var tools = new ToolRegistry();
        var recorder = new RecordingTool();
        tools.Register(recorder);

        var options = new AssistantOptions { MaxToolIterations = 2 };
        var harness = AssistantHarness.Create(kind, tools, llm, options);

        await harness.RunTurnAsync("loop", CancellationToken.None);

        Assert.Equal(2, recorder.Invocations); // exactly MaxToolIterations rounds executed
        Assert.Single(harness.IterationLimits);
        Assert.Equal(2, harness.IterationLimits[0].MaxToolIterations);
        Assert.NotEmpty(harness.IterationLimits[0].PendingToolCalls);
    }

    [Theory]
    [MemberData(nameof(Kinds))]
    public async Task IterationLimit_ThrowBehavior_Throws(string kind)
    {
        var llm = new ScriptedLlmProvider();
        for (int i = 0; i < 10; i++) llm.EnqueueToolCall("record", $"call-{i}", new { i });
        var tools = new ToolRegistry();
        tools.Register(new RecordingTool());

        var options = new AssistantOptions { MaxToolIterations = 1, OnToolIterationLimit = ToolIterationLimitBehavior.Throw };
        var harness = AssistantHarness.Create(kind, tools, llm, options);

        await Assert.ThrowsAsync<ToolIterationLimitException>(() => harness.RunTurnAsync("loop", CancellationToken.None));
    }

    [Fact]
    public void AssistantOptions_InvalidMaxIterations_ThrowsOnConstruction()
    {
        Assert.Throws<System.ArgumentOutOfRangeException>(() =>
            new Assistant(new Model.Conversation(), new ToolRegistry(), new ScriptedLlmProvider(),
                new AssistantOptions { MaxToolIterations = 0 }));
    }

    // ---- #13: cancellation and tool events ----

    [Theory]
    [MemberData(nameof(Kinds))]
    public async Task CooperativeCancellation_PropagatesInsteadOfBecomingToolError(string kind)
    {
        var cts = new CancellationTokenSource();
        var llm = new ScriptedLlmProvider().EnqueueToolCall("cancel", "c1", new { });
        var tools = new ToolRegistry();
        tools.Register(new CooperativeCancelTool(cts));
        var harness = AssistantHarness.Create(kind, tools, llm);

        await Assert.ThrowsAnyAsync<System.OperationCanceledException>(
            () => harness.RunTurnAsync("cancel me", cts.Token));

        // No tool-result message was recorded for the cancelled call.
        Assert.DoesNotContain(harness.Conversation.ToChronoMessages(), m => m.Role == Role.Tool);
    }

    [Theory]
    [MemberData(nameof(Kinds))]
    public async Task UnrelatedCancellation_BecomesToolError(string kind)
    {
        var llm = new ScriptedLlmProvider()
            .EnqueueToolCall("unrelated", "u1", new { })
            .EnqueueText("recovered");
        var tools = new ToolRegistry();
        tools.Register(new UnrelatedCancelTool());
        var harness = AssistantHarness.Create(kind, tools, llm);

        var final = await harness.RunTurnAsync("go", CancellationToken.None);

        Assert.Equal("recovered", final.Content);
        var toolMsg = harness.Conversation.ToChronoMessages().Single(m => m.Role == Role.Tool);
        Assert.True(toolMsg.ToolResults[0].IsError);
    }

    [Theory]
    [MemberData(nameof(Kinds))]
    public async Task ToolError_RedactsExceptionDetailsByDefault(string kind)
    {
        var llm = new ScriptedLlmProvider()
            .EnqueueToolCall("boom", "b1", new { })
            .EnqueueText("ok");
        var tools = new ToolRegistry();
        tools.Register(new ThrowingTool());
        var harness = AssistantHarness.Create(kind, tools, llm);

        await harness.RunTurnAsync("go", CancellationToken.None);

        var toolMsg = harness.Conversation.ToChronoMessages().Single(m => m.Role == Role.Tool);
        Assert.True(toolMsg.ToolResults[0].IsError);
        Assert.DoesNotContain(ThrowingTool.SecretMarker, toolMsg.ToolResults[0].ResultJson);
        Assert.Contains("tool_execution_failed", toolMsg.ToolResults[0].ResultJson);
    }

    [Theory]
    [MemberData(nameof(Kinds))]
    public async Task ToolError_IncludesDetailsWhenOptedIn(string kind)
    {
        var llm = new ScriptedLlmProvider()
            .EnqueueToolCall("boom", "b1", new { })
            .EnqueueText("ok");
        var tools = new ToolRegistry();
        tools.Register(new ThrowingTool());
        var options = new AssistantOptions { IncludeExceptionDetailsInToolErrors = true };
        var harness = AssistantHarness.Create(kind, tools, llm, options);

        await harness.RunTurnAsync("go", CancellationToken.None);

        var toolMsg = harness.Conversation.ToChronoMessages().Single(m => m.Role == Role.Tool);
        Assert.Contains(ThrowingTool.SecretMarker, toolMsg.ToolResults[0].ResultJson);
    }

    [Theory]
    [MemberData(nameof(Kinds))]
    public async Task MissingTool_ProducesNotFoundResultAndCounts(string kind)
    {
        var llm = new ScriptedLlmProvider()
            .EnqueueToolCall("ghost", "g1", new { })
            .EnqueueText("ok");
        var harness = AssistantHarness.Create(kind, new ToolRegistry(), llm);

        await harness.RunTurnAsync("go", CancellationToken.None);

        Assert.Single(harness.NotFounds);
        var toolMsg = harness.Conversation.ToChronoMessages().Single(m => m.Role == Role.Tool);
        Assert.True(toolMsg.ToolResults[0].IsError);
        Assert.Contains("tool_not_found", toolMsg.ToolResults[0].ResultJson);
        Assert.Equal(1, harness.TurnCompletions.Single().ToolCallsNotFound);
        Assert.Equal(0, harness.TurnCompletions.Single().ToolCallsExecuted);
    }

    [Theory]
    [MemberData(nameof(Kinds))]
    public async Task TurnCompleted_ReportsOutcomeCounts(string kind)
    {
        var llm = new ScriptedLlmProvider()
            .EnqueueCompletion(_ =>
            {
                var r = Responses.ToolCall("record", "ok", new { });
                r.AssistantMessage.ToolCalls.Add(new ToolCall { Id = "err", Name = "boom", ArgumentsJson = "{}" });
                return r;
            })
            .EnqueueText("done");
        var tools = new ToolRegistry();
        tools.Register(new RecordingTool());
        tools.Register(new ThrowingTool());
        var harness = AssistantHarness.Create(kind, tools, llm);

        await harness.RunTurnAsync("go", CancellationToken.None);

        var completed = harness.TurnCompletions.Single();
        Assert.Equal(2, completed.ToolCallsAttempted);
        Assert.Equal(1, completed.ToolCallsSucceeded);
        Assert.Equal(1, completed.ToolCallsFailed);
        Assert.Equal(2, completed.ToolCallsExecuted);
        Assert.Equal(1, completed.ToolRounds);
    }

    // ---- #9: streaming ----

    [Theory]
    [MemberData(nameof(Kinds))]
    public async Task Streaming_NoTools_AccumulatesTextIntoOneMessage(string kind)
    {
        var llm = new ScriptedLlmProvider()
            .EnqueueStream(Responses.TextDelta("Hel"), Responses.TextDelta("lo", isComplete: true));
        var harness = AssistantHarness.Create(kind, new ToolRegistry(), llm);

        var chunks = new List<Message>();
        await foreach (var m in harness.RunTurnStreamAsync("hi", CancellationToken.None))
        {
            chunks.Add(m);
        }

        Assert.Equal(2, chunks.Count); // both deltas surfaced to the caller
        var chrono = harness.Conversation.ToChronoMessages().ToList();
        Assert.Equal(2, chrono.Count);
        Assert.Equal(Role.Assistant, chrono[1].Role);
        Assert.Equal("Hello", chrono[1].Content); // accumulated, not just the last delta
    }

    [Theory]
    [MemberData(nameof(Kinds))]
    public async Task Streaming_ToolCall_MergesFragmentsAndStoresBeforeResults(string kind)
    {
        var llm = new ScriptedLlmProvider()
            .EnqueueStream(
                Responses.ToolCallDelta(name: "record", id: "call-1", argsFragment: "{\"loc\":"),
                Responses.ToolCallDelta(name: null, id: null, argsFragment: "\"NYC\"}", isComplete: true))
            .EnqueueStream(Responses.TextDelta("Weather is fine", isComplete: true));
        var tools = new ToolRegistry();
        var recorder = new RecordingTool();
        tools.Register(recorder);
        var harness = AssistantHarness.Create(kind, tools, llm);

        await foreach (var _ in harness.RunTurnStreamAsync("weather", CancellationToken.None)) { }

        Assert.Equal(1, recorder.Invocations);
        Assert.Equal("{\"loc\":\"NYC\"}", recorder.ReceivedArguments[0]); // fragments merged

        var chrono = harness.Conversation.ToChronoMessages().ToList();
        Assert.Equal(4, chrono.Count);
        Assert.Equal(Role.Assistant, chrono[1].Role);
        Assert.Single(chrono[1].ToolCalls); // assistant tool-call message stored...
        Assert.Equal(Role.Tool, chrono[2].Role); // ...before the tool result
        Assert.Equal(Role.Assistant, chrono[3].Role);
        Assert.Equal("Weather is fine", chrono[3].Content);
    }

    [Theory]
    [MemberData(nameof(Kinds))]
    public async Task Streaming_ProviderThrows_FiresErrorLifecycleEvent(string kind)
    {
        var llm = new ScriptedLlmProvider().EnqueueStream(_ => ThrowingStream());
        var harness = AssistantHarness.Create(kind, new ToolRegistry(), llm);

        await Assert.ThrowsAsync<System.InvalidOperationException>(async () =>
        {
            await foreach (var _ in harness.RunTurnStreamAsync("hi", CancellationToken.None)) { }
        });

        Assert.Single(harness.Errors);
        Assert.IsType<System.InvalidOperationException>(harness.Errors[0].Exception);
    }

    private static IEnumerable<Andy.Model.Llm.LlmStreamResponse> ThrowingStream()
    {
        yield return Responses.TextDelta("partial");
        throw new System.InvalidOperationException("stream blew up");
    }
}
