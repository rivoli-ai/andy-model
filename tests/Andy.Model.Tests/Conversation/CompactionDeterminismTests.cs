using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Andy.Model.Conversation;
using Andy.Model.Model;
using Xunit;

namespace Andy.Model.Tests.Conversation;

/// <summary>
/// Deterministic tests for automatic compaction and strategy state (#17). None rely on
/// timing delays to observe completion.
/// </summary>
public class CompactionDeterminismTests
{
    private static Turn UserTurn(string content)
        => new() { UserOrSystemMessage = new Message { Role = Role.User, Content = content } };

    [Fact]
    public async Task CompactIfNeeded_HonorsAutoCompactFalse()
    {
        var manager = new DefaultConversationManager(new ConversationManagerOptions
        {
            AutoCompact = false,
            CompactionThreshold = 1,
            CompressionStrategy = CompressionStrategy.Summary
        });
        manager.AddTurn(UserTurn("a"));
        manager.AddTurn(UserTurn("b"));

        var compacted = await manager.CompactIfNeededAsync();

        Assert.False(compacted);
        Assert.Null(manager.Conversation.GetState<string>("conversation_summary"));
    }

    [Fact]
    public async Task CompactIfNeeded_RunsWhenEnabledAndNeeded()
    {
        var manager = new DefaultConversationManager(new ConversationManagerOptions
        {
            AutoCompact = true,
            CompactionThreshold = 1,
            MaxRecentMessages = 2,
            CompressionStrategy = CompressionStrategy.Summary
        });
        manager.AddTurn(UserTurn("a"));
        manager.AddTurn(UserTurn("b"));
        manager.AddTurn(UserTurn("c"));

        var compacted = await manager.CompactIfNeededAsync();

        Assert.True(compacted);
    }

    [Fact]
    public async Task SlidingWindow_RepeatedCompaction_DoesNotDuplicateSummaries()
    {
        var manager = new SlidingWindowConversationManager(windowSize: 2);
        for (int i = 0; i < 4; i++) manager.AddTurn(UserTurn($"m{i}"));

        var first = await manager.CompactConversationAsync();
        // No new messages fell out of the window; a second call must be a no-op.
        var second = await manager.CompactConversationAsync();
        var third = await manager.CompactConversationAsync();

        Assert.True(first);
        Assert.False(second);
        Assert.False(third);
    }

    [Fact]
    public async Task SlidingWindow_Reset_ClearsSummaryState()
    {
        var manager = new SlidingWindowConversationManager(windowSize: 1);
        for (int i = 0; i < 3; i++) manager.AddTurn(UserTurn($"m{i}"));
        Assert.True(await manager.CompactConversationAsync());

        manager.Reset();

        // After reset the high-water mark is cleared, so compaction can run again.
        Assert.True(await manager.CompactConversationAsync());
    }

    [Fact]
    public async Task CompactIfNeeded_PropagatesStrategyException()
    {
        var manager = new ThrowingCompactionManager(new ConversationManagerOptions
        {
            AutoCompact = true,
            CompactionThreshold = 0
        });
        manager.AddTurn(UserTurn("a"));

        await Assert.ThrowsAsync<InvalidOperationException>(() => manager.CompactIfNeededAsync());
    }

    private sealed class ThrowingCompactionManager : DefaultConversationManager
    {
        public ThrowingCompactionManager(ConversationManagerOptions options) : base(options) { }

        public override Task<bool> CompactConversationAsync()
            => throw new InvalidOperationException("compaction failed");
    }
}
