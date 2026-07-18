using System;
using System.Collections.Generic;
using System.Linq;
using Andy.Model.Model;
using Andy.Model.Utils;
using Xunit;

namespace Andy.Model.Tests.Core;

/// <summary>
/// Lossless JSON round-trip tests for Conversation (#11).
/// </summary>
public class ConversationRoundTripTests
{
    private static void AssertMessagesEqual(IReadOnlyList<Message> expected, IReadOnlyList<Message> actual)
    {
        Assert.Equal(expected.Count, actual.Count);
        for (int i = 0; i < expected.Count; i++)
        {
            var e = expected[i];
            var a = actual[i];
            Assert.Equal(e.Role, a.Role);
            Assert.Equal(e.Content, a.Content);
            Assert.Equal(e.Id, a.Id);
            Assert.Equal(e.ToolCallId, a.ToolCallId);
            Assert.Equal(e.Timestamp, a.Timestamp);
            Assert.Equal(e.CacheControl, a.CacheControl);

            Assert.Equal(e.ToolCalls.Count, a.ToolCalls.Count);
            for (int j = 0; j < e.ToolCalls.Count; j++)
            {
                Assert.Equal(e.ToolCalls[j].Id, a.ToolCalls[j].Id);
                Assert.Equal(e.ToolCalls[j].Name, a.ToolCalls[j].Name);
                Assert.Equal(e.ToolCalls[j].ArgumentsJson, a.ToolCalls[j].ArgumentsJson);
            }

            Assert.Equal(e.ToolResults.Count, a.ToolResults.Count);
            for (int j = 0; j < e.ToolResults.Count; j++)
            {
                Assert.Equal(e.ToolResults[j].CallId, a.ToolResults[j].CallId);
                Assert.Equal(e.ToolResults[j].Name, a.ToolResults[j].Name);
                Assert.Equal(e.ToolResults[j].IsError, a.ToolResults[j].IsError);
                Assert.Equal(e.ToolResults[j].ResultJson, a.ToolResults[j].ResultJson);
            }
        }
    }

    private static void AssertRoundTrips(Model.Conversation original)
    {
        var restored = ConversationExtensions.FromJson(original.ToJson());

        Assert.Equal(original.Id, restored.Id);
        Assert.Equal(original.CreatedAt, restored.CreatedAt);
        Assert.Equal(original.LastActivityAt, restored.LastActivityAt);
        Assert.Equal(original.Turns.Count, restored.Turns.Count);
        AssertMessagesEqual(original.ToChronoMessages().ToList(), restored.ToChronoMessages().ToList());
    }

    [Fact]
    public void NormalConversation_RoundTrips()
    {
        var c = new Model.Conversation();
        c.AddTurn(new Turn { UserOrSystemMessage = new Message { Role = Role.System, Content = "Be helpful" } });
        c.AddTurn(new Turn
        {
            UserOrSystemMessage = new Message { Role = Role.User, Content = "Hello" },
            AssistantMessage = new Message { Role = Role.Assistant, Content = "Hi!", CacheControl = CacheControl.Ephemeral }
        });

        AssertRoundTrips(c);
    }

    [Fact]
    public void ToolConversation_RoundTrips_WithLegacyShape()
    {
        var c = new Model.Conversation();
        var turn = new Turn
        {
            UserOrSystemMessage = new Message { Role = Role.User, Content = "weather?" },
            AssistantMessage = new Message
            {
                Role = Role.Assistant,
                Content = "checking",
                ToolCalls = new List<ToolCall> { new() { Id = "c1", Name = "wx", ArgumentsJson = "{\"loc\":\"NYC\"}" } }
            }
        };
        turn.ToolMessages.Add(new Message
        {
            Role = Role.Tool,
            Content = "sunny",
            ToolCallId = "c1",
            ToolResults = new List<ToolResult> { ToolResult.FromObject("c1", "wx", new { temp = 70 }) },
            Metadata = new Dictionary<string, object> { ["tool_name"] = "wx", ["is_error"] = false }
        });
        c.AddTurn(turn);

        AssertRoundTrips(c);

        // Metadata survives (primitive values restored to CLR primitives).
        var restored = ConversationExtensions.FromJson(c.ToJson());
        var toolMsg = restored.ToChronoMessages().Single(m => m.Role == Role.Tool);
        Assert.Equal("wx", toolMsg.Metadata["tool_name"]);
        Assert.Equal(false, toolMsg.Metadata["is_error"]);
    }

    [Fact]
    public void OrchestrationShape_WithOrderedSequence_RoundTrips()
    {
        // Mirrors what the orchestrators produce: user -> assistant(tool call) -> tool -> assistant(final)
        var c = new Model.Conversation();
        var turn = new Turn { UserOrSystemMessage = new Message { Role = Role.User, Content = "go" } };
        turn.AddAssistantMessage(new Message
        {
            Role = Role.Assistant,
            Content = string.Empty,
            ToolCalls = new List<ToolCall> { new() { Id = "c1", Name = "t", ArgumentsJson = "{}" } }
        });
        turn.AddToolMessage(new Message
        {
            Role = Role.Tool,
            Content = "ok",
            ToolCallId = "c1",
            ToolResults = new List<ToolResult> { ToolResult.FromObject("c1", "t", new { done = true }) }
        });
        turn.AddAssistantMessage(new Message { Role = Role.Assistant, Content = "all done" });
        c.AddTurn(turn);

        AssertRoundTrips(c);

        var restored = ConversationExtensions.FromJson(c.ToJson());
        var roles = restored.ToChronoMessages().Select(m => m.Role).ToList();
        Assert.Equal(new[] { Role.User, Role.Assistant, Role.Tool, Role.Assistant }, roles);
    }

    [Fact]
    public void ConversationState_RoundTrips()
    {
        var c = new Model.Conversation();
        c.AddTurn(new Turn { UserOrSystemMessage = new Message { Role = Role.User, Content = "hi" } });
        c.SetState("conversation_summary", "a summary");
        c.SetState("compacted_turn_count", "3");

        var restored = ConversationExtensions.FromJson(c.ToJson());

        Assert.Equal("a summary", restored.GetState<string>("conversation_summary"));
        Assert.Equal("3", restored.GetState<string>("compacted_turn_count"));
    }

    [Fact]
    public void EmptyConversation_RoundTrips()
    {
        AssertRoundTrips(new Model.Conversation());
    }

    [Fact]
    public void MalformedJson_ThrowsActionableError()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => ConversationExtensions.FromJson("{ not json"));
        Assert.Contains("malformed", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void NewerFormatVersion_ThrowsActionableError()
    {
        var json = "{\"formatVersion\": 9999, \"id\": \"x\", \"turns\": [], \"state\": {}}";
        var ex = Assert.Throws<InvalidOperationException>(() => ConversationExtensions.FromJson(json));
        Assert.Contains("version", ex.Message, StringComparison.OrdinalIgnoreCase);
    }
}
