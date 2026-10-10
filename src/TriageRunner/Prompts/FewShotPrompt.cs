using TriageRunner.Llm;
using TriageRunner.Models;

namespace TriageRunner.Prompts;

/// <summary>
/// Few-shot strategy: the same task, preceded by five worked examples
/// sent as earlier user / assistant turns.
///
/// The examples are hand-written and are NOT taken from the evaluation set,
/// so no evaluation answers leak into the prompt. Their wording, and the
/// instructions below, are exactly as used in the study. Keep them fixed
/// when reproducing the results.
/// </summary>
public sealed class FewShotPrompt : IPromptStrategy
{
    public const string Instructions =
        "You are a security triage assistant. Given a security finding from an automated scanning tool, " +
        "decide whether it is a TRUE POSITIVE (a genuine, exploitable security issue) or a FALSE POSITIVE (not exploitable, " +
        "not applicable to this context, or not a real vulnerability). Study the worked examples below, then classify the new finding. " +
        "Respond with exactly one word: TP or FP. No explanation.";

    /// <summary>Worked examples: two SAST, two dependency, one secrets.</summary>
    public static readonly IReadOnlyList<(Finding Example, string Answer)> Examples = new[]
    {
        (Finding.Create("semgrep", "SAST", "login.py", "ERROR",
            "Detected a raw SQL query built via string concatenation with unsanitized user input, enabling SQL injection."),
         VerdictParser.TruePositive),

        (Finding.Create("semgrep", "SAST", "config.py", "WARNING",
            "Use of subprocess with shell=True detected. The command string is a fixed, hardcoded value with no user-controllable input."),
         VerdictParser.FalsePositive),

        (Finding.Create("safety", "Dependency", "pyyaml==3.12", "CVE-2020-1747",
            "Arbitrary code execution via yaml.load on untrusted input, affects versions before 5.3.1."),
         VerdictParser.TruePositive),

        (Finding.Create("safety", "Dependency", "examplepkg==1.0.0", "CVE-9999-0001",
            "Vulnerability only affects Windows systems' handling of file paths; the application is deployed exclusively on Linux via Docker."),
         VerdictParser.FalsePositive),

        (Finding.Create("gitleaks", "Secrets", "config.py", "high",
            "Hardcoded AWS secret access key detected in source code."),
         VerdictParser.TruePositive),
    };

    public string Name => "few-shot";
    public string VerdictColumn => "LlmVerdict_FewShot";

    /// <summary>
    /// Runs on the zero-shot output so both verdicts end up side by side in one file.
    /// </summary>
    public string DefaultInput => "zero_shot_results.csv";

    public VerdictRules Rules => VerdictRules.Lenient;
    public int RateLimitAttempts => 3;

    public IReadOnlyList<ChatMessage> BuildMessages(Finding finding)
    {
        var messages = new List<ChatMessage> { ChatMessage.System(Instructions) };

        foreach (var (example, answer) in Examples)
        {
            messages.Add(ChatMessage.User(ZeroShotPrompt.FormatFinding(example)));
            messages.Add(ChatMessage.Assistant(answer));
        }

        messages.Add(ChatMessage.User(ZeroShotPrompt.FormatFinding(finding)));
        return messages;
    }
}
