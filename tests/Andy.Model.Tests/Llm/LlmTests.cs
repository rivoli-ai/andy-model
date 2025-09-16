using Andy.Model.Examples;
using Andy.Model.Llm;
using Andy.Model.Model;
using Andy.Model.Tooling;
using Xunit;

namespace Andy.Model.Tests.Llm;

public class LlmTests
{
    [Fact]
    public void LlmClientConfig_ShouldInitializeWithDefaults()
    {
        // Act
        var config = new LlmClientConfig();

        // Assert
        Assert.Equal(string.Empty, config.Model);
        Assert.Equal(0.7m, config.Temperature);
        Assert.Equal(4000, config.MaxTokens);
        Assert.Equal(1.0m, config.TopP);
    }

    [Fact]
    public void LlmClientConfig_ShouldAcceptCustomValues()
    {
        // Arrange & Act
        var config = new LlmClientConfig
        {
            Model = "gpt-4",
            Temperature = 0.5m,
            MaxTokens = 2000,
            TopP = 0.9m
        };

        // Assert
        Assert.Equal("gpt-4", config.Model);
        Assert.Equal(0.5m, config.Temperature);
        Assert.Equal(2000, config.MaxTokens);
        Assert.Equal(0.9m, config.TopP);
    }

    [Fact]
    public void LlmUsage_ShouldStoreTokenCounts()
    {
        // Arrange & Act
        var usage = new LlmUsage
        {
            PromptTokens = 100,
            CompletionTokens = 50,
            TotalTokens = 150
        };

        // Assert
        Assert.Equal(100, usage.PromptTokens);
        Assert.Equal(50, usage.CompletionTokens);
        Assert.Equal(150, usage.TotalTokens);
    }

    [Fact]
    public void LlmStreamResponse_ShouldInitializeWithNullDelta()
    {
        // Act
        var response = new LlmStreamResponse();

        // Assert
        Assert.Null(response.Delta);
        Assert.False(response.IsComplete);
        Assert.Null(response.Usage);
        Assert.Null(response.Error);
    }

    [Fact]
    public void LlmStreamResponse_WithDelta_ShouldStoreDeltaMessage()
    {
        // Arrange
        var deltaMessage = new Message
        {
            Role = Role.Assistant,
            Content = "Streaming response chunk"
        };

        // Act
        var response = new LlmStreamResponse
        {
            Delta = deltaMessage,
            IsComplete = false
        };

        // Assert
        Assert.NotNull(response.Delta);
        Assert.Equal("Streaming response chunk", response.Delta.Content);
        Assert.False(response.IsComplete);
    }

    [Fact]
    public void LlmStreamResponse_FinalChunk_ShouldIncludeUsage()
    {
        // Arrange
        var usage = new LlmUsage
        {
            PromptTokens = 10,
            CompletionTokens = 20,
            TotalTokens = 30
        };

        // Act
        var response = new LlmStreamResponse
        {
            Delta = new Message { Role = Role.Assistant, Content = "Final chunk" },
            IsComplete = true,
            Usage = usage
        };

        // Assert
        Assert.True(response.IsComplete);
        Assert.NotNull(response.Usage);
        Assert.Equal(30, response.Usage.TotalTokens);
    }

    [Fact]
    public void LlmStreamResponse_WithError_ShouldStoreErrorMessage()
    {
        // Act
        var response = new LlmStreamResponse
        {
            Error = "Stream interrupted",
            IsComplete = true
        };

        // Assert
        Assert.Equal("Stream interrupted", response.Error);
        Assert.True(response.IsComplete);
    }

    [Fact]
    public void LlmRequest_WithTools_ShouldConfigureCorrectly()
    {
        // Arrange
        var messages = new List<Message>
        {
            new Message { Role = Role.User, Content = "Test" }
        };

        var tools = new[]
        {
            new ToolDeclaration { Name = "tool1", Description = "Test tool" }
        };

        var config = new LlmClientConfig
        {
            Model = "test-model",
            MaxTokens = 1000,
            Temperature = 0.8m
        };

        // Act
        var request = new LlmRequest
        {
            Messages = messages,
            Tools = tools,
            Config = config
        };

        // Assert
        Assert.Equal(messages, request.Messages);
        Assert.Equal(tools, request.Tools);
        Assert.NotNull(request.Config);
        Assert.Equal(1000, request.Config.MaxTokens);
        Assert.Equal(0.8m, request.Config.Temperature);
    }

    [Fact]
    public void LlmRequest_WithoutTools_ShouldHaveEmptyToolsList()
    {
        // Arrange
        var messages = new List<Message>
        {
            new Message { Role = Role.System, Content = "You are a helpful assistant" },
            new Message { Role = Role.User, Content = "Hello" }
        };

        // Act
        var request = new LlmRequest
        {
            Messages = messages
        };

        // Assert
        Assert.Equal(messages, request.Messages);
        Assert.Empty(request.Tools);
        Assert.Null(request.Config);
    }

    [Fact]
    public void LlmResponse_WithToolCalls_ShouldIndicateHasToolCalls()
    {
        // Arrange
        var message = new Message
        {
            Role = Role.Assistant,
            Content = "Let me calculate that",
            ToolCalls =
            {
                new ToolCall { Id = "1", Name = "calculator", ArgumentsJson = "{}" }
            }
        };

        // Act
        var response = new LlmResponse
        {
            AssistantMessage = message,
            Usage = new LlmUsage { TotalTokens = 10 }
        };

        // Assert
        Assert.True(response.HasToolCalls);
        Assert.Equal(message, response.AssistantMessage);
        Assert.Equal(10, response.Usage?.TotalTokens);
    }

    [Fact]
    public void LlmResponse_WithoutToolCalls_ShouldIndicateNoToolCalls()
    {
        // Arrange
        var message = new Message
        {
            Role = Role.Assistant,
            Content = "The answer is 42"
        };

        // Act
        var response = new LlmResponse
        {
            AssistantMessage = message
        };

        // Assert
        Assert.False(response.HasToolCalls);
    }

    [Fact]
    public void ModelInfo_ShouldInitializeWithDefaults()
    {
        // Act
        var model = new ModelInfo
        {
            Id = "model-1"
        };

        // Assert
        Assert.Equal("model-1", model.Id);
        Assert.Null(model.Name);
        Assert.Null(model.Description);
        Assert.Null(model.MaxTokens);
        Assert.False(model.SupportsFunctions);
        Assert.True(model.SupportsStreaming); // Default is true
        Assert.Null(model.UpdatedAt);
        Assert.NotNull(model.Metadata);
        Assert.Empty(model.Metadata);
    }

    [Fact]
    public void ModelInfo_ShouldAcceptAllProperties()
    {
        // Arrange
        var updateTime = DateTimeOffset.UtcNow;
        var metadata = new Dictionary<string, object>
        {
            ["vendor"] = "OpenAI",
            ["version"] = "1.0"
        };

        // Act
        var model = new ModelInfo
        {
            Id = "gpt-4",
            Name = "GPT-4",
            Description = "Advanced language model",
            MaxTokens = 8192,
            SupportsFunctions = true,
            SupportsStreaming = true,
            UpdatedAt = updateTime,
            Metadata = metadata
        };

        // Assert
        Assert.Equal("gpt-4", model.Id);
        Assert.Equal("GPT-4", model.Name);
        Assert.Equal("Advanced language model", model.Description);
        Assert.Equal(8192, model.MaxTokens);
        Assert.True(model.SupportsFunctions);
        Assert.True(model.SupportsStreaming);
        Assert.Equal(updateTime, model.UpdatedAt);
        Assert.Equal(2, model.Metadata.Count);
        Assert.Equal("OpenAI", model.Metadata["vendor"]);
    }

    [Fact]
    public async Task DemoProvider_IsAvailable_ShouldReturnTrue()
    {
        // Arrange
        var provider = new DemoLlmClient();

        // Act
        var isAvailable = await provider.IsAvailableAsync();

        // Assert
        Assert.True(isAvailable);
    }

    [Fact]
    public async Task DemoProvider_ListModels_ShouldReturnDemoModel()
    {
        // Arrange
        var provider = new DemoLlmClient();

        // Act
        var models = await provider.ListModelsAsync();

        // Assert
        Assert.NotNull(models);
        var modelList = models.ToList();
        Assert.Single(modelList);
        Assert.Equal("demo-model-1", modelList[0].Id);
        Assert.Equal("Demo Model", modelList[0].Name);
    }

    [Fact]
    public void DemoProvider_Name_ShouldReturnProviderName()
    {
        // Arrange
        var provider = new DemoLlmClient();

        // Act
        var name = provider.Name;

        // Assert
        Assert.Equal("Demo Provider", name);
    }
}