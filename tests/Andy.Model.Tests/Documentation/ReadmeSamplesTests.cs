using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Andy.Model.Conversation;
using Andy.Model.Model;
using Andy.Model.Orchestration;
using Andy.Model.Tests.Orchestration;
using Andy.Model.Tooling;
using Xunit;

namespace Andy.Model.Tests.Documentation;

/// <summary>
/// Compiles and exercises the code samples shown in README.md so the documentation cannot
/// silently drift out of sync with the API (#14). The tool below is a verbatim copy of the
/// README "Implementing Tools" example.
/// </summary>
public class ReadmeSamplesTests
{
    // ---- README: Implementing Tools ----
    public sealed class CalculatorTool : ITool
    {
        public ToolDeclaration Definition => new ToolDeclaration
        {
            Name = "calculator",
            Description = "Performs a basic arithmetic operation on two numbers",
            Parameters = new Dictionary<string, object>
            {
                ["type"] = "object",
                ["properties"] = new Dictionary<string, object>
                {
                    ["operation"] = new Dictionary<string, object>
                    {
                        ["type"] = "string",
                        ["enum"] = new[] { "add", "subtract", "multiply", "divide" }
                    },
                    ["a"] = new Dictionary<string, object> { ["type"] = "number" },
                    ["b"] = new Dictionary<string, object> { ["type"] = "number" }
                },
                ["required"] = new[] { "operation", "a", "b" }
            }
        };

        public Task<ToolResult> ExecuteAsync(ToolCall call, CancellationToken ct = default)
        {
            var args = call.ArgumentsAsJsonElement();
            var operation = args.GetProperty("operation").GetString();
            var a = args.GetProperty("a").GetDouble();
            var b = args.GetProperty("b").GetDouble();

            double result = operation switch
            {
                "add" => a + b,
                "subtract" => a - b,
                "multiply" => a * b,
                "divide" => b != 0 ? a / b : throw new DivideByZeroException(),
                _ => throw new ArgumentException($"Unknown operation: {operation}")
            };

            return Task.FromResult(ToolResult.FromObject(call.Id, call.Name, new { result }));
        }
    }

    [Fact]
    public void CalculatorTool_SchemaValidatesArguments()
    {
        var tool = new CalculatorTool();

        var valid = new ToolCall { Name = "calculator", ArgumentsJson = "{\"operation\":\"add\",\"a\":1,\"b\":2}" };
        Assert.True(ToolCallValidator.Validate(valid, tool.Definition).IsValid);

        var badEnum = new ToolCall { Name = "calculator", ArgumentsJson = "{\"operation\":\"power\",\"a\":1,\"b\":2}" };
        Assert.False(ToolCallValidator.Validate(badEnum, tool.Definition).IsValid);
    }

    [Fact]
    public async Task BasicSetup_EventsAndStreaming_Compile()
    {
        // README: Basic Assistant Setup / Event Monitoring / Streaming Responses
        var conversationManager = new DefaultConversationManager();
        var toolRegistry = new ToolRegistry();
        toolRegistry.Register(new CalculatorTool());

        var llmProvider = new ScriptedLlmProvider()
            .EnqueueToolCall("calculator", "c1", new { operation = "add", a = 2, b = 3 })
            .EnqueueText("The answer is 5.")
            .EnqueueStream(Responses.TextDelta("Once upon a time", isComplete: true));

        var assistant = new AssistantWithManager(conversationManager, toolRegistry, llmProvider);

        var toolNames = new List<string>();
        assistant.ToolExecutionStarted += (s, e) => toolNames.Add(e.ToolName);

        var response = await assistant.RunTurnAsync("What is 2 + 3?");
        Assert.Equal("The answer is 5.", response.Content);
        Assert.Contains("calculator", toolNames);

        var streamed = new List<string>();
        await foreach (var message in assistant.RunTurnStreamAsync("Tell me a story"))
        {
            streamed.Add(message.Content);
        }
        Assert.Contains("Once upon a time", string.Concat(streamed));
    }

    [Fact]
    public void ConversationManagerStrategies_Compile()
    {
        // README: Using Conversation Management Strategies
        var slidingWindowManager = new SlidingWindowConversationManager(
            windowSize: 10,
            preserveFirstMessage: true);

        var semanticManager = new SemanticConversationManager();
        semanticManager.AddImportantKeywords("budget", "deadline", "requirement");

        var options = new ConversationManagerOptions
        {
            MaxTokens = 4000,
            MaxRecentMessages = 20,
            CompressionStrategy = CompressionStrategy.Smart,
            PreserveToolCallPairs = true,
            AutoCompact = true,
            CompactionThreshold = 50
        };
        var manager = new DefaultConversationManager(options);

        Assert.NotNull(slidingWindowManager);
        Assert.NotNull(semanticManager);
        Assert.NotNull(manager);
    }
}
