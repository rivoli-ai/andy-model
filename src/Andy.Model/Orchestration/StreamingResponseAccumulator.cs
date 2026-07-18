using System.Text;
using Andy.Model.Model;

namespace Andy.Model.Orchestration;

/// <summary>
/// Accumulates streamed assistant deltas into a single, complete assistant message:
/// text fragments are concatenated and fragmented tool-call deltas are merged by call
/// identity into whole tool calls with reassembled JSON arguments.
/// </summary>
internal sealed class StreamingResponseAccumulator
{
    private sealed class CallBuffer
    {
        public string Id = string.Empty;
        public string Name = string.Empty;
        public readonly StringBuilder Args = new();
        public bool Anonymous;
    }

    private readonly StringBuilder _content = new();
    private readonly List<CallBuffer> _calls = new();
    private readonly Dictionary<string, CallBuffer> _callsById = new();
    private CallBuffer? _current;

    public bool HasToolCalls => _calls.Count > 0;

    public void AddDelta(Message delta)
    {
        if (!string.IsNullOrEmpty(delta.Content))
        {
            _content.Append(delta.Content);
        }

        foreach (var tc in delta.ToolCalls)
        {
            if (!string.IsNullOrEmpty(tc.Id))
            {
                if (!_callsById.TryGetValue(tc.Id, out var buffer))
                {
                    buffer = new CallBuffer { Id = tc.Id };
                    _callsById[tc.Id] = buffer;
                    _calls.Add(buffer);
                }
                _current = buffer;
                MergeInto(buffer, tc);
            }
            else if (_current != null)
            {
                // Continuation fragment for the call currently being streamed.
                MergeInto(_current, tc);
            }
            else
            {
                var buffer = new CallBuffer { Anonymous = true };
                _calls.Add(buffer);
                _current = buffer;
                MergeInto(buffer, tc);
            }
        }
    }

    private static void MergeInto(CallBuffer buffer, ToolCall tc)
    {
        if (!string.IsNullOrEmpty(tc.Name) && string.IsNullOrEmpty(buffer.Name))
        {
            buffer.Name = tc.Name;
        }

        var args = tc.ArgumentsJson;
        if (!string.IsNullOrEmpty(args) && args != "{}")
        {
            buffer.Args.Append(args);
        }
    }

    /// <summary>Build the complete assistant message from all accumulated deltas.</summary>
    public Message Build()
    {
        var toolCalls = _calls
            .Select(b => new ToolCall
            {
                Id = string.IsNullOrEmpty(b.Id) ? Guid.NewGuid().ToString("N") : b.Id,
                Name = b.Name,
                ArgumentsJson = b.Args.Length > 0 ? b.Args.ToString() : "{}"
            })
            .ToList();

        return new Message
        {
            Role = Role.Assistant,
            Content = _content.ToString(),
            ToolCalls = toolCalls
        };
    }
}
