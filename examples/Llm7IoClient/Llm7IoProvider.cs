using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Andy.Model.Llm;
using Andy.Model.Model;
using Andy.Model.Tooling;

namespace Llm7IoClient;

/// <summary>
/// LLM provider implementation for llm7.io API (OpenAI-compatible).
/// </summary>
public sealed class Llm7IoProvider : ILlmProvider
{
    private readonly HttpClient _httpClient;
    private readonly string _baseUrl = "https://api.llm7.io/v1";
    private readonly string _model;
    private readonly JsonSerializerOptions _jsonOptions;
    private readonly int _maxRetries = 3;
    private readonly int _baseDelayMs = 1000;

    public Llm7IoProvider(string model = "gpt-4o-mini-2024-07-18")
    {
        _model = model;
        _httpClient = new HttpClient();
        _httpClient.DefaultRequestHeaders.Add("Accept", "application/json");

        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            WriteIndented = false
        };
    }

    public string Name => "llm7.io";

    public async Task<LlmResponse> CompleteAsync(LlmRequest request, CancellationToken cancellationToken = default)
    {
        var openAiRequest = ConvertToOpenAiRequest(request);
        var json = JsonSerializer.Serialize(openAiRequest, _jsonOptions);

        for (int retry = 0; retry <= _maxRetries; retry++)
        {
            try
            {
                var content = new StringContent(json, Encoding.UTF8, "application/json");
                var response = await _httpClient.PostAsync($"{_baseUrl}/chat/completions", content, cancellationToken);

                // Check for rate limiting
                if (response.StatusCode == HttpStatusCode.TooManyRequests)
                {
                    if (retry < _maxRetries)
                    {
                        // Exponential backoff with jitter
                        var delay = _baseDelayMs * Math.Pow(2, retry) + Random.Shared.Next(100, 500);
                        Console.WriteLine($"[Rate Limited] Waiting {delay}ms before retry {retry + 1}/{_maxRetries}...");
                        await Task.Delay(TimeSpan.FromMilliseconds(delay), cancellationToken);
                        continue;
                    }
                }

                response.EnsureSuccessStatusCode();

                var responseJson = await response.Content.ReadAsStringAsync(cancellationToken);
                var openAiResponse = JsonSerializer.Deserialize<OpenAiChatCompletionResponse>(responseJson, _jsonOptions);

                return ConvertFromOpenAiResponse(openAiResponse);
            }
            catch (HttpRequestException ex) when (retry < _maxRetries && ex.Message.Contains("429"))
            {
                // Handle rate limiting exception
                var delay = _baseDelayMs * Math.Pow(2, retry) + Random.Shared.Next(100, 500);
                Console.WriteLine($"[Rate Limited] Waiting {delay}ms before retry {retry + 1}/{_maxRetries}...");
                await Task.Delay(TimeSpan.FromMilliseconds(delay), cancellationToken);
            }
            catch (TaskCanceledException) when (retry < _maxRetries && !cancellationToken.IsCancellationRequested)
            {
                // Handle timeout
                var delay = _baseDelayMs * Math.Pow(2, retry);
                Console.WriteLine($"[Timeout] Waiting {delay}ms before retry {retry + 1}/{_maxRetries}...");
                await Task.Delay(TimeSpan.FromMilliseconds(delay), cancellationToken);
            }
        }

        throw new HttpRequestException($"Failed to complete request after {_maxRetries} retries");
    }

    public async IAsyncEnumerable<LlmStreamResponse> StreamCompleteAsync(
        LlmRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        // For simplicity, we'll use the non-streaming API and return a single chunk
        // A real implementation would use Server-Sent Events (SSE) for streaming
        var response = await CompleteAsync(request, cancellationToken);

        yield return new LlmStreamResponse
        {
            Delta = response.AssistantMessage,
            IsComplete = true,
            Usage = response.Usage
        };
    }

    public async Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _httpClient.GetAsync($"{_baseUrl}/models", cancellationToken);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    public async Task<IEnumerable<ModelInfo>> ListModelsAsync(CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.GetAsync($"{_baseUrl}/models", cancellationToken);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        var modelsResponse = JsonSerializer.Deserialize<OpenAiModelsResponse>(json, _jsonOptions);

        return modelsResponse?.Data?.Select(m => new ModelInfo
        {
            Id = m.Id,
            Name = m.Id,
            UpdatedAt = m.Created.HasValue ? DateTimeOffset.FromUnixTimeSeconds(m.Created.Value) : null,
            SupportsFunctions = true,
            SupportsStreaming = true
        }) ?? Enumerable.Empty<ModelInfo>();
    }

    private OpenAiChatCompletionRequest ConvertToOpenAiRequest(LlmRequest request)
    {
        var openAiRequest = new OpenAiChatCompletionRequest
        {
            Model = request.Config?.Model ?? _model,
            Messages = request.Messages.Select(ConvertMessage).ToList(),
            Temperature = (double)(request.Config?.Temperature ?? 0.7m),
            MaxTokens = request.Config?.MaxTokens ?? 2000
        };

        if (request.Tools?.Any() == true)
        {
            openAiRequest.Tools = request.Tools.Select(ConvertTool).ToList();
            openAiRequest.ToolChoice = "auto";
        }

        return openAiRequest;
    }

    private OpenAiMessage ConvertMessage(Message message)
    {
        var openAiMessage = new OpenAiMessage
        {
            Role = message.Role.ToString().ToLowerInvariant(),
            Content = message.Content
        };

        if (message.ToolCalls?.Any() == true)
        {
            openAiMessage.ToolCalls = message.ToolCalls.Select(tc => new OpenAiToolCall
            {
                Id = tc.Id,
                Type = "function",
                Function = new OpenAiToolCallFunction
                {
                    Name = tc.Name,
                    Arguments = tc.ArgumentsJson
                }
            }).ToList();
        }

        if (message.Role == Role.Tool && message.ToolResults?.Any() == true)
        {
            var toolResult = message.ToolResults.First();
            openAiMessage.ToolCallId = toolResult.CallId;
        }

        return openAiMessage;
    }

    private OpenAiTool ConvertTool(ToolDeclaration tool)
    {
        return new OpenAiTool
        {
            Type = "function",
            Function = new OpenAiFunction
            {
                Name = tool.Name,
                Description = tool.Description,
                Parameters = tool.Parameters
            }
        };
    }

    private LlmResponse ConvertFromOpenAiResponse(OpenAiChatCompletionResponse openAiResponse)
    {
        var choice = openAiResponse?.Choices?.FirstOrDefault();
        if (choice?.Message == null)
        {
            throw new InvalidOperationException("Invalid response from LLM");
        }

        var toolCalls = choice.Message.ToolCalls?.Any() == true
            ? choice.Message.ToolCalls.Select(tc => new ToolCall
            {
                Id = tc.Id,
                Name = tc.Function.Name,
                ArgumentsJson = tc.Function.Arguments
            }).ToList()
            : new List<ToolCall>();

        var assistantMessage = new Message
        {
            Role = Role.Assistant,
            Content = choice.Message.Content ?? string.Empty,
            Timestamp = DateTimeOffset.Now,
            ToolCalls = toolCalls,
            Metadata = new Dictionary<string, object>
            {
                ["model"] = openAiResponse.Model ?? _model,
                ["request_id"] = openAiResponse.Id ?? string.Empty
            }
        };

        return new LlmResponse
        {
            AssistantMessage = assistantMessage,
            Usage = openAiResponse.Usage != null ? new LlmUsage
            {
                PromptTokens = openAiResponse.Usage.PromptTokens,
                CompletionTokens = openAiResponse.Usage.CompletionTokens,
                TotalTokens = openAiResponse.Usage.TotalTokens
            } : null
        };
    }

    public void Dispose()
    {
        _httpClient?.Dispose();
    }

    // OpenAI API Models (internal)
    private class OpenAiChatCompletionRequest
    {
        public string Model { get; set; }
        public List<OpenAiMessage> Messages { get; set; }
        public double? Temperature { get; set; }
        [JsonPropertyName("max_tokens")]
        public int? MaxTokens { get; set; }
        public List<OpenAiTool> Tools { get; set; }
        [JsonPropertyName("tool_choice")]
        public object ToolChoice { get; set; }
    }

    private class OpenAiMessage
    {
        public string Role { get; set; }
        public string Content { get; set; }
        [JsonPropertyName("tool_calls")]
        public List<OpenAiToolCall> ToolCalls { get; set; }
        [JsonPropertyName("tool_call_id")]
        public string ToolCallId { get; set; }
    }

    private class OpenAiTool
    {
        public string Type { get; set; }
        public OpenAiFunction Function { get; set; }
    }

    private class OpenAiFunction
    {
        public string Name { get; set; }
        public string Description { get; set; }
        public object Parameters { get; set; }
    }

    private class OpenAiToolCall
    {
        public string Id { get; set; }
        public string Type { get; set; }
        public OpenAiToolCallFunction Function { get; set; }
    }

    private class OpenAiToolCallFunction
    {
        public string Name { get; set; }
        public string Arguments { get; set; }
    }

    private class OpenAiChatCompletionResponse
    {
        public string Id { get; set; }
        public string Object { get; set; }
        public long Created { get; set; }
        public string Model { get; set; }
        public List<OpenAiChoice> Choices { get; set; }
        public OpenAiUsage Usage { get; set; }
    }

    private class OpenAiChoice
    {
        public int Index { get; set; }
        public OpenAiMessage Message { get; set; }
        [JsonPropertyName("finish_reason")]
        public string FinishReason { get; set; }
    }

    private class OpenAiUsage
    {
        [JsonPropertyName("prompt_tokens")]
        public int PromptTokens { get; set; }
        [JsonPropertyName("completion_tokens")]
        public int CompletionTokens { get; set; }
        [JsonPropertyName("total_tokens")]
        public int TotalTokens { get; set; }
    }

    private class OpenAiModelsResponse
    {
        public List<OpenAiModel> Data { get; set; }
    }

    private class OpenAiModel
    {
        public string Id { get; set; }
        public string Object { get; set; }
        public long? Created { get; set; }
        [JsonPropertyName("owned_by")]
        public string OwnedBy { get; set; }
    }
}