using System;
using System.Collections.Generic;
using System.Linq;
using Andy.Model.Conversation;
using Andy.Model.Model;
using Xunit;

namespace Andy.Model.Tests.Conversation;

/// <summary>
/// Invariant tests for the compression strategies and option validation (#16).
/// </summary>
public class CompressionInvariantsTests
{
    private static Turn UserTurn(string content, DateTimeOffset? ts = null)
        => new() { UserOrSystemMessage = new Message { Role = Role.User, Content = content, Timestamp = ts ?? DateTimeOffset.UtcNow } };

    private static Turn ToolTurn(string user, string callId)
    {
        var turn = new Turn
        {
            UserOrSystemMessage = new Message { Role = Role.User, Content = user },
            AssistantMessage = new Message
            {
                Role = Role.Assistant,
                Content = "calling",
                ToolCalls = new List<ToolCall> { new() { Id = callId, Name = "t", ArgumentsJson = "{}" } }
            }
        };
        turn.ToolMessages.Add(new Message
        {
            Role = Role.Tool,
            Content = "result",
            ToolCallId = callId,
            ToolResults = new List<ToolResult> { ToolResult.FromObject(callId, "t", new { ok = true }) }
        });
        return turn;
    }

    private static bool IsOrderPreserving(IReadOnlyList<Message> chrono, IReadOnlyList<Message> subset)
    {
        int idx = -1;
        foreach (var m in subset)
        {
            var found = -1;
            for (int i = idx + 1; i < chrono.Count; i++)
            {
                if (ReferenceEquals(chrono[i], m)) { found = i; break; }
            }
            if (found < 0) return false;
            idx = found;
        }
        return true;
    }

    [Fact]
    public void SmartCompression_PreservesChronologicalOrder()
    {
        var manager = new DefaultConversationManager(new ConversationManagerOptions
        {
            CompressionStrategy = CompressionStrategy.Smart,
            MaxRecentMessages = 2,
            MaxTokens = 100000
        });

        manager.AddTurn(ToolTurn("early tool use", "c1")); // older; tool call + result out of window
        for (int i = 0; i < 4; i++) manager.AddTurn(UserTurn($"recent {i}"));

        var chrono = manager.Conversation.ToChronoMessages().ToList();
        var result = manager.ExtractMessagesForNextTurn().ToList();

        Assert.True(IsOrderPreserving(chrono, result), "Smart compression must preserve original order");
    }

    [Fact]
    public void SmartCompression_KeepsToolCallAndResultAsCompleteUnit()
    {
        var manager = new DefaultConversationManager(new ConversationManagerOptions
        {
            CompressionStrategy = CompressionStrategy.Smart,
            MaxRecentMessages = 1, // pushes the tool exchange out of the recent window
            MaxTokens = 100000,
            PreserveToolCallPairs = true
        });

        manager.AddTurn(ToolTurn("do it", "c1"));
        for (int i = 0; i < 3; i++) manager.AddTurn(UserTurn($"chat {i}"));

        var result = manager.ExtractMessagesForNextTurn().ToList();

        var hasCall = result.Any(m => m.ToolCalls.Any(tc => tc.Id == "c1"));
        var hasResult = result.Any(m => m.ToolResults.Any(tr => tr.CallId == "c1"));
        Assert.Equal(hasCall, hasResult); // both or neither — never an orphan
        Assert.True(hasCall, "the older tool call and its result should be preserved together");
    }

    [Fact]
    public void PreserveMetadataKeys_ExemptsMessageFromAgeFilter()
    {
        var manager = new DefaultConversationManager(new ConversationManagerOptions
        {
            CompressionStrategy = CompressionStrategy.None,
            MaxMessageAge = TimeSpan.FromHours(1),
            MaxTokens = 100000,
            PreserveMetadataKeys = new HashSet<string> { "pinned" }
        });

        var pinned = new Turn
        {
            UserOrSystemMessage = new Message
            {
                Role = Role.User,
                Content = "remember me",
                Timestamp = DateTimeOffset.UtcNow - TimeSpan.FromHours(48),
                Metadata = new Dictionary<string, object> { ["pinned"] = true }
            }
        };
        manager.AddTurn(pinned);
        manager.AddTurn(UserTurn("recent"));

        var result = manager.ExtractMessagesForNextTurn().ToList();

        Assert.Contains(result, m => m.Content == "remember me");
    }

    [Fact]
    public void SemanticSelection_ConsidersSmallerCandidateAfterOversizedOne()
    {
        var options = new ConversationManagerOptions
        {
            CompressionStrategy = CompressionStrategy.Semantic,
            MaxTokens = 100,       // ~400 chars
            MaxRecentMessages = 10,
            PreserveToolCallPairs = false
        };
        var manager = new SemanticConversationManager(options);

        // A small, important message (older) followed by a huge, even-more-important one (newest).
        manager.AddTurn(UserTurn("important note")); // small, fits
        manager.AddTurn(UserTurn("important " + new string('x', 4000))); // ~1000 tokens, over budget

        var result = manager.ExtractMessagesForNextTurn().ToList();

        Assert.Contains(result, m => m.Content == "important note"); // smaller candidate still selected
        Assert.DoesNotContain(result, m => m.Content.Length > 1000);  // oversized one skipped
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void InvalidMaxTokens_ThrowsOnConstruction(int maxTokens)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new DefaultConversationManager(new ConversationManagerOptions { MaxTokens = maxTokens }));
    }

    [Fact]
    public void NegativeMaxRecentMessages_ThrowsOnConstruction()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new DefaultConversationManager(new ConversationManagerOptions { MaxRecentMessages = -1 }));
    }

    [Fact]
    public void NegativeMaxMessageAge_ThrowsOnConstruction()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new DefaultConversationManager(new ConversationManagerOptions { MaxMessageAge = TimeSpan.FromSeconds(-1) }));
    }

    [Fact]
    public void SemanticReset_ClearsImportanceScores()
    {
        var manager = new SemanticConversationManager(new ConversationManagerOptions { MaxTokens = 1000 });
        manager.AddTurn(UserTurn("hello"));
        _ = manager.ExtractMessagesForNextTurn().ToList();

        manager.Reset(); // should not throw and should clear strategy state

        Assert.NotNull(manager.ExtractMessagesForNextTurn());
    }
}
