namespace Andy.Model.Model;

/// <summary>
/// Opt-in marker requesting that a provider place a prompt-caching breakpoint
/// at this point in the request.
///
/// Anthropic/Claude (including via OpenRouter) only caches a prefix when the
/// request carries an explicit <c>cache_control</c> breakpoint on a content
/// block (typically the system prompt and/or the last stable message).
/// OpenAI/DeepSeek auto-cache and ignore this marker. Default behavior is
/// unchanged: callers that never set a <see cref="CacheControl"/> emit no
/// breakpoint and providers behave exactly as before.
/// </summary>
public sealed record CacheControl(string Type = "ephemeral")
{
    /// <summary>
    /// The standard ephemeral cache breakpoint, matching Anthropic's
    /// <c>{"type":"ephemeral"}</c> shape.
    /// </summary>
    public static readonly CacheControl Ephemeral = new("ephemeral");
}
