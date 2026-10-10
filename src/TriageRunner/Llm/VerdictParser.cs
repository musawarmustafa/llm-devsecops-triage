namespace TriageRunner.Llm;

/// <summary>
/// How a strategy reads the model's reply.
/// </summary>
/// <param name="RequireContent">If true, a reply with no "content" field counts as an error.</param>
/// <param name="FallBackToReasoning">If the content is empty, read the model's "reasoning" field instead.</param>
/// <param name="MaxUnclearLength">Trim UNCLEAR replies to this many characters. Null keeps the full text.</param>
public sealed record VerdictRules(bool RequireContent, bool FallBackToReasoning, int? MaxUnclearLength)
{
    public static readonly VerdictRules Strict = new(RequireContent: true, FallBackToReasoning: false, MaxUnclearLength: null);
    public static readonly VerdictRules Lenient = new(RequireContent: false, FallBackToReasoning: true, MaxUnclearLength: 50);
}

/// <summary>
/// Maps the model's reply to a verdict label.
/// The model is told to answer with one word, but the parser is forgiving:
/// any reply containing "TP" is a true positive, otherwise any reply containing
/// "FP" is a false positive. Anything else is kept as UNCLEAR for manual review.
/// </summary>
public static class VerdictParser
{
    public const string TruePositive = "TP";
    public const string FalsePositive = "FP";
    public const string Error = "ERROR";
    public const string RateLimited = "ERROR:RateLimitedAfterRetries";

    public static string Parse(ChatReply reply, VerdictRules rules)
    {
        if (rules.RequireContent && !reply.HasContent)
        {
            throw new LlmRequestException("Response had no message content.");
        }

        var text = (reply.Content ?? string.Empty).Trim().ToUpperInvariant();

        if (text.Length == 0 && rules.FallBackToReasoning && reply.Reasoning is not null)
        {
            text = reply.Reasoning.ToUpperInvariant();
        }

        if (text.Contains(TruePositive)) return TruePositive;
        if (text.Contains(FalsePositive)) return FalsePositive;

        if (rules.MaxUnclearLength is int max && text.Length > max)
        {
            text = text[..max];
        }

        return $"UNCLEAR:{text}";
    }
}
