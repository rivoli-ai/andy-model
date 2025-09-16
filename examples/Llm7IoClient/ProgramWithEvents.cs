using System;
using System.Linq;
using System.Threading.Tasks;
using Andy.Model.Model;
using Andy.Model.Orchestration;
using Andy.Model.Tooling;
using Andy.Model.Utils;
using Llm7IoClient;
using Llm7IoClient.Tools;

class ProgramWithEvents
{
    // Rename to MainWithEvents to avoid multiple entry point conflict
    // To run this example, call ProgramWithEvents.MainWithEvents() from Program.cs
    // or compile with /main:ProgramWithEvents
    public static async Task MainWithEvents(string[] args)
    {
        Console.WriteLine("=== LLM7.io Client with Assistant Events ===\n");

        // Create the provider
        var provider = new Llm7IoProvider("gpt-4o-mini-2024-07-18");

        // Test provider availability
        Console.WriteLine("Checking provider availability...");
        if (await provider.IsAvailableAsync())
        {
            Console.WriteLine($"✓ Provider '{provider.Name}' is available\n");
        }
        else
        {
            Console.WriteLine($"✗ Provider '{provider.Name}' is not available\n");
            return;
        }

        // Example 1: Basic chat with events
        Console.WriteLine("1. Chat Example with Event Monitoring");
        Console.WriteLine("=====================================\n");
        await RunChatWithEvents(provider);

        // Example 2: Streaming chat with events
        Console.WriteLine("\n2. Streaming Chat Example with Events");
        Console.WriteLine("=====================================\n");
        await RunStreamingChatWithEvents(provider);
    }

    static async Task RunChatWithEvents(Llm7IoProvider provider)
    {
        // Create conversation with tools
        var conversation = new Conversation();
        var toolRegistry = new ToolRegistry();

        // Register tools
        toolRegistry.Register(new FibonacciTool());
        toolRegistry.Register(new CurrentTimeTool());

        var assistant = new Assistant(conversation, toolRegistry, provider);

        // Subscribe to events
        SubscribeToEvents(assistant);

        // First interaction - will use tool
        Console.WriteLine("User: What's the 15th fibonacci number?\n");
        var response1 = await assistant.RunTurnAsync("What's the 15th fibonacci number?");
        Console.WriteLine($"\nAssistant: {response1.Content}\n");

        // Second interaction - will use different tool
        Console.WriteLine("\nUser: What's the current time?\n");
        var response2 = await assistant.RunTurnAsync("What's the current time?");
        Console.WriteLine($"\nAssistant: {response2.Content}\n");

        // Third interaction - no tools
        Console.WriteLine("\nUser: Explain what fibonacci numbers are\n");
        var response3 = await assistant.RunTurnAsync("Explain what fibonacci numbers are");
        Console.WriteLine($"\nAssistant: {response3.Content}\n");
    }

    static async Task RunStreamingChatWithEvents(Llm7IoProvider provider)
    {
        var conversation = new Conversation();
        var toolRegistry = new ToolRegistry();
        var assistant = new Assistant(conversation, toolRegistry, provider);

        // Subscribe to streaming events
        assistant.TurnStarted += (sender, e) =>
        {
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine($"[STREAM] Turn {e.TurnNumber} started");
            Console.ResetColor();
        };

        assistant.LlmRequestStarted += (sender, e) =>
        {
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine($"[STREAM] Sending {e.MessageCount} messages to LLM...");
            Console.ResetColor();
        };

        assistant.StreamingTokenReceived += (sender, e) =>
        {
            if (e.Delta?.Content?.Length > 0)
            {
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.Write(e.Delta.Content);
                Console.ResetColor();
            }
        };

        assistant.TurnCompleted += (sender, e) =>
        {
            Console.WriteLine();
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine($"[STREAM] Turn completed in {e.Duration.TotalMilliseconds:F0}ms");
            Console.ResetColor();
        };

        // Stream a response
        Console.WriteLine("User: Write a haiku about programming\n");
        Console.ForegroundColor = ConsoleColor.Green;
        Console.Write("Assistant: ");
        Console.ResetColor();

        await foreach (var message in assistant.RunTurnStreamAsync("Write a haiku about programming"))
        {
            // The streaming tokens are handled by the event handler above
            // This loop just ensures we consume the stream
        }
        Console.WriteLine("\n");
    }

    static void SubscribeToEvents(Assistant assistant)
    {
        // Turn lifecycle events
        assistant.TurnStarted += (sender, e) =>
        {
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine($"[EVENT] Turn {e.TurnNumber} started at {e.Timestamp:HH:mm:ss.fff}");
            Console.ResetColor();
        };

        assistant.TurnCompleted += (sender, e) =>
        {
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine($"[EVENT] Turn completed in {e.Duration.TotalMilliseconds:F0}ms with {e.ToolCallsExecuted} tool calls");
            Console.ResetColor();
        };

        // LLM interaction events
        assistant.LlmRequestStarted += (sender, e) =>
        {
            Console.ForegroundColor = ConsoleColor.Blue;
            var retryInfo = e.IsRetryAfterTools ? " (retry after tools)" : "";
            Console.WriteLine($"[LLM] Sending request with {e.MessageCount} messages and {e.ToolCount} tools{retryInfo}");
            Console.ResetColor();
        };

        assistant.LlmResponseReceived += (sender, e) =>
        {
            Console.ForegroundColor = ConsoleColor.Blue;
            var toolInfo = e.HasToolCalls ? " (has tool calls)" : "";
            var tokenInfo = e.Usage != null ? $" [{e.Usage.TotalTokens} tokens]" : "";
            Console.WriteLine($"[LLM] Response received{toolInfo}{tokenInfo}");
            Console.ResetColor();
        };

        // Tool execution events
        assistant.ToolExecutionStarted += (sender, e) =>
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"[TOOL] Executing '{e.ToolName}' (call_id: {e.ToolCall.Id.Substring(0, 8)}...)");
            Console.ResetColor();
        };

        assistant.ToolExecutionCompleted += (sender, e) =>
        {
            Console.ForegroundColor = e.IsError ? ConsoleColor.Red : ConsoleColor.Green;
            var status = e.IsError ? "ERROR" : "SUCCESS";
            Console.WriteLine($"[TOOL] {status}: '{e.ToolCall.Name}' completed in {e.Duration.TotalMilliseconds:F0}ms");
            if (!e.IsError)
            {
                Console.ForegroundColor = ConsoleColor.DarkGreen;
                Console.WriteLine($"[TOOL] Result: {e.Result.ResultJson}");
            }
            Console.ResetColor();
        };

        // Validation events
        assistant.ToolValidationFailed += (sender, e) =>
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"[VALIDATION] Tool '{e.ToolCall.Name}' validation failed:");
            foreach (var error in e.ValidationErrors)
            {
                Console.WriteLine($"  - {error}");
            }
            Console.ResetColor();
        };

        assistant.ToolNotFound += (sender, e) =>
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"[ERROR] Tool '{e.ToolName}' not found. Available tools: {string.Join(", ", e.AvailableTools)}");
            Console.ResetColor();
        };

        // Error handling
        assistant.ErrorOccurred += (sender, e) =>
        {
            Console.ForegroundColor = ConsoleColor.Red;
            var criticalInfo = e.IsCritical ? " [CRITICAL]" : "";
            Console.WriteLine($"[ERROR]{criticalInfo} in {e.Context}: {e.Exception.Message}");
            Console.ResetColor();
        };
    }
}