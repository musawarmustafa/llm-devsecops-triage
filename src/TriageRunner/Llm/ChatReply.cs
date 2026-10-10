namespace TriageRunner.Llm;

/// <summary>
/// The parts of a chat completion reply that triage cares about.
/// </summary>
/// <param name="HasContent">True when the message carried a "content" field at all.</param>
/// <param name="Content">The answer text. Null if the field was missing or JSON null.</param>
/// <param name="Reasoning">Reasoning text that some models return separately. Null if absent.</param>
public sealed record ChatReply(bool HasContent, string? Content, string? Reasoning);
