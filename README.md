# andy-model

Core model library for orchestrating assistant and LLM interactions with advanced conversation management.

> **Alpha status**
>
> Andy.Model is in early **alpha**. The public API may change between releases without
> notice, and there are no stability guarantees yet. It is a pure in-memory model and
> orchestration library: it performs no filesystem, network, or other destructive
> operations of its own — any side effects come solely from the `ILlmProvider` and
> `ITool` implementations you supply. Provided under the Apache License 2.0 with no
> warranty; see [LICENSE](LICENSE).

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

- .NET 10.0 or later
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
public sealed class CalculatorTool : ITool
{
    public ToolDeclaration Definition => new ToolDeclaration
    {
        Name = "calculator",
        Description = "Performs a basic arithmetic operation on two numbers",
        // Parameters use the JSON Schema subset validated by ToolCallValidator:
        // nested dictionaries with string keys such as "type", "properties", "enum".
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
public interface ILlmProvider
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

Contributions are welcome. Before opening a pull request, please ensure the same gates
CI enforces pass locally:

```bash
dotnet build -c Release        # builds without warnings-as-errors surprises
dotnet test                    # all tests pass
dotnet format --verify-no-changes   # code is formatted
```

Guidelines:
- New features and bug fixes include tests.
- Code follows existing patterns and passes `dotnet format`.
- Documentation is updated when behavior changes.

### Coverage

CI collects line coverage on the production library and enforces an initial threshold of
**75%** (see `.github/workflows/ci.yml`, `COVERAGE_THRESHOLD`). The threshold is a floor
that should be raised over time, not lowered. Generate a local HTML report with:

```bash
dotnet test --collect:"XPlat Code Coverage" --results-directory ./TestResults
reportgenerator -reports:"./TestResults/*/coverage.cobertura.xml" -targetdir:"./TestResults/CoverageReport" -reporttypes:Html
```

## License

This project is licensed under the Apache License 2.0 — see the [LICENSE](LICENSE) file for
details. Source files carry an `SPDX-License-Identifier: Apache-2.0` marker where a header
is present.