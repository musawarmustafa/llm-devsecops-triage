using TriageRunner.Llm;
using TriageRunner.Models;

namespace TriageRunner.Prompts;

/// <summary>
/// Zero-shot strategy: task instructions only, no worked examples.
/// The wording below is the exact prompt used in the study. Changing it
/// will change the results, so keep it fixed when reproducing.
/// </summary>
public sealed class ZeroShotPrompt : IPromptStrategy
{
    public const string Instructions =
        "You are a security triage assistant. Given a security finding from an automated scanning tool, " +
        "decide whether it is a TRUE POSITIVE (a genuine, exploitable security issue) or a FALSE POSITIVE (not exploitable, " +
        "not applicable to this context, or not a real vulnerability). Respond with exactly one word: TP or FP. No explanation.";

    public string Name => "zero-shot";
    public string VerdictColumn => "LlmVerdict";
    public string DefaultInput => "labeled_with_groundtruth.csv";
    public VerdictRules Rules => VerdictRules.Strict;
    public int RateLimitAttempts => 0;

    public IReadOnlyList<ChatMessage> BuildMessages(Finding finding) => new[]
    {
        ChatMessage.System(Instructions),
        ChatMessage.User(FormatFinding(finding))
    };

    /// <summary>Renders one finding in the fixed layout the model sees. Shared by all strategies.</summary>
    public static string FormatFinding(Finding f) =>
        $"Tool: {f.Tool}\n" +
        $"Category: {f.Category}\n" +
        $"File/Package: {f.Location}\n" +
        $"Severity: {f.Severity}\n" +
        $"Description: {f.Description}\n" +
        "\n" +
        "Verdict (TP or FP):";
}
