using System.Text.Json;
using Andy.Model.Model;

namespace Andy.Model.Tests.Core;

/// <summary>
/// Tests for API convenience patterns and usage scenarios
/// </summary>
public class ApiConvenienceTests
{
    [Fact]
    public void CreateSimpleConversation_ShouldBeEasy()
    {
        // This test demonstrates how simple conversation creation should be
        // Arrange & Act
        var conversation = new Conversation();

        // Add messages in a natural way
        var systemTurn = CreateSystemTurn("You are a helpful assistant");
        var userTurn = CreateUserTurn("What's the capital of France?");

        conversation.AddTurn(systemTurn);
        conversation.AddTurn(userTurn);

        // Assert
        var messages = conversation.ToChronoMessages().ToArray();
        Assert.Equal(2, messages.Length); // System and User
        Assert.Equal("You are a helpful assistant", messages[0].Content);
        Assert.Equal("What's the capital of France?", messages[1].Content);
    }

    [Fact]
    public void ConversationHistory_ShouldMaintainChronology()
    {
        // Arrange
        var conversation = new Conversation();
        var turns = new List<string>();

        // Act - Build conversation with specific order
        for (int i = 1; i <= 5; i++)
        {
            var turn = new Turn
            {
                UserOrSystemMessage = new Message { Role = Role.User, Content = $"Question {i}" },
                AssistantMessage = new Message { Role = Role.Assistant, Content = $"Answer {i}" }
            };
            conversation.AddTurn(turn);
            turns.Add($"Question {i}");
            turns.Add($"Answer {i}");
        }

        // Assert - Verify chronological order
        var messages = conversation.ToChronoMessages().ToList();
        Assert.Equal(10, messages.Count); // 5 questions + 5 answers

        for (int i = 0; i < turns.Count; i++)
        {
            Assert.Equal(turns[i], messages[i].Content);
        }
    }

    [Fact]
    public void ToolExecution_WithError_ShouldBeHandledGracefully()
    {
        // Arrange
        var turn = new Turn
        {
            UserOrSystemMessage = new Message { Role = Role.User, Content = "Calculate something" },
            AssistantMessage = new Message
            {
                Role = Role.Assistant,
                Content = "I'll calculate",
                ToolCalls = new List<ToolCall>
                {
                    new() { Id = "calc_1", Name = "calculator", ArgumentsJson = "{\"expression\":\"1/0\"}" }
                }
            }
        };

        // Act - Simulate tool error
        var errorResult = ToolResult.FromObject("calc_1", "calculator",
            new { error = "Division by zero", code = "MATH_ERROR" },
            isError: true);

        turn.ToolMessages.Add(new Message
        {
            Role = Role.Tool,
            Content = JsonSerializer.Serialize(new { error = "Division by zero" }),
            ToolResults = new List<ToolResult> { errorResult }
        });

        // Assert
        var toolMessage = turn.ToolMessages[0];
        Assert.Single(toolMessage.ToolResults);
        Assert.True(toolMessage.ToolResults[0].IsError);
        Assert.Contains("Division by zero", toolMessage.ToolResults[0].ResultJson);
    }

    [Fact]
    public void MessageMetadata_ShouldPersistThroughProcessing()
    {
        // Arrange
        var message = new Message
        {
            Role = Role.Assistant,
            Content = "Response with metadata",
            Metadata = new Dictionary<string, object>
            {
                ["model"] = "gpt-4",
                ["temperature"] = 0.7,
                ["max_tokens"] = 500,
                ["timestamp"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                ["custom_field"] = "custom_value"
            }
        };

        var conversation = new Conversation();
        conversation.AddTurn(new Turn { AssistantMessage = message });

        // Act
        var retrieved = conversation.ToChronoMessages().First(m => m.Role == Role.Assistant);

        // Assert
        Assert.Equal(5, retrieved.Metadata.Count);
        Assert.Equal("gpt-4", retrieved.Metadata["model"]);
        Assert.Equal(0.7, retrieved.Metadata["temperature"]);
        Assert.Equal(500, retrieved.Metadata["max_tokens"]);
        Assert.Equal("custom_value", retrieved.Metadata["custom_field"]);
    }

    [Fact]
    public void ParallelToolCalls_ShouldBeSupported()
    {
        // Arrange
        var message = new Message
        {
            Role = Role.Assistant,
            Content = "I'll check weather and time simultaneously",
            ToolCalls = new List<ToolCall>
            {
                new()
                {
                    Id = "weather_call",
                    Name = "get_weather",
                    ArgumentsJson = JsonSerializer.Serialize(new { location = "Tokyo" })
                },
                new()
                {
                    Id = "time_call",
                    Name = "get_time",
                    ArgumentsJson = JsonSerializer.Serialize(new { timezone = "JST" })
                },
                new()
                {
                    Id = "news_call",
                    Name = "get_news",
                    ArgumentsJson = JsonSerializer.Serialize(new { category = "technology" })
                }
            }
        };

        // Act & Assert
        Assert.Equal(3, message.ToolCalls.Count);

        // Each call should be independent
        var calls = message.ToolCalls.Select(tc => tc.Name).ToList();
        Assert.Contains("get_weather", calls);
        Assert.Contains("get_time", calls);
        Assert.Contains("get_news", calls);

        // Each should have unique ID
        var ids = message.ToolCalls.Select(tc => tc.Id).ToList();
        Assert.Equal(3, ids.Distinct().Count());
    }

    // Helper methods to create turns/messages easily
    private static Turn CreateSystemTurn(string content)
    {
        return new Turn
        {
            UserOrSystemMessage = new Message
            {
                Role = Role.System,
                Content = content
            }
        };
    }

    private static Turn CreateUserTurn(string content)
    {
        return new Turn
        {
            UserOrSystemMessage = new Message
            {
                Role = Role.User,
                Content = content
            }
        };
    }
}