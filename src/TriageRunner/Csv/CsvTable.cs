using System.Text;
using TriageRunner.Models;

namespace TriageRunner.Csv;

/// <summary>
/// Minimal RFC 4180 style CSV support. Handles quoted fields that contain
/// commas, escaped quotes and line breaks (scanner descriptions often do).
/// </summary>
public static class CsvTable
{
    /// <summary>Base column order for the results file. A strategy's verdict column is added if missing.</summary>
    public static readonly string[] BaseColumns =
    {
        "Tool", "Category", "FilePath", "Severity", "Description",
        "LlmVerdict", "GroundTruth", "Reason"
    };

    public static List<Finding> LoadFindings(string path)
    {
        var rows = Parse(File.ReadAllText(path));
        if (rows.Count == 0)
        {
            return new List<Finding>();
        }

        var header = rows[0];
        return rows
            .Skip(1)
            .Select(cells => new Finding(
                header.Select((name, idx) => (name, value: idx < cells.Count ? cells[idx] : string.Empty))
                      .ToDictionary(p => p.name, p => p.value)))
            .ToList();
    }

    /// <summary>Base columns plus any extra columns not already in the list, in order.</summary>
    public static IReadOnlyList<string> ColumnsWith(params string[] extra) =>
        BaseColumns.Concat(extra.Where(c => !BaseColumns.Contains(c))).ToList();

    public static void SaveFindings(string path, IEnumerable<Finding> findings, IReadOnlyList<string> columns)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var sb = new StringBuilder();
        sb.AppendLine(JoinRow(columns));

        foreach (var finding in findings)
        {
            var cells = columns.Select(col =>
                finding.Columns.TryGetValue(col, out var v) ? v : string.Empty);
            sb.AppendLine(JoinRow(cells));
        }

        File.WriteAllText(path, sb.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
    }

    private enum State { Normal, InQuotes, QuoteInQuotes }

    private static List<List<string>> Parse(string text)
    {
        var rows = new List<List<string>>();
        var row = new List<string>();
        var cell = new StringBuilder();
        var state = State.Normal;

        void EndCell()
        {
            row.Add(cell.ToString());
            cell.Clear();
        }

        void EndRow()
        {
            EndCell();
            rows.Add(row);
            row = new List<string>();
        }

        foreach (var ch in text)
        {
            switch (state)
            {
                case State.Normal:
                    if (ch == '"') state = State.InQuotes;
                    else if (ch == ',') EndCell();
                    else if (ch == '\n') EndRow();
                    else if (ch != '\r') cell.Append(ch);
                    break;

                case State.InQuotes:
                    if (ch == '"') state = State.QuoteInQuotes;
                    else cell.Append(ch);
                    break;

                case State.QuoteInQuotes:
                    if (ch == '"')
                    {
                        // Doubled quote inside a quoted field means a literal quote.
                        cell.Append('"');
                        state = State.InQuotes;
                    }
                    else
                    {
                        state = State.Normal;
                        if (ch == ',') EndCell();
                        else if (ch == '\n') EndRow();
                        else if (ch != '\r') cell.Append(ch);
                    }
                    break;
            }
        }

        if (cell.Length > 0 || row.Count > 0)
        {
            EndRow();
        }

        return rows;
    }

    private static string JoinRow(IEnumerable<string> cells) =>
        string.Join(",", cells.Select(Quote));

    private static string Quote(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        bool needsQuotes = value.IndexOfAny(new[] { ',', '"', '\n' }) >= 0;
        return needsQuotes ? $"\"{value.Replace("\"", "\"\"")}\"" : value;
    }
}
