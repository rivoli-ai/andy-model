using System.Linq;
using System.Threading.Tasks;
using Andy.Model.Conversation;
using Andy.Model.Model;
using Xunit;

namespace Andy.Model.Tests.Conversation;

public class SlidingWindowConversationManagerTests
{
    [Fact]
    public void Constructor_SetsWindowSize()
    {
        // Arrange & Act
        var manager = new SlidingWindowConversationManager(windowSize: 6);

        // Assert
        Assert.NotNull(manager.Conversation);
        Assert.Empty(manager.Conversation.Turns);
    }

    [Fact]
    public void ExtractMessagesForNextTurn_MaintainsWindowSize()
    {
        // Arrange
        var manager = new SlidingWindowConversationManager(windowSize: 4);

        // Add 10 messages (5 turns)
        for (int i = 1; i <= 5; i++)
        {
            manager.AddTurn(new Turn
            {
                UserOrSystemMessage = new Message { Role = Role.User, Content = $"Message {i}" },
                AssistantMessage = new Message { Role = Role.Assistant, Content = $"Response {i}" }
            });
        }

        // Act
        var messages = manager.ExtractMessagesForNextTurn().ToList();

        // Assert
        Assert.Equal(4, messages.Count); // Window size
        // Should contain last 4 messages
        Assert.Contains("Message 4", messages[0].Content);
        Assert.Contains("Response 4", messages[1].Content);
        Assert.Contains("Message 5", messages[2].Content);
        Assert.Contains("Response 5", messages[3].Content);
    }

    [Fact]
    public void ExtractMessagesForNextTurn_PreservesFirstSystemMessage()
    {
        // Arrange
        var manager = new SlidingWindowConversationManager(
            windowSize: 2,
            preserveFirstMessage: true
        );

        // Add system message
        manager.AddTurn(new Turn
        {
            UserOrSystemMessage = new Message { Role = Role.System, Content = "System prompt" }
        });

        // Add many user messages
        for (int i = 1; i <= 5; i++)
        {
            manager.AddTurn(new Turn
            {
                UserOrSystemMessage = new Message { Role = Role.User, Content = $"Message {i}" }
            });
        }

        // Act
        var messages = manager.ExtractMessagesForNextTurn().ToList();

        // Assert
        Assert.Equal(3, messages.Count); // System + window (2)
        Assert.Equal(Role.System, messages[0].Role);
        Assert.Contains("System prompt", messages[0].Content);
        Assert.Contains("Message 4", messages[1].Content); // Second to last message
        Assert.Contains("Message 5", messages[2].Content); // Last message in window
    }

    [Fact]
    public void ExtractMessagesForNextTurn_DoesNotPreserveFirstMessage_WhenDisabled()
    {
        // Arrange
        var manager = new SlidingWindowConversationManager(
            windowSize: 2,
            preserveFirstMessage: false
        );

        // Add system message
        manager.AddTurn(new Turn
        {
            UserOrSystemMessage = new Message { Role = Role.System, Content = "System prompt" }
        });

        // Add many user messages
        for (int i = 1; i <= 5; i++)
        {
            manager.AddTurn(new Turn
            {
                UserOrSystemMessage = new Message { Role = Role.User, Content = $"Message {i}" }
            });
        }

        // Act
        var messages = manager.ExtractMessagesForNextTurn().ToList();

        // Assert
        Assert.Equal(2, messages.Count); // Only window
        Assert.DoesNotContain(messages, m => m.Role == Role.System);
    }

    [Fact]
    public async Task CompactConversationAsync_CreatesSummaryOfOldMessages()
    {
        // Arrange
        var manager = new SlidingWindowConversationManager(windowSize: 2);

        // Add messages that will fall out of window
        for (int i = 1; i <= 5; i++)
        {
            manager.AddTurn(new Turn
            {
                UserOrSystemMessage = new Message { Role = Role.User, Content = $"Question {i}" },
                AssistantMessage = new Message { Role = Role.Assistant, Content = $"Answer {i}" }
            });
        }

        // Act
        var compacted = await manager.CompactConversationAsync();

        // Assert
        Assert.True(compacted);

        // Next extraction should include summary
        var messages = manager.ExtractMessagesForNextTurn().ToList();
        var summaryMessage = messages.FirstOrDefault(m =>
            m.Role == Role.System && m.Content.Contains("Previous context summary"));
        Assert.NotNull(summaryMessage);
    }

    [Fact]
    public async Task CompactConversationAsync_ReturnsFalse_WhenNotNeeded()
    {
        // Arrange
        var manager = new SlidingWindowConversationManager(windowSize: 10);

        // Add only 1 message (within window)
        manager.AddTurn(new Turn
        {
            UserOrSystemMessage = new Message { Role = Role.User, Content = "Single message" }
        });

        // Act
        var compacted = await manager.CompactConversationAsync();

        // Assert
        Assert.False(compacted);
    }

    [Fact]
    public void SlidingWindow_WorksWithMixedMessageTypes()
    {
        // Arrange
        var manager = new SlidingWindowConversationManager(windowSize: 6);

        // Add turn with tool messages
        var turnWithTool = new Turn
        {
            UserOrSystemMessage = new Message { Role = Role.User, Content = "Calculate" },
            AssistantMessage = new Message
            {
                Role = Role.Assistant,
                Content = "Using tool",
                ToolCalls = new List<ToolCall>
                {
                    new ToolCall { Id = "1", Name = "calc", ArgumentsJson = "{}" }
                }
            }
        };
        turnWithTool.ToolMessages.Add(new Message
        {
            Role = Role.Tool,
            Content = "Result: 42",
            ToolResults = new List<ToolResult>
            {
                new ToolResult { CallId = "1", Name = "calc", ResultJson = "42" }
            }
        });
        manager.AddTurn(turnWithTool);

        // Add more regular turns
        for (int i = 1; i <= 3; i++)
        {
            manager.AddTurn(new Turn
            {
                UserOrSystemMessage = new Message { Role = Role.User, Content = $"Message {i}" },
                AssistantMessage = new Message { Role = Role.Assistant, Content = $"Response {i}" }
            });
        }

        // Act
        var messages = manager.ExtractMessagesForNextTurn().ToList();

        // Assert
        Assert.Equal(6, messages.Count); // Window size
        // Should include most recent messages including tool messages
        var toolMessages = messages.Where(m => m.Role == Role.Tool).ToList();
        Assert.Empty(toolMessages); // Tool message should be out of window
    }

    [Fact]
    public void ExtractMessagesForNextTurn_MaintainsSummaryQueue()
    {
        // Arrange
        var manager = new SlidingWindowConversationManager(windowSize: 2);

        // Add many messages to trigger multiple compactions
        for (int batch = 0; batch < 3; batch++)
        {
            for (int i = 1; i <= 3; i++)
            {
                manager.AddTurn(new Turn
                {
                    UserOrSystemMessage = new Message { Role = Role.User, Content = $"Batch {batch} Message {i}" }
                });
            }
        }

        // Act
        var messages = manager.ExtractMessagesForNextTurn().ToList();

        // Assert
        Assert.Equal(2, messages.Count); // Window size
        // Most recent messages should be in window
        Assert.Contains("Batch 2", messages[0].Content);
    }

    [Fact]
    public void WindowSize_RespectedWithSingleLongTurn()
    {
        // Arrange
        var manager = new SlidingWindowConversationManager(windowSize: 3);

        // Add a turn with multiple tool messages
        var turn = new Turn
        {
            UserOrSystemMessage = new Message { Role = Role.User, Content = "Complex request" },
            AssistantMessage = new Message { Role = Role.Assistant, Content = "Processing..." }
        };

        // Add multiple tool messages
        for (int i = 1; i <= 5; i++)
        {
            turn.ToolMessages.Add(new Message
            {
                Role = Role.Tool,
                Content = $"Tool result {i}",
                ToolResults = new List<ToolResult>
                {
                    new ToolResult { CallId = $"{i}", Name = $"tool{i}", ResultJson = "{}" }
                }
            });
        }
        manager.AddTurn(turn);

        // Act
        var messages = manager.ExtractMessagesForNextTurn().ToList();

        // Assert
        Assert.Equal(3, messages.Count); // Window size
        // Should keep only the last 3 messages
        Assert.Contains("Tool result 5", messages.Last().Content);
    }

    [Fact]
    public async Task GetConversationSummaryAsync_CreatesSummaryFromAllTurns()
    {
        // Arrange
        var manager = new SlidingWindowConversationManager(windowSize: 2);

        for (int i = 1; i <= 3; i++)
        {
            manager.AddTurn(new Turn
            {
                UserOrSystemMessage = new Message { Role = Role.User, Content = $"Question {i}" },
                AssistantMessage = new Message { Role = Role.Assistant, Content = $"Answer {i}" }
            });
        }

        // Act
        var summary = await manager.GetConversationSummaryAsync();

        // Assert
        Assert.NotNull(summary);
        Assert.Contains("3 turns", summary);
    }
}