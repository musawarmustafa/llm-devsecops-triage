using System.Text.Json.Serialization;

namespace TriageRunner.Llm;

/// <summary>One message in an OpenAI-compatible chat completion request.</summary>
public sealed record ChatMessage(
    [property: JsonPropertyName("role")] string Role,
    [property: JsonPropertyName("content")] string Content)
{
    public static ChatMessage System(string content) => new("system", content);
    public static ChatMessage User(string content) => new("user", content);
    public static ChatMessage Assistant(string content) => new("assistant", content);
}
