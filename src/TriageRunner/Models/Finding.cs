namespace TriageRunner.Models;

/// <summary>
/// One scanner finding loaded from the labelled dataset.
/// Known columns are exposed as properties. Every column, known or not,
/// is kept in <see cref="Columns"/> so nothing is lost when results are written back.
/// </summary>
public sealed class Finding
{
    public Finding(IReadOnlyDictionary<string, string> columns)
    {
        Columns = new Dictionary<string, string>(columns, StringComparer.Ordinal);
    }

    /// <summary>Convenience factory for hand-written findings such as prompt examples.</summary>
    public static Finding Create(string tool, string category, string location, string severity, string description) =>
        new(new Dictionary<string, string>
        {
            ["Tool"] = tool,
            ["Category"] = category,
            ["FilePath"] = location,
            ["Severity"] = severity,
            ["Description"] = description
        });

    public Dictionary<string, string> Columns { get; }

    public string Tool => Get("Tool");
    public string Category => Get("Category");
    public string Location => Get("FilePath");
    public string Severity => Get("Severity");
    public string Description => Get("Description");

    public string Get(string column) =>
        Columns.TryGetValue(column, out var value) ? value : string.Empty;

    public void Set(string column, string value) => Columns[column] = value;
}
