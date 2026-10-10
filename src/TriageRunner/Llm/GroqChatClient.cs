using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TriageRunner.Llm;

/// <summary>
/// Thin wrapper around Groq's OpenAI-compatible chat completions endpoint.
/// </summary>
public sealed class GroqChatClient : IDisposable
{
    public const string DefaultEndpoint = "https://api.groq.com/openai/v1/chat/completions";
    public const string DefaultModel = "openai/gpt-oss-120b";

    private readonly HttpClient _http;
    private readonly string _endpoint;
    private readonly string _model;
    private readonly TimeSpan _rateLimitWait;

    public GroqChatClient(string apiKey, string? endpoint = null, string? model = null, TimeSpan? rateLimitWait = null)
    {
        _endpoint = endpoint ?? DefaultEndpoint;
        _model = model ?? DefaultModel;
        _rateLimitWait = rateLimitWait ?? TimeSpan.FromSeconds(15);
        _http = new HttpClient();
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
    }

    /// <summary>
    /// Sends the messages and returns the reply.
    /// Generation settings are fixed for reproducibility:
    /// deterministic output (temperature 0) and low reasoning effort to stay within the Groq free tier.
    /// </summary>
    /// <param name="rateLimitAttempts">
    /// Attempts allowed when the API answers HTTP 429. After each 429 the client waits and tries again.
    /// 0 treats a 429 like any other error.
    /// </param>
    public async Task<ChatReply> CompleteAsync(
        IReadOnlyList<ChatMessage> messages, int rateLimitAttempts = 0, CancellationToken ct = default)
    {
        var json = JsonSerializer.Serialize(new CompletionRequest(
            Model: _model,
            Messages: messages,
            Temperature: 0,
            MaxTokens: 500,
            ReasoningEffort: "low"));

        int attemptsLeft = Math.Max(rateLimitAttempts, 1);

        while (attemptsLeft-- > 0)
        {
            using var payload = new StringContent(json, Encoding.UTF8, "application/json");
            using var response = await _http.PostAsync(_endpoint, payload, ct);
            var body = await response.Content.ReadAsStringAsync(ct);

            if (rateLimitAttempts > 0 && response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                Console.WriteLine($"  Rate limited. Waiting {_rateLimitWait.TotalSeconds:0}s before trying again.");
                await Task.Delay(_rateLimitWait, ct);
                continue;
            }

            if (!response.IsSuccessStatusCode)
            {
                throw new LlmRequestException($"{(int)response.StatusCode} {response.StatusCode}: {body}");
            }

            return ReadReply(body);
        }

        throw new RateLimitedException();
    }

    private static ChatReply ReadReply(string body)
    {
        using var doc = JsonDocument.Parse(body);
        var message = doc.RootElement.GetProperty("choices")[0].GetProperty("message");

        bool hasContent = message.TryGetProperty("content", out var contentEl);
        string? content = hasContent ? contentEl.GetString() : null;

        string? reasoning = message.TryGetProperty("reasoning", out var reasoningEl)
                            && reasoningEl.ValueKind == JsonValueKind.String
            ? reasoningEl.GetString()
            : null;

        return new ChatReply(hasContent, content, reasoning);
    }

    public void Dispose() => _http.Dispose();

    private sealed record CompletionRequest(
        [property: JsonPropertyName("model")] string Model,
        [property: JsonPropertyName("messages")] IReadOnlyList<ChatMessage> Messages,
        [property: JsonPropertyName("temperature")] double Temperature,
        [property: JsonPropertyName("max_tokens")] int MaxTokens,
        [property: JsonPropertyName("reasoning_effort")] string ReasoningEffort);
}

public class LlmRequestException : Exception
{
    public LlmRequestException(string message) : base(message) { }
}

public sealed class RateLimitedException : LlmRequestException
{
    public RateLimitedException() : base("Still rate limited after all retry attempts.") { }
}
