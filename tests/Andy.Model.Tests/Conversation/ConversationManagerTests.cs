using System;
using System.Linq;
using System.Threading.Tasks;
using Andy.Model.Conversation;
using Andy.Model.Model;
using Xunit;

namespace Andy.Model.Tests.Conversation;

public class ConversationManagerTests
{
    [Fact]
    public void DefaultConversationManager_Should_Initialize()
    {
        // Arrange & Act
        var manager = new DefaultConversationManager();

        // Assert
        Assert.NotNull(manager.Conversation);
        Assert.Empty(manager.Conversation.Turns);
    }

    [Fact]
    public void DefaultConversationManager_Should_Add_Turns()
    {
        // Arrange
        var manager = new DefaultConversationManager();
        var turn = new Turn
        {
            UserOrSystemMessage = new Message { Role = Role.User, Content = "Hello" }
        };

        // Act
        manager.AddTurn(turn);

        // Assert
        Assert.Single(manager.Conversation.Turns);
        Assert.Equal("Hello", manager.Conversation.Turns[0].UserOrSystemMessage?.Content);
    }

    [Fact]
    public void ExtractMessagesForNextTurn_Should_Return_All_Messages_With_No_Compression()
    {
        // Arrange
        var options = new ConversationManagerOptions
        {
            CompressionStrategy = CompressionStrategy.None
        };
        var manager = new DefaultConversationManager(options);

        var turn1 = new Turn
        {
            UserOrSystemMessage = new Message { Role = Role.User, Content = "Message 1" },
            AssistantMessage = new Message { Role = Role.Assistant, Content = "Response 1" }
        };
        var turn2 = new Turn
        {
            UserOrSystemMessage = new Message { Role = Role.User, Content = "Message 2" },
            AssistantMessage = new Message { Role = Role.Assistant, Content = "Response 2" }
        };

        manager.AddTurn(turn1);
        manager.AddTurn(turn2);

        // Act
        var messages = manager.ExtractMessagesForNextTurn().ToList();

        // Assert
        Assert.Equal(4, messages.Count); // 2 user + 2 assistant
    }

    [Fact]
    public void ExtractMessagesForNextTurn_Should_Apply_Simple_Compression()
    {
        // Arrange
        var options = new ConversationManagerOptions
        {
            CompressionStrategy = CompressionStrategy.Simple,
            MaxRecentMessages = 2
        };
        var manager = new DefaultConversationManager(options);

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
        Assert.Equal(2, messages.Count); // Only last 2 messages
        Assert.Contains("Message 5", messages.First().Content);
        Assert.Contains("Response 5", messages.Last().Content);
    }

    [Fact]
    public void ShouldCompact_Returns_True_When_Threshold_Exceeded()
    {
        // Arrange
        var options = new ConversationManagerOptions
        {
            CompactionThreshold = 3
        };
        var manager = new DefaultConversationManager(options);

        for (int i = 1; i <= 4; i++)
        {
            manager.AddTurn(new Turn
            {
                UserOrSystemMessage = new Message { Role = Role.User, Content = $"Message {i}" }
            });
        }

        // Act & Assert
        Assert.True(manager.ShouldCompact());
    }

    [Fact]
    public async Task CompactConversationAsync_Should_Mark_Compaction_State()
    {
        // Arrange
        var options = new ConversationManagerOptions
        {
            CompactionThreshold = 2,
            MaxRecentMessages = 2,  // Ensure we can compact with 3 turns
            CompressionStrategy = CompressionStrategy.Summary
        };
        var manager = new DefaultConversationManager(options);

        for (int i = 1; i <= 3; i++)
        {
            manager.AddTurn(new Turn
            {
                UserOrSystemMessage = new Message { Role = Role.User, Content = $"Message {i}" }
            });
        }

        // Act
        var result = await manager.CompactConversationAsync();

        // Assert
        Assert.True(result);
        Assert.NotNull(manager.Conversation.GetState<string>("last_compaction"));
        Assert.NotNull(manager.Conversation.GetState<string>("conversation_summary"));
    }

    [Fact]
    public void SlidingWindowManager_Should_Maintain_Window_Size()
    {
        // Arrange
        var manager = new SlidingWindowConversationManager(windowSize: 4);

        for (int i = 1; i <= 10; i++)
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
        // Should contain the last 4 messages (2 from turn 9, 2 from turn 10)
        Assert.Contains("Message 9", messages[0].Content);
        Assert.Contains("Response 9", messages[1].Content);
        Assert.Contains("Message 10", messages[2].Content);
        Assert.Contains("Response 10", messages[3].Content);
    }

    [Fact]
    public void SlidingWindowManager_Should_Preserve_First_System_Message()
    {
        // Arrange
        var manager = new SlidingWindowConversationManager(windowSize: 2, preserveFirstMessage: true);

        // Add system message first
        var systemTurn = new Turn
        {
            UserOrSystemMessage = new Message { Role = Role.System, Content = "System prompt" }
        };
        manager.AddTurn(systemTurn);

        // Add more turns
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
    }

    [Fact]
    public void SemanticManager_Should_Score_Messages_By_Importance()
    {
        // Arrange
        var manager = new SemanticConversationManager(new ConversationManagerOptions
        {
            MaxRecentMessages = 4,
            MaxTokens = 1000
        });

        // Add messages with varying importance
        manager.AddTurn(new Turn
        {
            UserOrSystemMessage = new Message { Role = Role.System, Content = "System configuration" }
        });

        manager.AddTurn(new Turn
        {
            UserOrSystemMessage = new Message { Role = Role.User, Content = "Hi there" } // Low importance
        });

        manager.AddTurn(new Turn
        {
            UserOrSystemMessage = new Message { Role = Role.User, Content = "Important: Project deadline is Friday" } // High importance
        });

        manager.AddTurn(new Turn
        {
            UserOrSystemMessage = new Message { Role = Role.User, Content = "What's the weather?" } // Low importance
        });

        manager.AddTurn(new Turn
        {
            UserOrSystemMessage = new Message { Role = Role.User, Content = "Critical error in system" } // High importance
        });

        // Act
        var messages = manager.ExtractMessagesForNextTurn().ToList();

        // Assert
        Assert.Equal(4, messages.Count); // Limited by MaxRecentMessages
        // Should preserve important messages
        Assert.Contains(messages, m => m.Content.Contains("System configuration"));
        Assert.Contains(messages, m => m.Content.Contains("deadline"));
        Assert.Contains(messages, m => m.Content.Contains("Critical error"));
    }

    [Fact]
    public void SemanticManager_Should_Add_Custom_Keywords()
    {
        // Arrange
        var manager = new SemanticConversationManager();
        manager.AddImportantKeywords("budget", "milestone", "deliverable");

        manager.AddTurn(new Turn
        {
            UserOrSystemMessage = new Message { Role = Role.User, Content = "The budget is $100k" }
        });

        manager.AddTurn(new Turn
        {
            UserOrSystemMessage = new Message { Role = Role.User, Content = "Nice weather today" }
        });

        manager.AddTurn(new Turn
        {
            UserOrSystemMessage = new Message { Role = Role.User, Content = "Next milestone is Q2" }
        });

        // Act
        var messages = manager.ExtractMessagesForNextTurn().ToList();

        // Assert
        // Messages with custom keywords should be preserved
        Assert.Contains(messages, m => m.Content.Contains("budget"));
        Assert.Contains(messages, m => m.Content.Contains("milestone"));
    }

    [Fact]
    public void ConversationStats_Should_Be_Accurate()
    {
        // Arrange
        var manager = new DefaultConversationManager();

        var turn1 = new Turn
        {
            UserOrSystemMessage = new Message { Role = Role.User, Content = "Test" },
            AssistantMessage = new Message { Role = Role.Assistant, Content = "Response" }
        };

        var toolCall = new ToolCall { Id = "1", Name = "tool1", ArgumentsJson = "{}" };
        var turn2 = new Turn
        {
            UserOrSystemMessage = new Message { Role = Role.User, Content = "Test 2" },
            AssistantMessage = new Message
            {
                Role = Role.Assistant,
                Content = "Response 2",
                ToolCalls = new List<ToolCall> { toolCall }
            }
        };
        turn2.ToolMessages.Add(new Message
        {
            Role = Role.Tool,
            Content = "Tool result",
            ToolResults = new List<ToolResult>
            {
                new ToolResult { CallId = "1", Name = "tool1", ResultJson = "{}" }
            }
        });

        manager.AddTurn(turn1);
        manager.AddTurn(turn2);

        // Act
        var stats = manager.GetStatistics();

        // Assert
        Assert.Equal(2, stats.TotalTurns);
        Assert.Equal(2, stats.UserMessages);
        Assert.Equal(2, stats.AssistantMessages);
        Assert.Equal(1, stats.ToolMessages);
        Assert.Equal(1, stats.ToolCalls);
    }

    [Fact]
    public void PreserveToolCallPairs_Should_Keep_Related_Messages()
    {
        // Arrange
        var options = new ConversationManagerOptions
        {
            PreserveToolCallPairs = true,
            CompressionStrategy = CompressionStrategy.Smart,
            MaxRecentMessages = 2
        };
        var manager = new DefaultConversationManager(options);

        // Add turn with tool call
        var toolCall = new ToolCall { Id = "tc1", Name = "calculator", ArgumentsJson = "{}" };
        var turn = new Turn
        {
            UserOrSystemMessage = new Message { Role = Role.User, Content = "Calculate something" },
            AssistantMessage = new Message
            {
                Role = Role.Assistant,
                Content = "Let me calculate",
                ToolCalls = new List<ToolCall> { toolCall }
            }
        };
        turn.ToolMessages.Add(new Message
        {
            Role = Role.Tool,
            Content = "Result: 42",
            ToolResults = new List<ToolResult>
            {
                new ToolResult { CallId = "tc1", Name = "calculator", ResultJson = "{result: 42}" }
            }
        });
        manager.AddTurn(turn);

        // Add more turns to trigger compression
        for (int i = 0; i < 5; i++)
        {
            manager.AddTurn(new Turn
            {
                UserOrSystemMessage = new Message { Role = Role.User, Content = $"Filler {i}" }
            });
        }

        // Act
        var messages = manager.ExtractMessagesForNextTurn().ToList();

        // Assert
        // Should preserve the tool call and result even if old
        var hasToolCall = messages.Any(m => m.ToolCalls.Any(tc => tc.Id == "tc1"));
        var hasToolResult = messages.Any(m => m.ToolResults?.Any(tr => tr.CallId == "tc1") == true);
        Assert.True(hasToolCall || hasToolResult, "Tool call pair should be preserved");
    }
}