using System.Collections.Generic;
using System.Linq;
using Andy.Model.Conversation;
using Andy.Model.Model;
using Xunit;

namespace Andy.Model.Tests.Conversation;

public class SemanticConversationManagerTests
{
    [Fact]
    public void Constructor_InitializesWithDefaultKeywords()
    {
        // Arrange & Act
        var manager = new SemanticConversationManager();

        // Assert
        Assert.NotNull(manager.Conversation);
        Assert.Empty(manager.Conversation.Turns);
    }

    [Fact]
    public void AddImportantKeywords_AddsToKeywordList()
    {
        // Arrange
        var manager = new SemanticConversationManager();

        // Act
        manager.AddImportantKeywords("budget", "deadline", "milestone");

        // Add messages with keywords
        manager.AddTurn(new Turn
        {
            UserOrSystemMessage = new Message { Role = Role.User, Content = "The budget is $50k" }
        });
        manager.AddTurn(new Turn
        {
            UserOrSystemMessage = new Message { Role = Role.User, Content = "Random chat" }
        });
        manager.AddTurn(new Turn
        {
            UserOrSystemMessage = new Message { Role = Role.User, Content = "Deadline is next week" }
        });

        // Assert
        var messages = manager.ExtractMessagesForNextTurn().ToList();
        Assert.Contains(messages, m => m.Content.Contains("budget"));
        Assert.Contains(messages, m => m.Content.Contains("Deadline"));
    }

    [Fact]
    public void ExtractMessagesForNextTurn_PrioritizesImportantMessages()
    {
        // Arrange
        var options = new ConversationManagerOptions
        {
            MaxRecentMessages = 3,
            MaxTokens = 500
        };
        var manager = new SemanticConversationManager(options);

        // Add messages with varying importance
        manager.AddTurn(new Turn
        {
            UserOrSystemMessage = new Message { Role = Role.User, Content = "Hi there" } // Low
        });
        manager.AddTurn(new Turn
        {
            UserOrSystemMessage = new Message { Role = Role.User, Content = "Important: Project requirements" } // High
        });
        manager.AddTurn(new Turn
        {
            UserOrSystemMessage = new Message { Role = Role.User, Content = "Nice weather" } // Low
        });
        manager.AddTurn(new Turn
        {
            UserOrSystemMessage = new Message { Role = Role.User, Content = "Critical error occurred" } // High
        });
        manager.AddTurn(new Turn
        {
            UserOrSystemMessage = new Message { Role = Role.User, Content = "How are you?" } // Low
        });

        // Act
        var messages = manager.ExtractMessagesForNextTurn().ToList();

        // Assert
        Assert.Equal(3, messages.Count); // MaxRecentMessages
        // Should include important messages
        Assert.Contains(messages, m => m.Content.Contains("requirements"));
        Assert.Contains(messages, m => m.Content.Contains("Critical error"));
    }

    [Fact]
    public void ExtractMessagesForNextTurn_PreservesSystemMessages()
    {
        // Arrange
        var options = new ConversationManagerOptions
        {
            MaxRecentMessages = 2
        };
        var manager = new SemanticConversationManager(options);

        // Add system message (should have high score)
        manager.AddTurn(new Turn
        {
            UserOrSystemMessage = new Message { Role = Role.System, Content = "System configuration" }
        });

        // Add many low-importance messages
        for (int i = 1; i <= 5; i++)
        {
            manager.AddTurn(new Turn
            {
                UserOrSystemMessage = new Message { Role = Role.User, Content = $"Chat message {i}" }
            });
        }

        // Act
        var messages = manager.ExtractMessagesForNextTurn().ToList();

        // Assert
        // System message should be preserved due to high importance score
        Assert.Contains(messages, m => m.Role == Role.System);
    }

    [Fact]
    public void ExtractMessagesForNextTurn_PreservesToolCallPairs()
    {
        // Arrange
        var options = new ConversationManagerOptions
        {
            MaxRecentMessages = 3,
            PreserveToolCallPairs = true
        };
        var manager = new SemanticConversationManager(options);

        // Add turn with tool call
        var toolCall = new ToolCall { Id = "tc1", Name = "calculator", ArgumentsJson = "{}" };
        var turnWithTool = new Turn
        {
            UserOrSystemMessage = new Message { Role = Role.User, Content = "Calculate something" },
            AssistantMessage = new Message
            {
                Role = Role.Assistant,
                Content = "Calculating",
                ToolCalls = new List<ToolCall> { toolCall }
            }
        };
        turnWithTool.ToolMessages.Add(new Message
        {
            Role = Role.Tool,
            Content = "Result: 42",
            ToolResults = new List<ToolResult>
            {
                new ToolResult { CallId = "tc1", Name = "calculator", ResultJson = "42" }
            }
        });
        manager.AddTurn(turnWithTool);

        // Add more messages
        for (int i = 1; i <= 3; i++)
        {
            manager.AddTurn(new Turn
            {
                UserOrSystemMessage = new Message { Role = Role.User, Content = $"Message {i}" }
            });
        }

        // Act
        var messages = manager.ExtractMessagesForNextTurn().ToList();

        // Assert
        // If assistant message with tool call is selected, tool result should also be included
        var hasToolCall = messages.Any(m => m.ToolCalls?.Any(tc => tc.Id == "tc1") == true);
        if (hasToolCall)
        {
            var hasToolResult = messages.Any(m => m.ToolResults?.Any(tr => tr.CallId == "tc1") == true);
            Assert.True(hasToolResult);
        }
    }

    [Fact]
    public void ImportanceScoring_FavorsRecentMessages()
    {
        // Arrange
        var options = new ConversationManagerOptions
        {
            MaxRecentMessages = 2
        };
        var manager = new SemanticConversationManager(options);

        // Add old important message
        manager.AddTurn(new Turn
        {
            UserOrSystemMessage = new Message { Role = Role.User, Content = "Critical: Old information" }
        });

        // Add several filler messages
        for (int i = 1; i <= 3; i++)
        {
            manager.AddTurn(new Turn
            {
                UserOrSystemMessage = new Message { Role = Role.User, Content = $"Filler {i}" }
            });
        }

        // Add recent less important message
        manager.AddTurn(new Turn
        {
            UserOrSystemMessage = new Message { Role = Role.User, Content = "Recent message" }
        });

        // Act
        var messages = manager.ExtractMessagesForNextTurn().ToList();

        // Assert
        Assert.Equal(2, messages.Count);
        // Recent message should be included due to recency score
        Assert.Contains(messages, m => m.Content.Contains("Recent message"));
    }

    [Fact]
    public void ImportanceScoring_FavorsLongMessages()
    {
        // Arrange
        var options = new ConversationManagerOptions
        {
            MaxRecentMessages = 2
        };
        var manager = new SemanticConversationManager(options);

        // Add short message
        manager.AddTurn(new Turn
        {
            UserOrSystemMessage = new Message { Role = Role.User, Content = "Hi" }
        });

        // Add long message
        var longContent = string.Join(" ", Enumerable.Range(1, 100).Select(i => $"Word{i}"));
        manager.AddTurn(new Turn
        {
            UserOrSystemMessage = new Message { Role = Role.User, Content = longContent }
        });

        // Add another short message
        manager.AddTurn(new Turn
        {
            UserOrSystemMessage = new Message { Role = Role.User, Content = "Bye" }
        });

        // Act
        var messages = manager.ExtractMessagesForNextTurn().ToList();

        // Assert
        Assert.Equal(2, messages.Count);
        // Long message should be included due to length scoring
        Assert.Contains(messages, m => m.Content.Length > 500);
    }

    [Fact]
    public void ImportanceScoring_FavorsErrorMessages()
    {
        // Arrange
        var options = new ConversationManagerOptions
        {
            MaxRecentMessages = 2
        };
        var manager = new SemanticConversationManager(options);

        // Add regular messages
        for (int i = 1; i <= 3; i++)
        {
            manager.AddTurn(new Turn
            {
                UserOrSystemMessage = new Message { Role = Role.User, Content = $"Normal message {i}" }
            });
        }

        // Add error message
        manager.AddTurn(new Turn
        {
            UserOrSystemMessage = new Message
            {
                Role = Role.User,
                Content = "An error occurred",
                Metadata = new Dictionary<string, object> { ["is_error"] = true }
            }
        });

        // Act
        var messages = manager.ExtractMessagesForNextTurn().ToList();

        // Assert
        // Error message should be included due to error scoring
        Assert.Contains(messages, m => m.Content.Contains("error occurred"));
    }

    [Fact]
    public void ExtractMessagesForNextTurn_RespectsTokenBudget()
    {
        // Arrange
        var options = new ConversationManagerOptions
        {
            MaxTokens = 100, // Very small budget (~400 characters)
            MaxRecentMessages = 10
        };
        var manager = new SemanticConversationManager(options);

        // Add many messages
        for (int i = 1; i <= 5; i++)
        {
            var content = string.Join(" ", Enumerable.Range(1, 50).Select(x => $"Word{x}"));
            manager.AddTurn(new Turn
            {
                UserOrSystemMessage = new Message { Role = Role.User, Content = content }
            });
        }

        // Act
        var messages = manager.ExtractMessagesForNextTurn().ToList();

        // Assert
        // Should have limited messages due to token budget
        var totalChars = messages.Sum(m => m.Content.Length);
        Assert.True(totalChars <= 400); // ~100 tokens * 4 chars/token
    }

    [Fact]
    public void ExtractMessagesForNextTurn_MaintainsChronologicalOrder()
    {
        // Arrange
        var manager = new SemanticConversationManager(new ConversationManagerOptions
        {
            MaxRecentMessages = 5
        });

        // Add messages
        for (int i = 1; i <= 5; i++)
        {
            manager.AddTurn(new Turn
            {
                UserOrSystemMessage = new Message
                {
                    Role = Role.User,
                    Content = $"Message {i}",
                    Timestamp = System.DateTimeOffset.UtcNow.AddMinutes(i)
                }
            });
        }

        // Act
        var messages = manager.ExtractMessagesForNextTurn().ToList();

        // Assert
        // Messages should be in chronological order
        for (int i = 1; i < messages.Count; i++)
        {
            Assert.True(messages[i].Timestamp >= messages[i - 1].Timestamp);
        }
    }

    [Fact]
    public void DefaultKeywords_IncludeCommonImportantTerms()
    {
        // Arrange
        var manager = new SemanticConversationManager(new ConversationManagerOptions
        {
            MaxRecentMessages = 3
        });

        // Add messages with default keywords
        manager.AddTurn(new Turn
        {
            UserOrSystemMessage = new Message { Role = Role.User, Content = "This is important information" }
        });
        manager.AddTurn(new Turn
        {
            UserOrSystemMessage = new Message { Role = Role.User, Content = "Random chat message" }
        });
        manager.AddTurn(new Turn
        {
            UserOrSystemMessage = new Message { Role = Role.User, Content = "Critical issue found" }
        });
        manager.AddTurn(new Turn
        {
            UserOrSystemMessage = new Message { Role = Role.User, Content = "Another random message" }
        });

        // Act
        var messages = manager.ExtractMessagesForNextTurn().ToList();

        // Assert
        // Messages with default keywords should be prioritized
        Assert.Contains(messages, m => m.Content.Contains("important"));
        Assert.Contains(messages, m => m.Content.Contains("Critical"));
    }
}