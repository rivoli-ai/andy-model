using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Andy.Model.Llm;
using Andy.Model.Model;
using Andy.Model.Tooling;

namespace Andy.Model.Tests.Orchestration;

/// <summary>
/// A fully scriptable <see cref="ILlmProvider"/> for orchestration tests. Non-streaming
/// and streaming responses are enqueued independently and dequeued per call.
/// </summary>
internal sealed class ScriptedLlmProvider : ILlmProvider
{
    private readonly Queue<Func<LlmRequest, LlmResponse>> _completions = new();
    private readonly Queue<Func<LlmRequest, IEnumerable<LlmStreamResponse>>> _streams = new();

    public string Name => "Scripted";
    public int CompleteCallCount { get; private set; }
    public int StreamCallCount { get; private set; }
    public LlmRequest? LastRequest { get; private set; }
    public List<LlmRequest> Requests { get; } = new();

    public ScriptedLlmProvider EnqueueText(string content)
        => EnqueueCompletion(_ => Responses.Text(content));

    public ScriptedLlmProvider EnqueueToolCall(string name, string id, object args)
        => EnqueueCompletion(_ => Responses.ToolCall(name, id, args));

    public ScriptedLlmProvider EnqueueCompletion(Func<LlmRequest, LlmResponse> factory)
    {
        _completions.Enqueue(factory);
        return this;
    }

    public ScriptedLlmProvider EnqueueStream(params LlmStreamResponse[] chunks)
        => EnqueueStream(_ => chunks);

    public ScriptedLlmProvider EnqueueStream(Func<LlmRequest, IEnumerable<LlmStreamResponse>> factory)
    {
        _streams.Enqueue(factory);
        return this;
    }

    public Task<LlmResponse> CompleteAsync(LlmRequest request, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        CompleteCallCount++;
        LastRequest = request;
        Requests.Add(request);

        if (_completions.Count == 0)
        {
            throw new InvalidOperationException("ScriptedLlmProvider: no more scripted completions.");
        }

        return Task.FromResult(_completions.Dequeue()(request));
    }

    public async IAsyncEnumerable<LlmStreamResponse> StreamCompleteAsync(
        LlmRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        StreamCallCount++;
        LastRequest = request;
        Requests.Add(request);

        if (_streams.Count == 0)
        {
            throw new InvalidOperationException("ScriptedLlmProvider: no more scripted streams.");
        }

        foreach (var chunk in _streams.Dequeue()(request))
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return chunk;
            await Task.Yield();
        }
    }

    public Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default) => Task.FromResult(true);

    public Task<IEnumerable<ModelInfo>> ListModelsAsync(CancellationToken cancellationToken = default)
        => Task.FromResult<IEnumerable<ModelInfo>>(new[] { new ModelInfo { Id = "scripted", Name = "Scripted" } });

    public void Dispose() { }
}

/// <summary>Factory helpers for scripted responses and stream chunks.</summary>
internal static class Responses
{
    public static LlmResponse Text(string content) => new()
    {
        AssistantMessage = new Message { Role = Role.Assistant, Content = content }
    };

    public static LlmResponse ToolCall(string name, string id, object args) => new()
    {
        AssistantMessage = new Message
        {
            Role = Role.Assistant,
            Content = string.Empty,
            ToolCalls = new List<ToolCall>
            {
                new() { Id = id, Name = name, ArgumentsJson = JsonSerializer.Serialize(args) }
            }
        }
    };

    public static LlmStreamResponse TextDelta(string content, bool isComplete = false) => new()
    {
        Delta = new Message { Role = Role.Assistant, Content = content },
        IsComplete = isComplete
    };

    public static LlmStreamResponse ToolCallDelta(string? name, string? id, string? argsFragment, bool isComplete = false) => new()
    {
        Delta = new Message
        {
            Role = Role.Assistant,
            Content = string.Empty,
            ToolCalls = new List<ToolCall>
            {
                new() { Id = id ?? string.Empty, Name = name ?? string.Empty, ArgumentsJson = argsFragment ?? "{}" }
            }
        },
        IsComplete = isComplete
    };
}

/// <summary>Records how many times it was invoked and returns a canned success result.</summary>
internal sealed class RecordingTool : ITool
{
    private readonly string _name;
    public int Invocations { get; private set; }
    public List<string> ReceivedArguments { get; } = new();

    public RecordingTool(string name = "record") => _name = name;

    public ToolDeclaration Definition => new()
    {
        Name = _name,
        Description = "Records invocations",
        Parameters = new Dictionary<string, object> { ["type"] = "object" }
    };

    public Task<ToolResult> ExecuteAsync(ToolCall call, CancellationToken ct = default)
    {
        Invocations++;
        ReceivedArguments.Add(call.ArgumentsJson);
        return Task.FromResult(ToolResult.FromObject(call.Id, _name, new { ok = true, invocation = Invocations }));
    }
}

/// <summary>Throws a plain exception carrying a sensitive marker string.</summary>
internal sealed class ThrowingTool : ITool
{
    public const string SecretMarker = "SENSITIVE-STACK-DETAILS-9f83";
    private readonly string _name;

    public ThrowingTool(string name = "boom") => _name = name;

    public ToolDeclaration Definition => new() { Name = _name, Description = "Always throws" };

    public Task<ToolResult> ExecuteAsync(ToolCall call, CancellationToken ct = default)
        => throw new InvalidOperationException(SecretMarker);
}

/// <summary>
/// Cancels the supplied token source on execution and then observes cancellation,
/// simulating a tool that honors the active cancellation token.
/// </summary>
internal sealed class CooperativeCancelTool : ITool
{
    private readonly CancellationTokenSource _cts;
    private readonly string _name;

    public CooperativeCancelTool(CancellationTokenSource cts, string name = "cancel") { _cts = cts; _name = name; }

    public ToolDeclaration Definition => new() { Name = _name, Description = "Cancels" };

    public Task<ToolResult> ExecuteAsync(ToolCall call, CancellationToken ct = default)
    {
        _cts.Cancel();
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(ToolResult.FromObject(call.Id, _name, new { ok = true }));
    }
}

/// <summary>Throws an OperationCanceledException tied to an unrelated token.</summary>
internal sealed class UnrelatedCancelTool : ITool
{
    private readonly string _name;
    public UnrelatedCancelTool(string name = "unrelated") => _name = name;

    public ToolDeclaration Definition => new() { Name = _name, Description = "Throws unrelated cancellation" };

    public Task<ToolResult> ExecuteAsync(ToolCall call, CancellationToken ct = default)
    {
        using var other = new CancellationTokenSource();
        other.Cancel();
        throw new OperationCanceledException(other.Token);
    }
}
