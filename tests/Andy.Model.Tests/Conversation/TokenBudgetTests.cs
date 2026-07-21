using System.Collections.Generic;
using System.Linq;
using Andy.Model.Conversation;
using Andy.Model.Model;
using Andy.Model.Tooling;
using Xunit;

namespace Andy.Model.Tests.Conversation;

/// <summary>
/// Boundary tests for DefaultConversationManager token-budget enforcement (#7).
/// Estimation is ~4 characters per token, so content length / 4 == token estimate.
/// </summary>
public class TokenBudgetTests
{
    private static DefaultConversationManager Manager(int maxTokens, bool includeSystem = true)
        => new(new ConversationManagerOptions
        {
            MaxTokens = maxTokens,
            CompressionStrategy = CompressionStrategy.None,
            IncludeSystemMessages = includeSystem
        });

    private static Turn UserTurn(string content)
        => new() { UserOrSystemMessage = new Message { Role = Role.User, Content = content } };

    private static string Chars(int n) => new('x', n);

    private static int EstimateContentTokens(IEnumerable<Message> messages)
        => messages.Sum(m => System.Math.Max(1, m.Content.Length / 4));

    [Fact]
    public void ExactFit_KeepsAllMessages()
    {
        var manager = Manager(maxTokens: 30);
        for (int i = 0; i < 3; i++) manager.AddTurn(UserTurn(Chars(40))); // 10 tokens each => 30

        var result = manager.ExtractMessagesForNextTurn().ToList();

        Assert.Equal(3, result.Count);
    }

    [Fact]
    public void OneTokenOver_DropsOldest_AndStaysWithinBudget()
    {
        var manager = Manager(maxTokens: 29);
        manager.AddTurn(UserTurn("A" + Chars(39))); // oldest
        manager.AddTurn(UserTurn("B" + Chars(39)));
        manager.AddTurn(UserTurn("C" + Chars(39))); // newest

        var result = manager.ExtractMessagesForNextTurn().ToList();

        Assert.True(EstimateContentTokens(result) <= 29);
        Assert.Equal(2, result.Count);
        Assert.StartsWith("B", result[0].Content); // chronological order preserved
        Assert.StartsWith("C", result[1].Content);
    }

    [Fact]
    public void OversizedSystemMessage_IsPreservedEvenThoughOverBudget()
    {
        var manager = Manager(maxTokens: 10);
        manager.AddTurn(new Turn { UserOrSystemMessage = new Message { Role = Role.System, Content = Chars(200) } }); // 50 tokens

        var result = manager.ExtractMessagesForNextTurn().ToList();

        Assert.Contains(result, m => m.Role == Role.System);
    }

    [Fact]
    public void IndivisibleNewestMessage_IsNeverDropped()
    {
        var manager = Manager(maxTokens: 5);
        manager.AddTurn(UserTurn(Chars(400))); // 100 tokens, far over the 5-token budget

        var result = manager.ExtractMessagesForNextTurn().ToList();

        Assert.Single(result); // kept whole despite exceeding budget
    }

    [Fact]
    public void EmptyContentMessages_DoNotThrow_AndAreCounted()
    {
        var manager = Manager(maxTokens: 100);
        for (int i = 0; i < 3; i++) manager.AddTurn(UserTurn(string.Empty));

        var result = manager.ExtractMessagesForNextTurn().ToList();

        Assert.Equal(3, result.Count);
    }

    [Fact]
    public void ToolPayloads_CountTowardBudget()
    {
        // A turn whose tool call + result carry a large JSON payload that has almost no
        // "Content" text. Content-only estimation would ignore it; message estimation must not.
        var manager = Manager(maxTokens: 20);

        manager.AddTurn(UserTurn(Chars(20))); // 5 tokens, older

        var bigArgs = "{\"data\":\"" + Chars(400) + "\"}"; // ~100 tokens of arguments
        var toolTurn = new Turn
        {
            UserOrSystemMessage = new Message { Role = Role.User, Content = Chars(20) },
            AssistantMessage = new Message
            {
                Role = Role.Assistant,
                Content = string.Empty,
                ToolCalls = new List<ToolCall> { new() { Id = "c1", Name = "big", ArgumentsJson = bigArgs } }
            }
        };
        toolTurn.ToolMessages.Add(new Message
        {
            Role = Role.Tool,
            Content = string.Empty,
            ToolResults = new List<ToolResult> { ToolResult.FromObject("c1", "big", new { blob = Chars(400) }) }
        });
        manager.AddTurn(toolTurn);

        var result = manager.ExtractMessagesForNextTurn().ToList();

        // The oversized tool exchange forces older messages out; content-only estimation
        // would have wrongly kept everything.
        Assert.DoesNotContain(result, m => m.Role == Role.User && ReferenceEquals(m.Content, null));
        Assert.True(result.Count < 4);
    }
}
