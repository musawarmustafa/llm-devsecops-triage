using TriageRunner.Csv;
using TriageRunner.Llm;
using TriageRunner.Prompts;

namespace TriageRunner;

/// <summary>
/// Runs LLM-based triage over a labelled set of scanner findings
/// and writes one verdict (TP / FP) per finding to a results CSV.
///
/// Typical run:
///   1. dotnet run -- --mode zero-shot     (labeled_with_groundtruth.csv -> zero_shot_results.csv)
///   2. dotnet run -- --mode few-shot      (zero_shot_results.csv        -> few_shot_results.csv)
///
/// The second step adds a few-shot verdict column next to the zero-shot one.
/// Requires the GROQ_API_KEY environment variable.
/// </summary>
internal static class Program
{
    private static readonly IPromptStrategy[] Strategies = { new ZeroShotPrompt(), new FewShotPrompt() };

    private static async Task<int> Main(string[] args)
    {
        var options = RunOptions.Parse(args);
        if (options is null)
        {
            RunOptions.PrintUsage(Strategies);
            return 2;
        }

        var strategy = Strategies.FirstOrDefault(s => s.Name == options.Mode);
        if (strategy is null)
        {
            Console.Error.WriteLine($"Unknown mode '{options.Mode}'. Supported: {string.Join(", ", Strategies.Select(s => s.Name))}.");
            return 2;
        }

        var apiKey = Environment.GetEnvironmentVariable("GROQ_API_KEY");
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            Console.Error.WriteLine("GROQ_API_KEY is not set. Add it as an environment variable and try again.");
            return 1;
        }

        var inputPath = options.InputPath ?? strategy.DefaultInput;
        var outputPath = options.OutputPath ?? $"{strategy.Name.Replace('-', '_')}_results.csv";

        if (!File.Exists(inputPath))
        {
            Console.Error.WriteLine($"Input file not found: {inputPath}");
            return 1;
        }

        var findings = CsvTable.LoadFindings(inputPath);
        Console.WriteLine($"Mode: {strategy.Name} | Findings: {findings.Count} | Input: {inputPath}");

        using var llm = new GroqChatClient(apiKey, options.Endpoint, rateLimitWait: TimeSpan.FromMilliseconds(options.RetryWaitMs));

        // When running few-shot on zero-shot output, show both verdicts side by side.
        const string baselineColumn = "LlmVerdict";
        bool showBaseline = strategy.VerdictColumn != baselineColumn;

        for (int n = 0; n < findings.Count; n++)
        {
            var finding = findings[n];
            string verdict;

            try
            {
                var reply = await llm.CompleteAsync(strategy.BuildMessages(finding), strategy.RateLimitAttempts);
                verdict = VerdictParser.Parse(reply, strategy.Rules);
            }
            catch (RateLimitedException)
            {
                verdict = VerdictParser.RateLimited;
            }
            catch (Exception ex)
            {
                // Any other API or parsing failure is recorded as ERROR so the run can continue.
                Console.Error.WriteLine($"  Request failed for finding {n + 1}: {ex.Message}");
                verdict = VerdictParser.Error;
            }

            finding.Set(strategy.VerdictColumn, verdict);

            var line = $"{n + 1,4}/{findings.Count}  {finding.Category,-10} {finding.Location,-30} {verdict}";
            if (showBaseline)
            {
                line += $"   (zero-shot: {finding.Get(baselineColumn)})";
            }
            Console.WriteLine(line);

            // Pause between calls to respect the free-tier rate limit.
            if (n < findings.Count - 1)
            {
                await Task.Delay(options.DelayMs);
            }
        }

        CsvTable.SaveFindings(outputPath, findings, CsvTable.ColumnsWith(strategy.VerdictColumn));
        Console.WriteLine($"Saved results to {outputPath}");
        return 0;
    }
}

internal sealed record RunOptions(
    string Mode, string? InputPath, string? OutputPath, int DelayMs, int RetryWaitMs, string? Endpoint)
{
    public static RunOptions? Parse(string[] args)
    {
        string mode = "zero-shot";
        string? input = null;
        string? output = null;
        int delayMs = 2100;
        int retryWaitMs = 15000;
        string? endpoint = null;

        for (int i = 0; i < args.Length; i++)
        {
            string NextValue() => i + 1 < args.Length ? args[++i] : throw new ArgumentException($"Missing value for {args[i]}");

            try
            {
                switch (args[i])
                {
                    case "--mode": mode = NextValue().ToLowerInvariant(); break;
                    case "--input": input = NextValue(); break;
                    case "--output": output = NextValue(); break;
                    case "--delay-ms": delayMs = int.Parse(NextValue()); break;
                    case "--retry-wait-ms": retryWaitMs = int.Parse(NextValue()); break;
                    case "--endpoint": endpoint = NextValue(); break;
                    case "-h":
                    case "--help": return null;
                    default:
                        Console.Error.WriteLine($"Unknown option: {args[i]}");
                        return null;
                }
            }
            catch (Exception ex) when (ex is ArgumentException or FormatException)
            {
                Console.Error.WriteLine(ex.Message);
                return null;
            }
        }

        return new RunOptions(mode, input, output, delayMs, retryWaitMs, endpoint);
    }

    public static void PrintUsage(IEnumerable<IPromptStrategy> strategies)
    {
        Console.WriteLine($"""
            Usage: triage-runner [options]

              --mode <name>           {string.Join(" | ", strategies.Select(s => s.Name))}. Default: zero-shot
              --input <path>          Findings CSV. Default depends on mode:
            {string.Join("\n", strategies.Select(s => $"                          {s.Name}: {s.DefaultInput}"))}
              --output <path>         Results CSV. Default: <mode>_results.csv
              --delay-ms <ms>         Pause between API calls. Default: 2100
              --retry-wait-ms <ms>    Wait after a rate-limit reply (few-shot only). Default: 15000
              --endpoint <url>        Override the chat completions URL (testing only)

            Environment:
              GROQ_API_KEY            Required. Your Groq API key.
            """);
    }
}
