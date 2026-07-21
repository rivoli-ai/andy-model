using System.Text.Json;
using System.Text.Json.Serialization;
using Andy.Model.Model;

namespace Andy.Model.Utils;

/// <summary>
/// DTO-based, lossless serialization for <see cref="Model.Conversation"/>.
/// </summary>
/// <remarks>
/// <para>The on-disk shape is a <see cref="ConversationDto"/> tree with a
/// <see cref="ConversationDto.FormatVersion"/> so future readers can detect
/// incompatibility. It captures conversation id and timestamps, every turn's ordered
/// message sequence (or legacy assistant/tool split), and each message's id, role,
/// timestamp, tool-call id, tool calls, tool results, metadata, and cache control, plus
/// conversation state.</para>
/// <para><b>State and metadata type policy.</b> Values in conversation state and message
/// metadata are arbitrary. They are written as their natural JSON representation and, on
/// read, restored to CLR primitives where unambiguous (string, bool, <see cref="long"/>,
/// <see cref="double"/>). JSON objects and arrays are restored as <see cref="JsonElement"/>.
/// Callers that need typed complex values back should store JSON-friendly primitives or
/// re-parse the <see cref="JsonElement"/> themselves.</para>
/// </remarks>
internal static class ConversationSerialization
{
    public const int CurrentFormatVersion = 1;

    public static string Serialize(Model.Conversation conversation)
        => JsonSerializer.Serialize(ToDto(conversation), JsonOptions.Default);

    public static Model.Conversation Deserialize(string json)
    {
        ConversationDto? dto;
        try
        {
            dto = JsonSerializer.Deserialize<ConversationDto>(json, JsonOptions.Default);
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException($"Failed to deserialize conversation: malformed JSON. {ex.Message}", ex);
        }

        if (dto == null)
        {
            throw new InvalidOperationException("Failed to deserialize conversation: payload was null.");
        }

        if (dto.FormatVersion > CurrentFormatVersion)
        {
            throw new InvalidOperationException(
                $"Failed to deserialize conversation: format version {dto.FormatVersion} is newer than the supported version {CurrentFormatVersion}.");
        }

        return FromDto(dto);
    }

    private static ConversationDto ToDto(Model.Conversation conversation)
    {
        var dto = new ConversationDto
        {
            FormatVersion = CurrentFormatVersion,
            Id = conversation.Id,
            CreatedAt = conversation.CreatedAt,
            LastActivityAt = conversation.LastActivityAt,
            Turns = conversation.Turns.Select(ToDto).ToList(),
            State = conversation.GetStateSnapshot().ToDictionary(kv => kv.Key, kv => kv.Value)
        };
        return dto;
    }

    private static TurnDto ToDto(Turn turn)
    {
        var dto = new TurnDto
        {
            UserOrSystemMessage = ToDto(turn.UserOrSystemMessage)
        };

        if (turn.Messages.Count > 0)
        {
            dto.Messages = turn.Messages.Select(ToDto).ToList();
        }
        else
        {
            dto.AssistantMessage = turn.AssistantMessage != null ? ToDto(turn.AssistantMessage) : null;
            dto.ToolMessages = turn.ToolMessages.Select(ToDto).ToList();
        }

        return dto;
    }

    private static MessageDto ToDto(Message message) => new()
    {
        Role = message.Role,
        Content = message.Content,
        Id = message.Id,
        Timestamp = message.Timestamp,
        ToolCallId = message.ToolCallId,
        CacheControl = message.CacheControl,
        ToolCalls = message.ToolCalls.Select(tc => new ToolCallDto
        {
            Id = tc.Id,
            Name = tc.Name,
            ArgumentsJson = tc.ArgumentsJson
        }).ToList(),
        ToolResults = message.ToolResults.Select(tr => new ToolResultDto
        {
            CallId = tr.CallId,
            Name = tr.Name,
            IsError = tr.IsError,
            ResultJson = tr.ResultJson
        }).ToList(),
        Metadata = message.Metadata.ToDictionary(kv => kv.Key, kv => kv.Value)
    };

    private static Model.Conversation FromDto(ConversationDto dto)
    {
        var conversation = new Model.Conversation
        {
            Id = dto.Id ?? Guid.NewGuid().ToString("N"),
            CreatedAt = dto.CreatedAt
        };

        foreach (var turnDto in dto.Turns)
        {
            conversation.AddTurn(FromDto(turnDto));
        }

        foreach (var kv in dto.State)
        {
            conversation.RestoreState(kv.Key, NormalizeValue(kv.Value));
        }

        conversation.RestoreActivityTimestamp(dto.LastActivityAt);
        return conversation;
    }

    private static Turn FromDto(TurnDto dto)
    {
        var turn = new Turn
        {
            UserOrSystemMessage = FromDto(dto.UserOrSystemMessage ?? new MessageDto { Role = Role.User })
        };

        if (dto.Messages.Count > 0)
        {
            foreach (var m in dto.Messages)
            {
                turn.AddMessage(FromDto(m));
            }
        }
        else
        {
            if (dto.AssistantMessage != null)
            {
                turn.AssistantMessage = FromDto(dto.AssistantMessage);
            }
            foreach (var t in dto.ToolMessages)
            {
                turn.ToolMessages.Add(FromDto(t));
            }
        }

        return turn;
    }

    private static Message FromDto(MessageDto dto) => new()
    {
        Role = dto.Role,
        Content = dto.Content ?? string.Empty,
        Id = dto.Id ?? Guid.NewGuid().ToString("N"),
        Timestamp = dto.Timestamp,
        ToolCallId = dto.ToolCallId,
        CacheControl = dto.CacheControl,
        ToolCalls = dto.ToolCalls.Select(tc => new ToolCall
        {
            Id = tc.Id ?? Guid.NewGuid().ToString("N"),
            Name = tc.Name ?? string.Empty,
            ArgumentsJson = tc.ArgumentsJson ?? "{}"
        }).ToList(),
        ToolResults = dto.ToolResults.Select(tr => new ToolResult
        {
            CallId = tr.CallId ?? string.Empty,
            Name = tr.Name ?? string.Empty,
            IsError = tr.IsError,
            ResultJson = tr.ResultJson ?? "{}"
        }).ToList(),
        Metadata = dto.Metadata.ToDictionary(kv => kv.Key, kv => NormalizeValue(kv.Value))
    };

    /// <summary>
    /// Convert a deserialized state/metadata value (a <see cref="JsonElement"/> when it came
    /// through System.Text.Json) into a CLR primitive where unambiguous; objects and arrays are
    /// kept as <see cref="JsonElement"/>.
    /// </summary>
    private static object NormalizeValue(object value)
    {
        if (value is not JsonElement element)
        {
            return value;
        }

        return element.ValueKind switch
        {
            JsonValueKind.String => element.GetString()!,
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Number => element.TryGetInt64(out var l) ? l : element.GetDouble(),
            JsonValueKind.Null => null!,
            _ => element.Clone()
        };
    }

    // ---- DTOs ----

    internal sealed class ConversationDto
    {
        public int FormatVersion { get; set; } = CurrentFormatVersion;
        public string? Id { get; set; }
        public DateTimeOffset CreatedAt { get; set; }
        public DateTimeOffset LastActivityAt { get; set; }
        public List<TurnDto> Turns { get; set; } = new();
        public Dictionary<string, object> State { get; set; } = new();
    }

    internal sealed class TurnDto
    {
        public MessageDto? UserOrSystemMessage { get; set; }
        public List<MessageDto> Messages { get; set; } = new();
        public MessageDto? AssistantMessage { get; set; }
        public List<MessageDto> ToolMessages { get; set; } = new();
    }

    internal sealed class MessageDto
    {
        [JsonConverter(typeof(JsonStringEnumConverter))]
        public Role Role { get; set; }
        public string? Content { get; set; }
        public string? Id { get; set; }
        public DateTimeOffset Timestamp { get; set; }
        public string? ToolCallId { get; set; }
        public CacheControl? CacheControl { get; set; }
        public List<ToolCallDto> ToolCalls { get; set; } = new();
        public List<ToolResultDto> ToolResults { get; set; } = new();
        public Dictionary<string, object> Metadata { get; set; } = new();
    }

    internal sealed class ToolCallDto
    {
        public string? Id { get; set; }
        public string? Name { get; set; }
        public string? ArgumentsJson { get; set; }
    }

    internal sealed class ToolResultDto
    {
        public string? CallId { get; set; }
        public string? Name { get; set; }
        public bool IsError { get; set; }
        public string? ResultJson { get; set; }
    }
}
