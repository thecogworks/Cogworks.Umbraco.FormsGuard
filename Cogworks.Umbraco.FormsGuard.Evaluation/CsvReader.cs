using System.Text;

namespace Cogworks.Umbraco.FormsGuard.Evaluation;

/// <summary>One labelled corpus row.</summary>
public sealed record CorpusRow(int Line, string Label, string Category, string Name, string Email, string Message)
{
    public bool IsSpam => string.Equals(Label, "spam", StringComparison.OrdinalIgnoreCase);
}

/// <summary>A small RFC 4180-style reader: comma separated, double-quoted fields, <c>""</c> for an embedded quote.</summary>
public static class CsvReader
{
    private static readonly string[] Columns = { "label", "category", "name", "email", "message" };

    /// <summary>Parses the corpus; columns are mapped by header name. Throws <see cref="FormatException"/> on bad input.</summary>
    public static IReadOnlyList<CorpusRow> ReadCorpus(string text)
    {
        var records = Parse(text);
        if (records.Count == 0)
        {
            throw new FormatException("The CSV is empty.");
        }

        var header = records[0].Fields.Select(h => h.Trim()).ToList();
        var index = new Dictionary<string, int>();
        foreach (var column in Columns)
        {
            var i = header.FindIndex(h => string.Equals(h, column, StringComparison.OrdinalIgnoreCase));
            if (i < 0)
            {
                throw new FormatException($"The CSV header is missing the '{column}' column.");
            }

            index[column] = i;
        }

        var rows = new List<CorpusRow>();
        foreach (var record in records.Skip(1))
        {
            if (record.Fields.Count == 1 && record.Fields[0].Length == 0)
            {
                continue; // Blank line.
            }

            string Get(string column)
            {
                var i = index[column];
                if (i >= record.Fields.Count)
                {
                    throw new FormatException($"Line {record.Line} is missing the '{column}' column.");
                }

                return record.Fields[i];
            }

            var label = Get("label").Trim();
            if (!string.Equals(label, "spam", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(label, "genuine", StringComparison.OrdinalIgnoreCase))
            {
                throw new FormatException($"Line {record.Line} has label '{label}'; expected 'spam' or 'genuine'.");
            }

            rows.Add(new CorpusRow(record.Line, label, Get("category"), Get("name"), Get("email"), Get("message")));
        }

        return rows;
    }

    /// <summary>Splits text into records of fields. Quoted fields may contain commas, quotes and newlines.</summary>
    public static IReadOnlyList<(int Line, IReadOnlyList<string> Fields)> Parse(string text)
    {
        var records = new List<(int, IReadOnlyList<string>)>();
        var fields = new List<string>();
        var field = new StringBuilder();
        var line = 1;
        var recordLine = 1;
        var inQuotes = false;
        var i = 0;

        while (i < text.Length)
        {
            var c = text[i];
            if (inQuotes)
            {
                if (c == '"')
                {
                    if (i + 1 < text.Length && text[i + 1] == '"')
                    {
                        field.Append('"');
                        i += 2;
                        continue;
                    }

                    inQuotes = false;
                }
                else
                {
                    if (c == '\n')
                    {
                        line++;
                    }

                    field.Append(c);
                }

                i++;
                continue;
            }

            switch (c)
            {
                case '"':
                    inQuotes = true;
                    break;
                case ',':
                    fields.Add(field.ToString());
                    field.Clear();
                    break;
                case '\r':
                    break;
                case '\n':
                    fields.Add(field.ToString());
                    field.Clear();
                    records.Add((recordLine, fields));
                    fields = new List<string>();
                    line++;
                    recordLine = line;
                    break;
                default:
                    field.Append(c);
                    break;
            }

            i++;
        }

        if (inQuotes)
        {
            throw new FormatException($"Unterminated quoted field starting on line {recordLine}.");
        }

        if (field.Length > 0 || fields.Count > 0)
        {
            fields.Add(field.ToString());
            records.Add((recordLine, fields));
        }

        return records;
    }
}
