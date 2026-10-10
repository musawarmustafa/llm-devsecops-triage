using TriageRunner.Llm;
using TriageRunner.Models;

namespace TriageRunner.Prompts;

/// <summary>
/// One prompting strategy (zero-shot, few-shot). It decides what the model sees,
/// how the reply is read, and which column the verdict is written to.
/// </summary>
public interface IPromptStrategy
{
    /// <summary>Name used on the command line, e.g. "zero-shot".</summary>
    string Name { get; }

    /// <summary>Results column that receives this strategy's verdict.</summary>
    string VerdictColumn { get; }

    /// <summary>Default input file when --input is not given.</summary>
    string DefaultInput { get; }

    /// <summary>Settings for turning a reply into a verdict.</summary>
    VerdictRules Rules { get; }

    /// <summary>
    /// Attempts allowed when the API returns HTTP 429 (rate limited).
    /// 0 means a 429 is treated like any other API error.
    /// </summary>
    int RateLimitAttempts { get; }

    IReadOnlyList<ChatMessage> BuildMessages(Finding finding);
}
