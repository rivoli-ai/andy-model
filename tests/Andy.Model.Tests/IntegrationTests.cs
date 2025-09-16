using Andy.Model.Examples;
using Andy.Model.Model;
using Andy.Model.Tooling;
using Andy.Model.Utils;

namespace Andy.Model.Tests;

public class IntegrationTests
{

    [Fact]
    public void Conversation_StateManagement_WorksCorrectly()
    {
        // Arrange
        var conversation = new Conversation();

        // Act
        conversation.SetState("user_id", "12345");
        conversation.SetState("session_id", "abc123");
        conversation.SetState("preferences", new { theme = "dark", language = "en" });

        // Assert
        Assert.Equal("12345", conversation.GetState<string>("user_id"));
        Assert.Equal("abc123", conversation.GetState<string>("session_id"));
        Assert.NotNull(conversation.GetState<object>("preferences"));
        Assert.Null(conversation.GetState<string>("nonexistent"));
    }

    [Fact]
    public void ToolRegistry_Management_WorksCorrectly()
    {
        // Arrange
        var registry = new ToolRegistry();
        var tool = new CalculatorTool();

        // Act
        registry.Register(tool);

        // Assert
        Assert.True(registry.IsRegistered("calculator"));
        Assert.True(registry.TryGet("calculator", out var retrievedTool));
        Assert.Same(tool, retrievedTool);
        Assert.Single(registry.GetDeclaredTools());
    }
}