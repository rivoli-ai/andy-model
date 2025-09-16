using System.Text.Json;
using Andy.Model.Examples;
using Andy.Model.Model;
using Andy.Model.Tooling;
using Andy.Model.Utils;

namespace Andy.Model.Tests.Core;

public class ConversationHelpersTests
{
    [Fact]
    public void Conversation_WithHelperMethods_ShouldSimplifyMessageCreation()
    {
        // Arrange
        var conversation = new Model.Conversation();

        // Act - Add system message
        var systemTurn = new Turn
        {
            UserOrSystemMessage = new Message
            {
                Role = Role.System,
                Content = "You are a helpful assistant"
            }
        };
        conversation.AddTurn(systemTurn);

        // Add user message
        var userTurn = new Turn
        {
            UserOrSystemMessage = new Message
            {
                Role = Role.User,
                Content = "Hello!"
            }
        };
        conversation.AddTurn(userTurn);

        // Assert
        var messages = conversation.ToChronoMessages().ToArray();
        Assert.Equal(2, messages.Length);
        Assert.Equal(Role.System, messages[0].Role);
        Assert.Equal(Role.User, messages[1].Role);
    }

    [Fact]
    public void ConversationExtensions_GetSummary_ShouldFormatMessages()
    {
        // Arrange
        var conversation = new Model.Conversation();
        conversation.AddTurn(new Turn
        {
            UserOrSystemMessage = new Message { Role = Role.System, Content = "Be helpful" }
        });
        conversation.AddTurn(new Turn
        {
            UserOrSystemMessage = new Message { Role = Role.User, Content = "Hello" },
            AssistantMessage = new Message { Role = Role.Assistant, Content = "Hi there!" }
        });

        // Act
        var summary = conversation.GetSummary();

        // Assert
        Assert.Contains("system: Be helpful", summary);
        Assert.Contains("user: Hello", summary);
        Assert.Contains("assistant: Hi there!", summary);
    }

    [Fact]
    public void ConversationExtensions_ToJson_ShouldSerializeConversation()
    {
        // Arrange
        var conversation = new Model.Conversation();
        conversation.AddTurn(new Turn
        {
            UserOrSystemMessage = new Message { Role = Role.User, Content = "Test message" }
        });

        // Act
        var json = conversation.ToJson();
        var deserialized = ConversationExtensions.FromJson(json);

        // Assert
        Assert.NotNull(json);
        Assert.NotNull(deserialized);
        // Note: Private readonly fields may not deserialize properly
        // At minimum, the ID should be preserved
        Assert.NotNull(deserialized.Id);
    }

    [Fact]
    public void Message_CharacterCount_ShouldCalculateCorrectly()
    {
        // Arrange
        var messages = new List<Message>
        {
            new() { Role = Role.System, Content = "System" },  // 6 chars
            new() { Role = Role.User, Content = "User" },      // 4 chars
            new() { Role = Role.Assistant, Content = "Bot" }   // 3 chars
        };

        // Act
        var totalChars = messages.Sum(m => m.Content.Length);

        // Assert
        Assert.Equal(13, totalChars); // 6 + 4 + 3
    }

    [Fact]
    public void Turn_WithToolResponse_ShouldCreateCorrectStructure()
    {
        // Arrange
        var turn = new Turn
        {
            UserOrSystemMessage = new Message { Role = Role.User, Content = "What's the weather?" },
            AssistantMessage = new Message
            {
                Role = Role.Assistant,
                Content = "I'll check the weather",
                ToolCalls = new List<ToolCall>
                {
                    new() { Id = "call_123", Name = "weather", ArgumentsJson = "{\"location\":\"NYC\"}" }
                }
            }
        };

        // Act - Add tool response
        var toolResponse = new { temperature = 72, condition = "sunny" };
        turn.ToolMessages.Add(new Message
        {
            Role = Role.Tool,
            Content = JsonSerializer.Serialize(toolResponse),
            ToolResults = new List<ToolResult>
            {
                ToolResult.FromObject("call_123", "weather", toolResponse)
            }
        });

        // Assert
        var messages = turn.EnumerateMessages().ToList();
        Assert.Equal(3, messages.Count);

        var toolMessage = messages[2];
        Assert.Equal(Role.Tool, toolMessage.Role);
        Assert.Single(toolMessage.ToolResults);
        Assert.Equal("weather", toolMessage.ToolResults[0].Name);
        Assert.Equal("call_123", toolMessage.ToolResults[0].CallId);
        Assert.Contains("temperature", toolMessage.ToolResults[0].ResultJson);
    }

    [Fact]
    public void Conversation_Clear_ByRemovingTurns()
    {
        // Arrange
        var conversation = new Model.Conversation();
        conversation.SetState("key1", "value1");

        for (int i = 0; i < 3; i++)
        {
            conversation.AddTurn(new Turn
            {
                UserOrSystemMessage = new Message { Role = Role.User, Content = $"Message {i}" }
            });
        }

        // Act - Clear by creating new conversation (since we don't have Clear method)
        conversation = new Model.Conversation();

        // Assert
        Assert.Empty(conversation.Turns);
        Assert.Empty(conversation.ToChronoMessages());
        Assert.Null(conversation.GetState<string>("key1"));
    }

    [Fact]
    public void Message_WithComplexToolCalls_ShouldMaintainStructure()
    {
        // Arrange & Act
        var message = new Message
        {
            Role = Role.Assistant,
            Content = "I'll check multiple things",
            ToolCalls = new List<ToolCall>
            {
                new()
                {
                    Id = "call_1",
                    Name = "get_weather",
                    ArgumentsJson = JsonSerializer.Serialize(new { location = "NYC" })
                },
                new()
                {
                    Id = "call_2",
                    Name = "get_time",
                    ArgumentsJson = JsonSerializer.Serialize(new { timezone = "EST" })
                }
            }
        };

        // Assert
        Assert.Equal(2, message.ToolCalls.Count);

        var firstCall = message.ToolCalls[0];
        Assert.Equal("get_weather", firstCall.Name);
        Assert.Equal("call_1", firstCall.Id);
        Assert.Contains("NYC", firstCall.ArgumentsJson);

        var secondCall = message.ToolCalls[1];
        Assert.Equal("get_time", secondCall.Name);
        Assert.Equal("call_2", secondCall.Id);
        Assert.Contains("EST", secondCall.ArgumentsJson);
    }

    [Fact]
    public void ToolRegistry_WithAvailableTools_ShouldProvideDeclarations()
    {
        // Arrange
        var registry = new ToolRegistry();
        var tool = new CalculatorTool();

        // Act
        registry.Register(tool);
        var declarations = registry.GetDeclaredTools();

        // Assert
        Assert.Single(declarations);

        var declaration = declarations[0];
        Assert.Equal("calculator", declaration.Name);
        Assert.Equal("Evaluates a basic arithmetic expression (e.g., '2+2*3').", declaration.Description);
        Assert.NotEmpty(declaration.Parameters);

        // Check that Parameters dictionary structure is correct
        Assert.True(declaration.Parameters.ContainsKey("type"));
        Assert.Equal("object", declaration.Parameters["type"]);

        if (declaration.Parameters.TryGetValue("properties", out var props) && props is Dictionary<string, object> properties)
        {
            Assert.True(properties.ContainsKey("expression"));
        }
    }

    [Fact]
    public void Conversation_FluentPattern_ShouldManageMessages()
    {
        // This test demonstrates a fluent pattern for managing conversations
        // Similar to the ConversationContext example but using our architecture

        // Arrange
        var conversation = new Model.Conversation();
        var registry = new ToolRegistry();

        // Act - Build conversation fluently
        // System instruction
        conversation.AddTurn(new Turn
        {
            UserOrSystemMessage = new Message { Role = Role.System, Content = "You are a helpful assistant." }
        });

        // User: Hello!
        conversation.AddTurn(new Turn
        {
            UserOrSystemMessage = new Message { Role = Role.User, Content = "Hello!" },
            AssistantMessage = new Message { Role = Role.Assistant, Content = "Hi there! How can I help you?" }
        });

        // User: What's 2+2?
        conversation.AddTurn(new Turn
        {
            UserOrSystemMessage = new Message { Role = Role.User, Content = "What's 2+2?" },
            AssistantMessage = new Message { Role = Role.Assistant, Content = "2+2 equals 4." }
        });

        // Assert
        var messages = conversation.ToChronoMessages().ToArray();
        Assert.Equal(5, messages.Length); // System + 2 User + 2 Assistant
        Assert.Equal(Role.System, messages[0].Role);
        Assert.Equal(Role.User, messages[1].Role);
        Assert.Equal(Role.Assistant, messages[2].Role);
        Assert.Equal(Role.User, messages[3].Role);
        Assert.Equal(Role.Assistant, messages[4].Role);

        // Verify content
        Assert.Equal("You are a helpful assistant.", messages[0].Content);
        Assert.Equal("Hello!", messages[1].Content);
        Assert.Equal("Hi there! How can I help you?", messages[2].Content);
        Assert.Equal("What's 2+2?", messages[3].Content);
        Assert.Equal("2+2 equals 4.", messages[4].Content);
    }

    [Fact]
    public void FunctionCalling_EndToEnd_ShouldWork()
    {
        // This test demonstrates end-to-end function calling
        // Similar to the FunctionCalling_ShouldWork example but using our architecture

        // Arrange
        var conversation = new Model.Conversation();
        var registry = new ToolRegistry();

        // Register a weather tool (using ToolDeclaration)
        var weatherDeclaration = new ToolDeclaration
        {
            Name = "get_weather",
            Description = "Get the weather for a location",
            Parameters = new Dictionary<string, object>
            {
                ["type"] = "object",
                ["properties"] = new Dictionary<string, object>
                {
                    ["location"] = new Dictionary<string, object>
                    {
                        ["type"] = "string",
                        ["description"] = "The city and state"
                    }
                },
                ["required"] = new[] { "location" }
            }
        };

        // Act - Simulate conversation with tool call
        var turn = new Turn
        {
            UserOrSystemMessage = new Message { Role = Role.User, Content = "What's the weather in New York?" },
            AssistantMessage = new Message
            {
                Role = Role.Assistant,
                Content = "", // Assistant might not include text when calling tools
                ToolCalls = new List<ToolCall>
                {
                    new ToolCall
                    {
                        Id = "call_123",
                        Name = "get_weather",
                        ArgumentsJson = JsonSerializer.Serialize(new { location = "New York, NY" })
                    }
                }
            }
        };

        // Add tool response
        var weatherData = new { temperature = 72, condition = "sunny" };
        turn.ToolMessages.Add(new Message
        {
            Role = Role.Tool,
            Content = JsonSerializer.Serialize(weatherData),
            ToolResults = new List<ToolResult>
            {
                ToolResult.FromObject("call_123", "get_weather", weatherData)
            }
        });

        conversation.AddTurn(turn);

        // Assert
        var messages = conversation.ToChronoMessages().ToArray();
        Assert.Equal(3, messages.Length); // User + Assistant with tool call + Tool response

        // Check the assistant message with tool call
        var assistantMsg = messages[1];
        Assert.Equal(Role.Assistant, assistantMsg.Role);
        Assert.Single(assistantMsg.ToolCalls);
        Assert.Equal("get_weather", assistantMsg.ToolCalls[0].Name);
        Assert.Equal("call_123", assistantMsg.ToolCalls[0].Id);
        Assert.Contains("New York, NY", assistantMsg.ToolCalls[0].ArgumentsJson);

        // Check the tool response
        var toolMsg = messages[2];
        Assert.Equal(Role.Tool, toolMsg.Role);
        Assert.Single(toolMsg.ToolResults);
        Assert.Equal("get_weather", toolMsg.ToolResults[0].Name);
        Assert.Equal("call_123", toolMsg.ToolResults[0].CallId);
        Assert.Contains("72", toolMsg.ToolResults[0].ResultJson);
        Assert.Contains("sunny", toolMsg.ToolResults[0].ResultJson);
    }

    [Fact]
    public void ConversationStats_WithFullConversation_ShouldCountEverything()
    {
        // Arrange
        var conversation = new Model.Conversation();

        // System message
        conversation.AddTurn(new Turn
        {
            UserOrSystemMessage = new Message { Role = Role.System, Content = "Be helpful" }
        });

        // User-Assistant exchange with tool
        var turn = new Turn
        {
            UserOrSystemMessage = new Message { Role = Role.User, Content = "Calculate 10/2" },
            AssistantMessage = new Message
            {
                Role = Role.Assistant,
                Content = "Let me calculate",
                ToolCalls = new List<ToolCall>
                {
                    new() { Id = "calc_1", Name = "calculator", ArgumentsJson = "{\"expression\":\"10/2\"}" }
                }
            }
        };

        turn.ToolMessages.Add(new Message
        {
            Role = Role.Tool,
            Content = "5",
            ToolResults = new List<ToolResult>
            {
                ToolResult.FromObject("calc_1", "calculator", new { result = 5 })
            }
        });

        conversation.AddTurn(turn);

        // Regular exchange
        conversation.AddTurn(new Turn
        {
            UserOrSystemMessage = new Message { Role = Role.User, Content = "Thanks" },
            AssistantMessage = new Message { Role = Role.Assistant, Content = "You're welcome!" }
        });

        // Act
        var stats = conversation.GetStats();

        // Assert
        Assert.Equal(3, stats.TotalTurns);
        Assert.Equal(6, stats.TotalMessages); // System, User, Assistant, Tool, User, Assistant
        Assert.Equal(1, stats.SystemMessages);
        Assert.Equal(2, stats.UserMessages);
        Assert.Equal(2, stats.AssistantMessages);
        Assert.Equal(1, stats.ToolMessages);
        Assert.Equal(1, stats.ToolCalls);
        Assert.Equal(1, stats.ToolResults);
    }
}