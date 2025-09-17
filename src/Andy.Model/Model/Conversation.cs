namespace Andy.Model.Model;

/// <summary>
/// Global conversation store with enhanced state management.
/// </summary>
public sealed class Conversation
{
    private readonly List<Turn> _turns = new();
    private readonly Dictionary<string, object> _state = new();

    public IReadOnlyList<Turn> Turns => _turns;

    // Conversation metadata
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    
    // UTC timestamp when the conversation was created.
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    
    // UTC timestamp of the last activity (message added).
    public DateTimeOffset LastActivityAt { get; private set; } = DateTimeOffset.UtcNow;

    // Add a new turn to the conversation.
    public void AddTurn(Turn t)
    {
        _turns.Add(t);
        LastActivityAt = DateTimeOffset.UtcNow;
    }

    /// <summary>
    /// Flattens to message list in absolute chronological order.
    /// </summary>
    public IEnumerable<Message> ToChronoMessages()
    {
        // Use yield return to avoid creating intermediate lists
        return _turns.SelectMany(t => t.EnumerateMessages());
    }

    /// <summary>
    /// State management for conversation context.
    /// </summary>
    public T? GetState<T>(string key) where T : class
    {
        return _state.TryGetValue(key, out var value) ? value as T : null;
    }

    /// <summary>
    /// Set state value for a given key.
    /// </summary>
    /// <param name="key"></param>
    /// <param name="value"></param>
    /// <typeparam name="T"></typeparam>
    public void SetState<T>(string key, T value) where T : class
    {
        _state[key] = value;
    }

    /// <summary>
    /// Clear all state entries.
    /// </summary>
    public void ClearState()
    {
        _state.Clear();
    }
}