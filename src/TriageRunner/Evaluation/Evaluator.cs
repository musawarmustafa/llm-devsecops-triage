using System.Globalization;
using System.Text;
using TriageRunner.Llm;
using TriageRunner.Models;

namespace TriageRunner.Evaluation;

/// <summary>One row of the metrics report: a run, a category (or OVERALL) and its counts.</summary>
public sealed record MetricsRow(string Run, string Category, ConfusionCounts Counts);

/// <summary>
/// Scores LLM verdicts against the hand-labelled ground truth.
/// Produces per-category metrics plus an OVERALL row pooled across all findings
/// (micro-averaged: counts are summed first, then the metrics are computed).
/// </summary>
public static class Evaluator
{
    public const string GroundTruthColumn = "GroundTruth";
    public const string OverallLabel = "OVERALL";

    /// <summary>Verdict columns the evaluator knows about, with a readable run name.</summary>
    public static readonly IReadOnlyList<(string Column, string Run)> KnownRuns = new[]
    {
        ("LlmVerdict", "ZeroShot"),
        ("LlmVerdict_FewShot", "FewShot"),
    };

    public static IReadOnlyList<MetricsRow> Score(IReadOnlyList<Finding> findings, string verdictColumn, string runName,
                                                  out int unresolvedVerdicts, out int unlabelledRows)
    {
        // Keep categories in the order they first appear in the data.
        var byCategory = new Dictionary<string, ConfusionCounts>();
        var categoryOrder = new List<string>();
        var overall = new ConfusionCounts();
        unresolvedVerdicts = 0;
        unlabelledRows = 0;

        foreach (var finding in findings)
        {
            var truth = Normalise(finding.Get(GroundTruthColumn));
            if (truth != VerdictParser.TruePositive && truth != VerdictParser.FalsePositive)
            {
                unlabelledRows++;
                continue;
            }

            var verdict = Normalise(finding.Get(verdictColumn));
            if (verdict != VerdictParser.TruePositive && verdict != VerdictParser.FalsePositive)
            {
                // ERROR / UNCLEAR replies are not an escalation, so they count as a negative prediction.
                unresolvedVerdicts++;
            }

            bool predicted = verdict == VerdictParser.TruePositive;
            bool actual = truth == VerdictParser.TruePositive;

            if (!byCategory.TryGetValue(finding.Category, out var counts))
            {
                counts = new ConfusionCounts();
                byCategory[finding.Category] = counts;
                categoryOrder.Add(finding.Category);
            }

            counts.Add(predicted, actual);
            overall.Add(predicted, actual);
        }

        var rows = categoryOrder.Select(c => new MetricsRow(runName, c, byCategory[c])).ToList();
        rows.Add(new MetricsRow(runName, OverallLabel, overall));
        return rows;
    }

    public static string FormatTable(IReadOnlyList<MetricsRow> rows)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"{"Category",-12}{"N",5}{"TP",5}{"FN",5}{"FP",5}{"TN",5}{"Precision",11}{"Recall",9}{"F1",9}{"Accuracy",10}");
        foreach (var r in rows)
        {
            var c = r.Counts;
            sb.AppendLine($"{r.Category,-12}{c.Total,5}{c.TruePos,5}{c.FalseNeg,5}{c.FalsePos,5}{c.TrueNeg,5}" +
                          $"{Fmt(c.Precision),11}{Fmt(c.Recall),9}{Fmt(c.F1),9}{Fmt(c.Accuracy),10}");
        }
        return sb.ToString();
    }

    public static void SaveCsv(string path, IEnumerable<MetricsRow> rows)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var sb = new StringBuilder();
        sb.AppendLine("RunType,Category,N,TP,FN,FP,TN,Precision,Recall,F1,Accuracy");
        foreach (var r in rows)
        {
            var c = r.Counts;
            sb.AppendLine(string.Join(",", r.Run, r.Category, c.Total, c.TruePos, c.FalseNeg, c.FalsePos, c.TrueNeg,
                                      Fmt(c.Precision), Fmt(c.Recall), Fmt(c.F1), Fmt(c.Accuracy)));
        }
        File.WriteAllText(path, sb.ToString());
    }

    private static string Normalise(string value) => value.Trim().ToUpperInvariant();

    private static string Fmt(double value) => value.ToString("0.0000", CultureInfo.InvariantCulture);
}
