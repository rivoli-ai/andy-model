# andy-model

Core model library for orchestrating assistant and LLM interactions with advanced conversation management.

> **ALPHA RELEASE WARNING**
>
> This software is in ALPHA stage. **NO GUARANTEES** are made about its functionality, stability, or safety.
>
> **CRITICAL WARNINGS:**
> - This tool performs **DESTRUCTIVE OPERATIONS** on files and directories
> - Permission management is **NOT FULLY TESTED** and may have security vulnerabilities
> - **DO NOT USE** in production environments
> - **DO NOT USE** on systems with critical or irreplaceable data
> - **DO NOT USE** on systems without complete, verified backups
> - The authors assume **NO RESPONSIBILITY** for data loss, system damage, or security breaches
>
> **USE AT YOUR OWN RISK**

## Overview

Andy.Model provides a flexible and extensible framework for building AI assistants with:
- Conversation orchestration with turn-based interactions
- Tool/function calling support with validation
- Multiple LLM provider interfaces
- Advanced conversation management strategies
- Event-driven architecture for progress monitoring
- Streaming response support

## Features

### Core Components

- **Assistant Orchestrator**: Manages the flow between user input, LLM calls, and tool execution
- **Conversation Management**: Flexible strategies for context window management and message compression
- **Tool System**: Declarative tool definitions with automatic validation and error handling
- **Event System**: Comprehensive events for monitoring conversation progress and debugging
- **LLM Abstraction**: Provider-agnostic interface for integrating different LLM services

### Conversation Management Strategies

The library includes multiple conversation management strategies through the `IConversationManager` interface:

1. **DefaultConversationManager**: Configurable compression with token budget management
2. **SlidingWindowConversationManager**: Maintains a fixed window of recent messages
3. **SemanticConversationManager**: Preserves messages based on semantic importance
4. **Summary-based Compression**: Replaces old messages with generated summaries

### Key Capabilities

- **Turn-based Conversation Model**: Structured representation of multi-turn conversations
- **Tool Call Validation**: Automatic validation of tool calls against their schemas
- **Message Filtering**: Filter messages by role, age, and importance
- **Token Budget Management**: Automatic message compression to fit context windows
- **Tool Call/Result Preservation**: Smart preservation of tool interaction pairs
- **Automatic Compaction**: Configurable thresholds for automatic conversation compaction
- **Event-driven Architecture**: Subscribe to conversation lifecycle events
- **Streaming Support**: Real-time streaming of LLM responses

## Installation

### Prerequisites

- .NET 8.0 or later
- NuGet package manager

### Package Installation

```bash
dotnet add package Andy.Model
```

## Usage

### Basic Assistant Setup

```csharp
using Andy.Model.Orchestration;
using Andy.Model.Tooling;
using Andy.Model.Conversation;

// Create components
var conversationManager = new DefaultConversationManager();
var toolRegistry = new ToolRegistry();
var llmProvider = new YourLlmProvider(); // Implement ILlmProvider

// Create assistant
var assistant = new AssistantWithManager(conversationManager, toolRegistry, llmProvider);

// Run a turn
var response = await assistant.RunTurnAsync("Hello, how are you?");
Console.WriteLine(response.Content);
```

### Using Conversation Management Strategies

```csharp
// Sliding window strategy
var slidingWindowManager = new SlidingWindowConversationManager(
    windowSize: 10,
    preserveFirstMessage: true
);

// Semantic importance strategy
var semanticManager = new SemanticConversationManager();
semanticManager.AddImportantKeywords("budget", "deadline", "requirement");

// Configure with options
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
```

### Implementing Tools

```csharp
public class CalculatorTool : ITool
{
    public ToolDeclaration Definition => new ToolDeclaration
    {
        Name = "calculator",
        Description = "Performs basic arithmetic operations",
        Parameters = new Dictionary<string, object>
        {
            ["type"] = "object",
            ["properties"] = new Dictionary<string, object>
            {
                ["operation"] = new { type = "string", enum = new[] { "add", "subtract", "multiply", "divide" } },
                ["a"] = new { type = "number" },
                ["b"] = new { type = "number" }
            },
            ["required"] = new[] { "operation", "a", "b" }
        }
    };

    public async Task<ToolResult> ExecuteAsync(ToolCall call, CancellationToken ct = default)
    {
        // Parse arguments and perform calculation
        var args = JsonSerializer.Deserialize<CalculatorArgs>(call.ArgumentsJson);
        var result = args.Operation switch
        {
            "add" => args.A + args.B,
            "subtract" => args.A - args.B,
            "multiply" => args.A * args.B,
            "divide" => args.B != 0 ? args.A / args.B : throw new DivideByZeroException(),
            _ => throw new ArgumentException($"Unknown operation: {args.Operation}")
        };

        return ToolResult.FromObject(call.Id, call.Name, new { result });
    }
}

// Register the tool
toolRegistry.Register(new CalculatorTool());
```

### Event Monitoring

```csharp
assistant.TurnStarted += (s, e) =>
    Console.WriteLine($"Turn {e.TurnNumber} started");

assistant.ToolExecutionStarted += (s, e) =>
    Console.WriteLine($"Executing tool: {e.ToolName}");

assistant.ToolExecutionCompleted += (s, e) =>
    Console.WriteLine($"Tool {e.ToolCall.Name} completed in {e.Duration.TotalMilliseconds}ms");

assistant.StreamingTokenReceived += (s, e) =>
    Console.Write(e.Delta.Content);

assistant.ErrorOccurred += (s, e) =>
    Console.WriteLine($"Error in {e.Context}: {e.Exception.Message}");
```

### Streaming Responses

```csharp
await foreach (var message in assistant.RunTurnStreamAsync("Tell me a story"))
{
    // Messages are streamed as they arrive
    Console.Write(message.Content);
}
```

## Architecture

### Core Models

- **Conversation**: Container for turns with state management
- **Turn**: Represents a single interaction cycle (user message, assistant response, tool calls)
- **Message**: Individual message with role, content, and metadata
- **ToolCall/ToolResult**: Tool interaction representations

### Provider Interface

Implement `ILlmProvider` to integrate new LLM services:

```csharp
public interface ILlmProvider : IDisposable
{
    string Name { get; }
    Task<LlmResponse> CompleteAsync(LlmRequest request, CancellationToken cancellationToken = default);
    IAsyncEnumerable<LlmStreamResponse> StreamCompleteAsync(LlmRequest request, CancellationToken cancellationToken = default);
    Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default);
    Task<IEnumerable<ModelInfo>> ListModelsAsync(CancellationToken cancellationToken = default);
}
```

## Examples

The repository includes several example implementations:

1. **Basic Examples** (`examples/Andy.Model.Examples/`): Simple usage patterns
2. **LLM7.io Client** (`examples/Llm7IoClient/`): OpenAI-compatible API integration
3. **Conversation Manager Examples** (`examples/ConversationManagerExamples/`): Different management strategies

## Testing

The project includes comprehensive unit tests covering:
- Conversation management strategies
- Tool execution and validation
- Message filtering and compression
- Event system functionality
- Assistant orchestration

Run tests with:
```bash
dotnet test
```

Generate coverage report:
```bash
dotnet test --collect:"XPlat Code Coverage"
reportgenerator -reports:"./TestResults/*/coverage.cobertura.xml" -targetdir:"./TestResults/CoverageReport" -reporttypes:Html
```

## Contributing

Contributions are welcome! Please ensure:
- All tests pass
- New features include tests
- Code follows existing patterns
- Documentation is updated

## License

This project is licensed under the Apache License 2.0 - see the LICENSE file for details.