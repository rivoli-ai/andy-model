using System;
using System.Linq;
using System.Threading.Tasks;
using Andy.Model.Model;
using Andy.Model.Orchestration;
using Andy.Model.Tooling;
using Andy.Model.Utils;
using Llm7IoClient;
using Llm7IoClient.Tools;

class Program
{
    static async Task Main(string[] args)
    {
        // Check if user wants to run the events example
        if (args.Length > 0 && args[0] == "--events")
        {
            await ProgramWithEvents.MainWithEvents(args);
            return;
        }

        Console.WriteLine("=== LLM7.io Client with Assistant ===");
        Console.WriteLine("Tip: Run with --events flag to see detailed event monitoring\n");

        // Create the provider
        var provider = new Llm7IoProvider("gpt-4o-mini-2024-07-18");

        // Test model listing
        Console.WriteLine("1. Fetching available models...");
        try
        {
            var models = await provider.ListModelsAsync();
            Console.WriteLine($"Provider: {provider.Name}");
            Console.WriteLine($"Available: {await provider.IsAvailableAsync()}");
            var count = 0;
            foreach (var model in models)
            {
                count++;
                if (count <= 5) // Show first 5 models
                    Console.WriteLine($"  - {model.Id}");
            }
            if (count > 5)
                Console.WriteLine($"  ... and {count - 5} more models");
            Console.WriteLine();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error fetching models: {ex.Message}\n");
        }

        // Example 1: Basic chat without tools
        Console.WriteLine("2. Chat Example (Without Tools)");
        Console.WriteLine("=====================================\n");
        await RunBasicChatExample(provider);

        // Add delay to avoid rate limiting
        Console.WriteLine("\n[Waiting 2 seconds to avoid rate limiting...]\n");
        await Task.Delay(2000);

        // Example 2: Chat with tools
        Console.WriteLine("3. Chat Example (With Tools)");
        Console.WriteLine("=====================================\n");
        await RunToolsChatExample(provider);
    }

    static async Task RunBasicChatExample(Llm7IoProvider provider)
    {
        // Create conversation and assistant without tools
        var conversation = new Conversation();
        var toolRegistry = new ToolRegistry();
        var assistant = new Assistant(conversation, toolRegistry, provider);

        // First user message
        Console.WriteLine("User: Write a simple fibonacci function in C#");
        var response1 = await assistant.RunTurnAsync("Write a simple fibonacci function in C#");
        Console.WriteLine($"Assistant: {response1.Content}");

        // Small delay to avoid rate limiting
        await Task.Delay(1500);

        // Follow-up
        Console.WriteLine("\nUser: Can you make it iterative instead of recursive?");
        var response2 = await assistant.RunTurnAsync("Can you make it iterative instead of recursive?");
        Console.WriteLine($"Assistant: {response2.Content}");
    }

    static async Task RunToolsChatExample(Llm7IoProvider provider)
    {
        try
        {
            // Create conversation with tools
            var conversation = new Conversation();
            var toolRegistry = new ToolRegistry();

            // Register tools
            toolRegistry.Register(new FibonacciTool());
            toolRegistry.Register(new CurrentTimeTool());

            var assistant = new Assistant(conversation, toolRegistry, provider);

            // Subscribe to basic progress events
            assistant.ToolExecutionStarted += (sender, e) =>
                Console.WriteLine($"  [Executing tool: {e.ToolName}]");

            assistant.ToolExecutionCompleted += (sender, e) =>
                Console.WriteLine($"  [Tool completed: {e.ToolCall.Name}]");

            // Ask about fibonacci
            Console.WriteLine("User: What's the 10th fibonacci number?");
            var response1 = await assistant.RunTurnAsync("What's the 10th fibonacci number?");

        // Check if tools were called
        var turn1 = conversation.Turns.Last();
        if (turn1.ToolMessages.Any())
        {
            Console.WriteLine("\nAssistant called tools:");
            foreach (var toolMsg in turn1.ToolMessages)
            {
                var toolResult = toolMsg.ToolResults?.FirstOrDefault();
                if (toolResult != null)
                {
                    Console.WriteLine($"  Tool: {toolResult.Name}");
                    Console.WriteLine($"  Result: {toolResult.ResultJson}");
                }
            }
        }

        Console.WriteLine($"\nAssistant: {response1.Content}");

        // Small delay to avoid rate limiting
        await Task.Delay(1500);

        // Ask about time
        Console.WriteLine("\nUser: What time is it?");
        var response2 = await assistant.RunTurnAsync("What time is it?");

        // Check if tools were called
        var turn2 = conversation.Turns.Last();
        if (turn2.ToolMessages.Any())
        {
            Console.WriteLine("\nAssistant called tools:");
            foreach (var toolMsg in turn2.ToolMessages)
            {
                var toolResult = toolMsg.ToolResults?.FirstOrDefault();
                if (toolResult != null)
                {
                    Console.WriteLine($"  Tool: {toolResult.Name}");
                    Console.WriteLine($"  Result: {toolResult.ResultJson}");
                }
            }
        }

        Console.WriteLine($"\nAssistant: {response2.Content}");

        // Show conversation stats
        Console.WriteLine("\n=== Conversation Statistics ===");
        var stats = conversation.GetStats();
        Console.WriteLine($"Total Turns: {stats.TotalTurns}");
        Console.WriteLine($"User Messages: {stats.UserMessages}");
        Console.WriteLine($"Assistant Messages: {stats.AssistantMessages}");
            Console.WriteLine($"Tool Calls: {stats.ToolCalls}");
            Console.WriteLine($"Tool Messages: {stats.ToolMessages}");
        }
        catch (HttpRequestException ex)
        {
            Console.WriteLine($"\n[Error] HTTP request failed: {ex.Message}");
            Console.WriteLine("This might be due to rate limiting. Please wait a moment and try again.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"\n[Error] An unexpected error occurred: {ex.Message}");
        }
    }
}