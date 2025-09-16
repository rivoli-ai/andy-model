using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Andy.Model.Conversation;
using Andy.Model.Llm;
using Andy.Model.Model;
using Andy.Model.Orchestration;
using Andy.Model.Tooling;
using Xunit;

namespace Andy.Model.Tests.Orchestration;

public class AssistantWithManagerTests
{
    private class MockLlmProvider : ILlmProvider
    {
        public string Name => "MockLLM";
        public int CallCount { get; private set; }
        public LlmRequest? LastRequest { get; private set; }

        public Task<LlmResponse> CompleteAsync(LlmRequest request, System.Threading.CancellationToken cancellationToken = default)
        {
            CallCount++;
            LastRequest = request;

            var response = new LlmResponse
            {
                AssistantMessage = new Message
                {
                    Role = Role.Assistant,
                    Content = $"Response to: {request.Messages.LastOrDefault()?.Content ?? "empty"}",
                    ToolCalls = new List<ToolCall>()
                },
                Usage = new LlmUsage
                {
                    PromptTokens = 10,
                    CompletionTokens = 20,
                    TotalTokens = 30
                }
            };

            return Task.FromResult(response);
        }

        public async IAsyncEnumerable<LlmStreamResponse> StreamCompleteAsync(
            LlmRequest request,
            [System.Runtime.CompilerServices.EnumeratorCancellation] System.Threading.CancellationToken cancellationToken = default)
        {
            CallCount++;
            LastRequest = request;

            yield return new LlmStreamResponse
            {
                Delta = new Message
                {
                    Role = Role.Assistant,
                    Content = "Streaming response",
                    ToolCalls = new List<ToolCall>()
                },
                IsComplete = true
            };

            await Task.CompletedTask;
        }

        public Task<bool> IsAvailableAsync(System.Threading.CancellationToken cancellationToken = default)
        {
            return Task.FromResult(true);
        }

        public Task<IEnumerable<ModelInfo>> ListModelsAsync(System.Threading.CancellationToken cancellationToken = default)
        {
            var models = new List<ModelInfo>
            {
                new ModelInfo { Id = "mock-model", Name = "Mock Model" }
            };
            return Task.FromResult<IEnumerable<ModelInfo>>(models);
        }

        public void Dispose() { }
    }

    [Fact]
    public void Constructor_WithConversationManager_UsesProvided()
    {
        // Arrange
        var conversationManager = new DefaultConversationManager();
        var toolRegistry = new ToolRegistry();
        var llm = new MockLlmProvider();

        // Act
        var assistant = new AssistantWithManager(conversationManager, toolRegistry, llm);

        // Assert
        Assert.Same(conversationManager, assistant.ConversationManager);
        Assert.NotNull(assistant.Conversation);
    }

    [Fact]
    public void Constructor_WithoutConversationManager_CreatesDefault()
    {
        // Arrange
        var toolRegistry = new ToolRegistry();
        var llm = new MockLlmProvider();
        var options = new ConversationManagerOptions
        {
            MaxRecentMessages = 5
        };

        // Act
        var assistant = new AssistantWithManager(toolRegistry, llm, options);

        // Assert
        Assert.NotNull(assistant.ConversationManager);
        Assert.IsType<DefaultConversationManager>(assistant.ConversationManager);
        Assert.NotNull(assistant.Conversation);
    }

    [Fact]
    public async Task RunTurnAsync_UsesConversationManager()
    {
        // Arrange
        var conversationManager = new SlidingWindowConversationManager(windowSize: 4);
        var toolRegistry = new ToolRegistry();
        var llm = new MockLlmProvider();
        var assistant = new AssistantWithManager(conversationManager, toolRegistry, llm);

        // Act
        var response = await assistant.RunTurnAsync("Hello");

        // Assert
        Assert.NotNull(response);
        Assert.Equal(Role.Assistant, response.Role);
        Assert.Contains("Hello", response.Content);
        Assert.Single(assistant.Conversation.Turns);
        Assert.Equal(1, llm.CallCount);
    }

    [Fact]
    public async Task RunTurnAsync_FiresEvents()
    {
        // Arrange
        var assistant = new AssistantWithManager(
            new ToolRegistry(),
            new MockLlmProvider()
        );

        var turnStartedFired = false;
        var turnCompletedFired = false;
        var llmRequestStartedFired = false;
        var llmResponseReceivedFired = false;

        assistant.TurnStarted += (s, e) => turnStartedFired = true;
        assistant.TurnCompleted += (s, e) => turnCompletedFired = true;
        assistant.LlmRequestStarted += (s, e) => llmRequestStartedFired = true;
        assistant.LlmResponseReceived += (s, e) => llmResponseReceivedFired = true;

        // Act
        await assistant.RunTurnAsync("Test message");

        // Assert
        Assert.True(turnStartedFired);
        Assert.True(turnCompletedFired);
        Assert.True(llmRequestStartedFired);
        Assert.True(llmResponseReceivedFired);
    }

    [Fact]
    public async Task RunTurnAsync_UsesExtractedMessages()
    {
        // Arrange
        var conversationManager = new SlidingWindowConversationManager(windowSize: 2);
        var toolRegistry = new ToolRegistry();
        var llm = new MockLlmProvider();
        var assistant = new AssistantWithManager(conversationManager, toolRegistry, llm);

        // Add messages to exceed window
        for (int i = 1; i <= 5; i++)
        {
            await assistant.RunTurnAsync($"Message {i}");
        }

        // Act
        await assistant.RunTurnAsync("Final message");

        // Assert
        Assert.NotNull(llm.LastRequest);
        // Should only have window size messages in request
        Assert.True(llm.LastRequest.Messages.Length <= 3); // Window + new message
    }

    [Fact]
    public async Task GetConversationSummaryAsync_CallsManager()
    {
        // Arrange
        var assistant = new AssistantWithManager(
            new ToolRegistry(),
            new MockLlmProvider()
        );

        await assistant.RunTurnAsync("Test message 1");
        await assistant.RunTurnAsync("Test message 2");

        // Act
        var summary = await assistant.GetConversationSummaryAsync();

        // Assert
        Assert.NotNull(summary);
        Assert.Contains("2 turns", summary);
    }

    [Fact]
    public async Task CompactConversationAsync_CallsManager()
    {
        // Arrange
        var options = new ConversationManagerOptions
        {
            CompactionThreshold = 2,
            CompressionStrategy = CompressionStrategy.Summary
        };
        var conversationManager = new DefaultConversationManager(options);
        var assistant = new AssistantWithManager(
            conversationManager,
            new ToolRegistry(),
            new MockLlmProvider()
        );

        // Add turns to exceed threshold
        await assistant.RunTurnAsync("Message 1");
        await assistant.RunTurnAsync("Message 2");
        await assistant.RunTurnAsync("Message 3");

        // Act
        var compacted = await assistant.CompactConversationAsync();

        // Assert
        Assert.True(compacted);
        Assert.NotNull(conversationManager.Conversation.GetState<string>("conversation_summary"));
    }

    [Fact]
    public void GetConversationStats_ReturnsStatistics()
    {
        // Arrange
        var assistant = new AssistantWithManager(
            new ToolRegistry(),
            new MockLlmProvider()
        );

        // Act
        var stats = assistant.GetConversationStats();

        // Assert
        Assert.NotNull(stats);
        Assert.Equal(0, stats.TotalTurns);
    }

    [Fact]
    public async Task RunTurnStreamAsync_UsesConversationManager()
    {
        // Arrange
        var conversationManager = new SlidingWindowConversationManager(windowSize: 4);
        var toolRegistry = new ToolRegistry();
        var llm = new MockLlmProvider();
        var assistant = new AssistantWithManager(conversationManager, toolRegistry, llm);

        // Act
        var messages = new List<Message>();
        await foreach (var message in assistant.RunTurnStreamAsync("Hello"))
        {
            messages.Add(message);
        }

        // Assert
        Assert.NotEmpty(messages);
        Assert.Single(assistant.Conversation.Turns);
        Assert.Equal(1, llm.CallCount);
    }

    [Fact]
    public async Task AutoCompact_TriggersWhenThresholdExceeded()
    {
        // Arrange
        var options = new ConversationManagerOptions
        {
            CompactionThreshold = 2,
            AutoCompact = true,
            CompressionStrategy = CompressionStrategy.Summary
        };
        var conversationManager = new DefaultConversationManager(options);
        var assistant = new AssistantWithManager(
            conversationManager,
            new ToolRegistry(),
            new MockLlmProvider()
        );

        // Act - Add turns to exceed threshold
        await assistant.RunTurnAsync("Message 1");
        await assistant.RunTurnAsync("Message 2");
        await assistant.RunTurnAsync("Message 3"); // Should trigger compaction

        // Wait for async compaction
        await Task.Delay(100);

        // Assert
        Assert.True(conversationManager.ShouldCompact());
    }

    [Fact]
    public async Task ConversationManager_IntegrationWithSemanticStrategy()
    {
        // Arrange
        var options = new ConversationManagerOptions
        {
            MaxRecentMessages = 3,
            CompressionStrategy = CompressionStrategy.Semantic
        };
        var conversationManager = new SemanticConversationManager(options);
        conversationManager.AddImportantKeywords("budget", "deadline");

        var assistant = new AssistantWithManager(
            conversationManager,
            new ToolRegistry(),
            new MockLlmProvider()
        );

        // Add messages with varying importance
        await assistant.RunTurnAsync("Random chat");
        await assistant.RunTurnAsync("The budget is $100k"); // Important
        await assistant.RunTurnAsync("Nice weather");
        await assistant.RunTurnAsync("Deadline is Friday"); // Important

        // Act
        var messages = conversationManager.ExtractMessagesForNextTurn().ToList();

        // Assert
        // Should preserve important messages
        Assert.Contains(messages, m => m.Content.Contains("budget"));
        Assert.Contains(messages, m => m.Content.Contains("Deadline"));
    }
}