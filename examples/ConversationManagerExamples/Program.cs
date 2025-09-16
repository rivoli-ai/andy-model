using System;
using System.Threading.Tasks;
using Andy.Model.Conversation;
using Andy.Model.Model;
using Andy.Model.Orchestration;
using Andy.Model.Tooling;
using Llm7IoClient;
using Llm7IoClient.Tools;

class Program
{
    static async Task Main(string[] args)
    {
        Console.WriteLine("=== Conversation Manager Examples ===\n");

        if (args.Length > 0)
        {
            switch (args[0])
            {
                case "--sliding-window":
                    await RunSlidingWindowExample();
                    break;
                case "--semantic":
                    await RunSemanticExample();
                    break;
                case "--summary":
                    await RunSummaryBasedExample();
                    break;
                default:
                    await RunDefaultExample();
                    break;
            }
        }
        else
        {
            await RunDefaultExample();
            Console.WriteLine("\nTip: Run with --sliding-window, --semantic, or --summary to see different strategies");
        }
    }

    static async Task RunDefaultExample()
    {
        Console.WriteLine("1. Default Conversation Manager Example");
        Console.WriteLine("========================================\n");

        // Create provider and tools
        var provider = new Llm7IoProvider("gpt-4o-mini-2024-07-18");
        var toolRegistry = new ToolRegistry();
        toolRegistry.Register(new FibonacciTool());
        toolRegistry.Register(new CurrentTimeTool());

        // Create default conversation manager with options
        var options = new ConversationManagerOptions
        {
            MaxRecentMessages = 10,
            MaxTokens = 2000,
            CompressionStrategy = CompressionStrategy.Smart,
            PreserveToolCallPairs = true,
            CompactionThreshold = 5, // Compact after 5 turns
            AutoCompact = true
        };

        var conversationManager = new DefaultConversationManager(options);
        var assistant = new AssistantWithManager(conversationManager, toolRegistry, provider);

        // Subscribe to events
        assistant.TurnStarted += (s, e) => Console.WriteLine($"[Turn {e.TurnNumber}] Started");
        assistant.TurnCompleted += (s, e) => Console.WriteLine($"[Turn Complete] Duration: {e.Duration.TotalMilliseconds:F0}ms");

        // Simulate a conversation
        Console.WriteLine("User: What's the fibonacci of 10?");
        var response1 = await assistant.RunTurnAsync("What's the fibonacci of 10?");
        Console.WriteLine($"Assistant: {response1.Content}\n");

        await Task.Delay(1500); // Rate limiting

        Console.WriteLine("User: What time is it?");
        var response2 = await assistant.RunTurnAsync("What time is it?");
        Console.WriteLine($"Assistant: {response2.Content}\n");

        // Show statistics
        var stats = assistant.GetConversationStats();
        Console.WriteLine("\n=== Conversation Statistics ===");
        Console.WriteLine($"Total Turns: {stats.TotalTurns}");
        Console.WriteLine($"Total Messages: {stats.UserMessages + stats.AssistantMessages + stats.ToolMessages}");
        Console.WriteLine($"Tool Calls: {stats.ToolCalls}");

        // Show what messages would be sent in the next turn
        Console.WriteLine("\n=== Messages for Next Turn ===");
        var nextTurnMessages = conversationManager.ExtractMessagesForNextTurn();
        var messageCount = 0;
        foreach (var msg in nextTurnMessages)
        {
            messageCount++;
            var preview = msg.Content.Length > 50 ? msg.Content.Substring(0, 50) + "..." : msg.Content;
            Console.WriteLine($"{messageCount}. [{msg.Role}] {preview}");
        }
    }

    static async Task RunSlidingWindowExample()
    {
        Console.WriteLine("2. Sliding Window Conversation Manager");
        Console.WriteLine("========================================\n");
        Console.WriteLine("This manager keeps only the last N messages in context.\n");

        var provider = new Llm7IoProvider("gpt-4o-mini-2024-07-18");
        var toolRegistry = new ToolRegistry();

        // Create sliding window manager with small window
        var conversationManager = new SlidingWindowConversationManager(
            windowSize: 6, // Keep only last 6 messages
            preserveFirstMessage: true // Keep system message if any
        );

        var assistant = new AssistantWithManager(conversationManager, toolRegistry, provider);

        // Simulate a longer conversation
        var questions = new[]
        {
            "Hi, I'm learning about numbers",
            "What is 2 + 2?",
            "What is 5 * 3?",
            "What is 10 / 2?",
            "What is 7 - 3?",
            "What was my first question?" // This should not remember the first question
        };

        foreach (var question in questions)
        {
            Console.WriteLine($"User: {question}");
            var response = await assistant.RunTurnAsync(question);
            Console.WriteLine($"Assistant: {response.Content}\n");
            await Task.Delay(1500); // Rate limiting
        }

        // Show window status
        Console.WriteLine("\n=== Sliding Window Status ===");
        var messages = conversationManager.ExtractMessagesForNextTurn().ToList();
        Console.WriteLine($"Messages in window: {messages.Count}");
        Console.WriteLine($"Window contains only recent messages (last {6} messages)");

        // Demonstrate compaction
        if (await conversationManager.CompactConversationAsync())
        {
            Console.WriteLine("Older messages have been summarized and removed from active window.");
        }
    }

    static async Task RunSemanticExample()
    {
        Console.WriteLine("3. Semantic Conversation Manager");
        Console.WriteLine("==================================\n");
        Console.WriteLine("This manager preserves messages based on semantic importance.\n");

        var provider = new Llm7IoProvider("gpt-4o-mini-2024-07-18");
        var toolRegistry = new ToolRegistry();
        toolRegistry.Register(new FibonacciTool());

        var conversationManager = new SemanticConversationManager(new ConversationManagerOptions
        {
            MaxRecentMessages = 8,
            CompressionStrategy = CompressionStrategy.Semantic
        });

        // Add custom important keywords
        if (conversationManager is SemanticConversationManager semantic)
        {
            semantic.AddImportantKeywords("project", "deadline", "budget", "requirement");
        }

        var assistant = new AssistantWithManager(conversationManager, toolRegistry, provider);

        // Simulate conversation with varying importance
        var exchanges = new[]
        {
            ("What's the weather like?", false), // Low importance
            ("The project deadline is next Friday", true), // High importance
            ("Calculate fibonacci of 5", false), // Medium importance (has tool)
            ("How are you today?", false), // Low importance
            ("Important: The budget is $50,000", true), // High importance
            ("What's 2+2?", false), // Low importance
            ("Remember the requirement is 99% uptime", true), // High importance
            ("Tell me about the important points we discussed", false) // Should recall important messages
        };

        foreach (var (question, important) in exchanges)
        {
            var marker = important ? "[IMPORTANT] " : "";
            Console.WriteLine($"{marker}User: {question}");
            var response = await assistant.RunTurnAsync(question);
            Console.WriteLine($"Assistant: {response.Content}\n");
            await Task.Delay(1500);
        }

        // Show which messages were preserved
        Console.WriteLine("\n=== Semantic Preservation ===");
        var preserved = conversationManager.ExtractMessagesForNextTurn().ToList();
        Console.WriteLine($"Preserved {preserved.Count} semantically important messages");

        foreach (var msg in preserved.Where(m => m.Role == Role.User))
        {
            var preview = msg.Content.Length > 60 ? msg.Content.Substring(0, 60) + "..." : msg.Content;
            Console.WriteLine($"  - {preview}");
        }
    }

    static async Task RunSummaryBasedExample()
    {
        Console.WriteLine("4. Summary-Based Conversation Manager");
        Console.WriteLine("======================================\n");
        Console.WriteLine("This manager replaces old messages with summaries.\n");

        var provider = new Llm7IoProvider("gpt-4o-mini-2024-07-18");
        var toolRegistry = new ToolRegistry();

        var options = new ConversationManagerOptions
        {
            MaxRecentMessages = 6,
            CompressionStrategy = CompressionStrategy.Summary,
            CompactionThreshold = 3, // Compact after just 3 turns for demo
            AutoCompact = true
        };

        var conversationManager = new DefaultConversationManager(options);
        var assistant = new AssistantWithManager(conversationManager, toolRegistry, provider);

        // Long conversation that will trigger summarization
        var conversation = new[]
        {
            "I want to learn about space",
            "Tell me about Mars",
            "How far is Mars from Earth?",
            "What about Jupiter?",
            "How many moons does Jupiter have?",
            "Can we live on any of these planets?"
        };

        for (int i = 0; i < conversation.Length; i++)
        {
            Console.WriteLine($"Turn {i + 1}: {conversation[i]}");
            var response = await assistant.RunTurnAsync(conversation[i]);
            Console.WriteLine($"Assistant: {response.Content.Substring(0, Math.Min(100, response.Content.Length))}...\n");

            await Task.Delay(1500);

            // Check if compaction occurred
            if (i == 2) // After 3 turns
            {
                Console.WriteLine("[System] Triggering conversation compaction...");
                if (await conversationManager.CompactConversationAsync())
                {
                    Console.WriteLine("[System] Old messages have been summarized.\n");
                }
            }
        }

        // Get and display summary
        Console.WriteLine("\n=== Conversation Summary ===");
        var summary = await assistant.GetConversationSummaryAsync();
        Console.WriteLine(summary);

        // Show current context
        Console.WriteLine("\n=== Current Context ===");
        var context = conversationManager.ExtractMessagesForNextTurn().ToList();
        Console.WriteLine($"Context contains {context.Count} messages");
        if (context.Any(m => m.Content.Contains("summary", StringComparison.OrdinalIgnoreCase)))
        {
            Console.WriteLine("Including summarized history from earlier turns");
        }
    }
}