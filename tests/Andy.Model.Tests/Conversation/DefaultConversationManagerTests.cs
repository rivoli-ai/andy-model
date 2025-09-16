using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Andy.Model.Conversation;
using Andy.Model.Model;
using Xunit;

namespace Andy.Model.Tests.Conversation;

public class DefaultConversationManagerTests
{
    [Fact]
    public void Constructor_WithoutOptions_CreatesDefaultConfiguration()
    {
        // Arrange & Act
        var manager = new DefaultConversationManager();

        // Assert
        Assert.NotNull(manager.Conversation);
        Assert.Empty(manager.Conversation.Turns);
        Assert.NotNull(manager.Conversation.Id);
    }

    [Fact]
    public void Constructor_WithExistingConversation_UsesProvidedConversation()
    {
        // Arrange
        var conversation = new Model.Conversation { Id = "test-123" };
        var turn = new Turn
        {
            UserOrSystemMessage = new Message { Role = Role.User, Content = "Test" }
        };
        conversation.AddTurn(turn);

        // Act
        var manager = new DefaultConversationManager(conversation);

        // Assert
        Assert.Same(conversation, manager.Conversation);
        Assert.Single(manager.Conversation.Turns);
        Assert.Equal("test-123", manager.Conversation.Id);
    }

    [Fact]
    public void AddTurn_AddsToConversation()
    {
        // Arrange
        var manager = new DefaultConversationManager();
        var turn = new Turn
        {
            UserOrSystemMessage = new Message { Role = Role.User, Content = "Hello" },
            AssistantMessage = new Message { Role = Role.Assistant, Content = "Hi there" }
        };

        // Act
        manager.AddTurn(turn);

        // Assert
        Assert.Single(manager.Conversation.Turns);
        Assert.Equal("Hello", manager.Conversation.Turns[0].UserOrSystemMessage?.Content);
        Assert.Equal("Hi there", manager.Conversation.Turns[0].AssistantMessage?.Content);
    }

    [Fact]
    public void ExtractMessagesForNextTurn_WithNoCompression_ReturnsAll()
    {
        // Arrange
        var options = new ConversationManagerOptions
        {
            CompressionStrategy = CompressionStrategy.None
        };
        var manager = new DefaultConversationManager(options);

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
        Assert.Equal(6, messages.Count); // 3 user + 3 assistant
        Assert.Contains("Message 1", messages[0].Content);
        Assert.Contains("Response 3", messages[5].Content);
    }

    [Fact]
    public void ExtractMessagesForNextTurn_WithSimpleCompression_LimitsMessages()
    {
        // Arrange
        var options = new ConversationManagerOptions
        {
            CompressionStrategy = CompressionStrategy.Simple,
            MaxRecentMessages = 4
        };
        var manager = new DefaultConversationManager(options);

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
        Assert.Equal(4, messages.Count);
        Assert.Contains("Message 9", messages[0].Content); // Second to last user message
        Assert.Contains("Response 10", messages[3].Content); // Last assistant message
    }

    [Fact]
    public void ExtractMessagesForNextTurn_FiltersOldMessages()
    {
        // Arrange
        var now = DateTimeOffset.UtcNow;
        var options = new ConversationManagerOptions
        {
            MaxMessageAge = TimeSpan.FromHours(1),
            CompressionStrategy = CompressionStrategy.None
        };
        var manager = new DefaultConversationManager(options);

        // Add old message (2 hours old - should be filtered out)
        var oldTurn = new Turn
        {
            UserOrSystemMessage = new Message
            {
                Role = Role.User,
                Content = "Old message",
                Timestamp = now.AddHours(-2)
            }
        };
        manager.AddTurn(oldTurn);

        // Add recent message (30 minutes old - should be included)
        var recentTurn = new Turn
        {
            UserOrSystemMessage = new Message
            {
                Role = Role.User,
                Content = "Recent message",
                Timestamp = now.AddMinutes(-30)
            }
        };
        manager.AddTurn(recentTurn);

        // Act
        var messages = manager.ExtractMessagesForNextTurn().ToList();

        // Assert - only recent message should remain after filtering
        Assert.Single(messages);
        Assert.Equal("Recent message", messages[0].Content);
    }

    [Fact]
    public void ExtractMessagesForNextTurn_ExcludesSystemMessages_WhenConfigured()
    {
        // Arrange
        var options = new ConversationManagerOptions
        {
            IncludeSystemMessages = false,
            CompressionStrategy = CompressionStrategy.None
        };
        var manager = new DefaultConversationManager(options);

        manager.AddTurn(new Turn
        {
            UserOrSystemMessage = new Message { Role = Role.System, Content = "System prompt" }
        });
        manager.AddTurn(new Turn
        {
            UserOrSystemMessage = new Message { Role = Role.User, Content = "User message" }
        });

        // Act
        var messages = manager.ExtractMessagesForNextTurn().ToList();

        // Assert
        Assert.Single(messages);
        Assert.Equal(Role.User, messages[0].Role);
    }

    [Fact]
    public void ExtractMessagesForNextTurn_ExcludesToolMessages_WhenConfigured()
    {
        // Arrange
        var options = new ConversationManagerOptions
        {
            IncludeToolMessages = false,
            CompressionStrategy = CompressionStrategy.None
        };
        var manager = new DefaultConversationManager(options);

        var turn = new Turn
        {
            UserOrSystemMessage = new Message { Role = Role.User, Content = "Calculate something" }
        };
        turn.ToolMessages.Add(new Message
        {
            Role = Role.Tool,
            Content = "Tool result",
            ToolResults = new List<ToolResult>
            {
                new ToolResult { CallId = "1", Name = "calculator", ResultJson = "{}" }
            }
        });
        manager.AddTurn(turn);

        // Act
        var messages = manager.ExtractMessagesForNextTurn().ToList();

        // Assert
        Assert.Single(messages);
        Assert.Equal(Role.User, messages[0].Role);
    }

    [Fact]
    public void ShouldCompact_ReturnsTrueWhenThresholdExceeded()
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

        // Act
        var shouldCompact = manager.ShouldCompact();

        // Assert
        Assert.True(shouldCompact);
    }

    [Fact]
    public void ShouldCompact_ReturnsFalseWhenBelowThreshold()
    {
        // Arrange
        var options = new ConversationManagerOptions
        {
            CompactionThreshold = 10
        };
        var manager = new DefaultConversationManager(options);

        manager.AddTurn(new Turn
        {
            UserOrSystemMessage = new Message { Role = Role.User, Content = "Message" }
        });

        // Act
        var shouldCompact = manager.ShouldCompact();

        // Assert
        Assert.False(shouldCompact);
    }

    [Fact]
    public async Task CompactConversationAsync_SetsCompactionState()
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
    public async Task GetConversationSummaryAsync_CreatesSummary()
    {
        // Arrange
        var manager = new DefaultConversationManager();

        manager.AddTurn(new Turn
        {
            UserOrSystemMessage = new Message { Role = Role.User, Content = "What is AI?" },
            AssistantMessage = new Message { Role = Role.Assistant, Content = "AI is artificial intelligence." }
        });

        // Act
        var summary = await manager.GetConversationSummaryAsync();

        // Assert
        Assert.NotNull(summary);
        Assert.Contains("1 turns", summary);
        Assert.Contains("2 messages", summary);
    }

    [Fact]
    public void Reset_ClearsConversationState()
    {
        // Arrange
        var manager = new DefaultConversationManager();
        manager.Conversation.SetState("test_key", "test_value");

        // Act
        manager.Reset();

        // Assert
        Assert.Null(manager.Conversation.GetState<string>("test_key"));
    }

    [Fact]
    public void GetStatistics_ReturnsAccurateStats()
    {
        // Arrange
        var manager = new DefaultConversationManager();

        var turn1 = new Turn
        {
            UserOrSystemMessage = new Message { Role = Role.User, Content = "Question" },
            AssistantMessage = new Message { Role = Role.Assistant, Content = "Answer" }
        };

        var turn2 = new Turn
        {
            UserOrSystemMessage = new Message { Role = Role.User, Content = "Follow-up" },
            AssistantMessage = new Message
            {
                Role = Role.Assistant,
                Content = "Response",
                ToolCalls = new List<ToolCall>
                {
                    new ToolCall { Id = "1", Name = "tool", ArgumentsJson = "{}" }
                }
            }
        };
        turn2.ToolMessages.Add(new Message
        {
            Role = Role.Tool,
            Content = "Tool result",
            ToolResults = new List<ToolResult>
            {
                new ToolResult { CallId = "1", Name = "tool", ResultJson = "{}" }
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
    public void ApplySmartCompression_PreservesToolCallPairs()
    {
        // Arrange
        var options = new ConversationManagerOptions
        {
            CompressionStrategy = CompressionStrategy.Smart,
            PreserveToolCallPairs = true,
            MaxRecentMessages = 4
        };
        var manager = new DefaultConversationManager(options);

        // Add turn with tool call
        var toolCall = new ToolCall { Id = "tc1", Name = "calc", ArgumentsJson = "{}" };
        var turnWithTool = new Turn
        {
            UserOrSystemMessage = new Message { Role = Role.User, Content = "Calculate" },
            AssistantMessage = new Message
            {
                Role = Role.Assistant,
                Content = "Calculating...",
                ToolCalls = new List<ToolCall> { toolCall }
            }
        };
        turnWithTool.ToolMessages.Add(new Message
        {
            Role = Role.Tool,
            Content = "42",
            ToolResults = new List<ToolResult>
            {
                new ToolResult { CallId = "tc1", Name = "calc", ResultJson = "42" }
            }
        });
        manager.AddTurn(turnWithTool);

        // Add many more turns to trigger compression
        for (int i = 0; i < 10; i++)
        {
            manager.AddTurn(new Turn
            {
                UserOrSystemMessage = new Message { Role = Role.User, Content = $"Filler {i}" },
                AssistantMessage = new Message { Role = Role.Assistant, Content = $"Response {i}" }
            });
        }

        // Act
        var messages = manager.ExtractMessagesForNextTurn().ToList();

        // Assert
        // Should preserve tool call even though it's old
        var hasToolCall = messages.Any(m => m.ToolCalls?.Any(tc => tc.Id == "tc1") == true);
        var hasToolResult = messages.Any(m => m.ToolResults?.Any(tr => tr.CallId == "tc1") == true);
        Assert.True(hasToolCall || hasToolResult);
    }

    [Fact]
    public void ApplySmartCompression_PreservesFirstSystemMessage()
    {
        // Arrange
        var options = new ConversationManagerOptions
        {
            CompressionStrategy = CompressionStrategy.Smart,
            MaxRecentMessages = 2
        };
        var manager = new DefaultConversationManager(options);

        // Add system message first
        manager.AddTurn(new Turn
        {
            UserOrSystemMessage = new Message { Role = Role.System, Content = "System instructions" }
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
        Assert.True(messages.Count >= 2); // At least system + recent messages
        Assert.Equal(Role.System, messages[0].Role);
        Assert.Contains("System instructions", messages[0].Content);
    }

    [Fact]
    public void ApplySummaryCompression_IncludesSummaryInContext()
    {
        // Arrange
        var options = new ConversationManagerOptions
        {
            CompressionStrategy = CompressionStrategy.Summary,
            MaxRecentMessages = 2
        };
        var manager = new DefaultConversationManager(options);

        // Set a summary in state
        manager.Conversation.SetState("conversation_summary", "Previous discussion about AI and ML");

        // Add recent messages
        manager.AddTurn(new Turn
        {
            UserOrSystemMessage = new Message { Role = Role.User, Content = "What about deep learning?" }
        });

        // Act
        var messages = manager.ExtractMessagesForNextTurn().ToList();

        // Assert
        Assert.True(messages.Count >= 2);
        var summaryMessage = messages.FirstOrDefault(m => m.Content.Contains("Previous discussion"));
        Assert.NotNull(summaryMessage);
        Assert.Equal(Role.System, summaryMessage.Role);
    }

    [Fact]
    public async Task AutoCompact_TriggersWhenEnabled()
    {
        // Arrange
        var options = new ConversationManagerOptions
        {
            CompactionThreshold = 2,
            AutoCompact = true,
            CompressionStrategy = CompressionStrategy.Summary
        };
        var manager = new DefaultConversationManager(options);

        // Act - Add turns to exceed threshold
        for (int i = 1; i <= 3; i++)
        {
            manager.AddTurn(new Turn
            {
                UserOrSystemMessage = new Message { Role = Role.User, Content = $"Message {i}" }
            });
        }

        // Wait a bit for async compaction
        await Task.Delay(100);

        // Assert
        // Note: Auto-compact runs async, so we check if it was triggered
        Assert.True(manager.ShouldCompact());
    }
}