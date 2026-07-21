namespace Andy.Model.Model;

/// <summary>
/// A single conversational turn: an opening user (or system) message followed by
/// an ordered sequence of assistant and tool messages produced in response.
///
/// The turn preserves the <em>complete</em> protocol sequence so that a turn with
/// tool calls is stored as distinct messages in provider-valid order:
/// <c>user → assistant(tool_calls) → tool result(s) → assistant(final)</c>, and
/// multi-step tool loops keep every intermediate assistant/tool message.
/// </summary>
/// <remarks>
/// <para><b>Migration note.</b> Earlier versions exposed a single settable
/// <see cref="AssistantMessage"/> plus a <see cref="ToolMessages"/> list. Those
/// members are retained for backward compatibility with manually-constructed
/// turns, but the authoritative representation is now the ordered
/// <see cref="Messages"/> sequence. When <see cref="Messages"/> is non-empty it
/// supersedes the legacy members for enumeration. Prefer
/// <see cref="AddAssistantMessage"/> / <see cref="AddToolMessage"/> for new code.</para>
/// </remarks>
public sealed class Turn
{
    public Message UserOrSystemMessage { get; init; } = new() { Role = Role.User, Content = string.Empty };

    /// <summary>
    /// Legacy single-assistant accessor. When the ordered <see cref="Messages"/>
    /// sequence is in use, the getter returns the last assistant message from it
    /// and the setter appends. Retained for manual construction and simple turns.
    /// </summary>
    public Message? AssistantMessage
    {
        get => _messages.Count > 0
            ? _messages.LastOrDefault(m => m.Role == Role.Assistant) ?? _legacyAssistantMessage
            : _legacyAssistantMessage;
        set
        {
            if (_messages.Count > 0)
            {
                if (value != null) _messages.Add(value);
            }
            else
            {
                _legacyAssistantMessage = value;
            }
        }
    }
    private Message? _legacyAssistantMessage;

    /// <summary>
    /// Legacy tool-results list. Populated for manually-constructed turns; the
    /// orchestrators use <see cref="AddToolMessage"/> which feeds <see cref="Messages"/>.
    /// </summary>
    public List<Message> ToolMessages { get; init; } = new(); // each with Role=Tool

    private readonly List<Message> _messages = new();

    /// <summary>
    /// Authoritative, ordered sequence of assistant and tool messages produced in
    /// response to <see cref="UserOrSystemMessage"/>, in provider-valid protocol order.
    /// Empty for legacy turns that only populate <see cref="AssistantMessage"/> /
    /// <see cref="ToolMessages"/>.
    /// </summary>
    public IReadOnlyList<Message> Messages => _messages;

    /// <summary>Append any message to the ordered response sequence, preserving order.</summary>
    public Turn AddMessage(Message message)
    {
        _messages.Add(message);
        return this;
    }

    /// <summary>Append an assistant message (text and/or tool calls) to the ordered sequence.</summary>
    public Turn AddAssistantMessage(Message message) => AddMessage(message);

    /// <summary>Append a tool-result message (Role=Tool) to the ordered sequence.</summary>
    public Turn AddToolMessage(Message message) => AddMessage(message);

    /// <summary>
    /// The final assistant message of the turn regardless of representation, i.e. the
    /// last assistant-role message that would be sent to a provider. Null if the turn
    /// has produced no assistant message yet.
    /// </summary>
    public Message? FinalAssistantMessage =>
        EnumerateMessages().LastOrDefault(m => m.Role == Role.Assistant);

    public IEnumerable<Message> EnumerateMessages()
    {
        yield return UserOrSystemMessage;

        // Ordered sequence supersedes legacy members when present.
        if (_messages.Count > 0)
        {
            foreach (var m in _messages)
            {
                yield return m;
            }
            yield break;
        }

        if (_legacyAssistantMessage != null) yield return _legacyAssistantMessage;
        foreach (var t in ToolMessages) yield return t;
    }
}
