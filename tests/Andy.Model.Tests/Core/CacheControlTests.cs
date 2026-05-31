using Andy.Model.Llm;
using Andy.Model.Model;

namespace Andy.Model.Tests.Core;

public class CacheControlTests
{
    [Fact]
    public void CacheControl_DefaultsToEphemeral()
    {
        var cc = new CacheControl();
        Assert.Equal("ephemeral", cc.Type);
    }

    [Fact]
    public void CacheControl_EphemeralSingleton_HasExpectedType()
    {
        Assert.Equal("ephemeral", CacheControl.Ephemeral.Type);
    }

    [Fact]
    public void Message_CacheControl_DefaultsToNull()
    {
        var message = new Message { Role = Role.User, Content = "hi" };
        Assert.Null(message.CacheControl);
    }

    [Fact]
    public void Message_CanCarryCacheBreakpoint()
    {
        var message = new Message
        {
            Role = Role.User,
            Content = "stable prefix",
            CacheControl = CacheControl.Ephemeral
        };

        Assert.NotNull(message.CacheControl);
        Assert.Equal("ephemeral", message.CacheControl!.Type);
    }

    [Fact]
    public void LlmRequest_CacheSystemPrompt_DefaultsToFalse()
    {
        var request = new LlmRequest
        {
            Messages = new List<Message> { new() { Role = Role.User, Content = "hi" } }
        };

        Assert.False(request.CacheSystemPrompt);
    }

    [Fact]
    public void LlmRequest_CacheSystemPrompt_CanBeEnabled()
    {
        var request = new LlmRequest
        {
            Messages = new List<Message> { new() { Role = Role.User, Content = "hi" } },
            SystemPrompt = "stable system prompt",
            CacheSystemPrompt = true
        };

        Assert.True(request.CacheSystemPrompt);
    }
}
