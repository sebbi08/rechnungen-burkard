using System.Text;

namespace Burkard.Invoicing.Api.Services.Import;

/// <summary>Minimal RFC 4180 reader and writer: comma separated (a reader can
/// be given another delimiter), double quotes
/// around fields that need them, "" for an embedded quote. Addresses span
/// several lines, so quoted line breaks must survive a round trip.</summary>
public static class Csv
{
    public static string Write(
        IReadOnlyList<string> header,
        IEnumerable<IReadOnlyList<string>> rows
    )
    {
        var builder = new StringBuilder();
        AppendRow(builder, header);
        foreach (var row in rows)
        {
            AppendRow(builder, row);
        }

        return builder.ToString();
    }

    /// <summary>Splits the text into rows of fields. A trailing newline does not
    /// produce an extra row, and a row that is completely empty is dropped, so a
    /// file with nothing but a header reads as "no data". Each row keeps its
    /// <see cref="CsvRow.Number"/> all the same, counted the way a spreadsheet
    /// counts them -- a dropped row included, a quoted line break not -- so a
    /// problem names the row the user sees.</summary>
    public static List<CsvRow> Parse(string text, char delimiter = ',')
    {
        var rows = new List<CsvRow>();
        var row = new List<string>();
        var number = 0;
        var field = new StringBuilder();
        var inQuotes = false;
        var fieldStarted = false;

        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (inQuotes)
            {
                if (c != '"')
                {
                    field.Append(c);
                }
                else if (i + 1 < text.Length && text[i + 1] == '"')
                {
                    field.Append('"');
                    i++;
                }
                else
                {
                    inQuotes = false;
                }

                continue;
            }

            if (c == delimiter)
            {
                row.Add(field.ToString());
                field.Clear();
                fieldStarted = false;
                continue;
            }

            switch (c)
            {
                case '"':
                    inQuotes = true;
                    fieldStarted = true;
                    break;
                case '\r':
                    break;
                case '\n':
                    row.Add(field.ToString());
                    field.Clear();
                    fieldStarted = false;
                    AddRow(rows, row, ++number);
                    row = new List<string>();
                    break;
                default:
                    field.Append(c);
                    fieldStarted = true;
                    break;
            }
        }

        if (fieldStarted || field.Length > 0 || row.Count > 0)
        {
            row.Add(field.ToString());
            AddRow(rows, row, ++number);
        }

        return rows;
    }

    private static void AddRow(List<CsvRow> rows, List<string> row, int number)
    {
        if (row.Any(f => !string.IsNullOrWhiteSpace(f)))
        {
            rows.Add(new CsvRow(number, row));
        }
    }

    private static void AppendRow(StringBuilder builder, IReadOnlyList<string> row)
    {
        for (var i = 0; i < row.Count; i++)
        {
            if (i > 0)
                builder.Append(',');
            builder.Append(Escape(row[i]));
        }

        builder.Append("\r\n");
    }

    private static string Escape(string value)
    {
        if (value.IndexOfAny(new[] { ',', '"', '\n', '\r' }) < 0)
        {
            return value;
        }

        return $"\"{value.Replace("\"", "\"\"")}\"";
    }
}

/// <summary>One row's fields, with the row's number in its file (the header is
/// row 1).</summary>
public sealed class CsvRow : List<string>
{
    public CsvRow(int number, IEnumerable<string> fields)
        : base(fields) => Number = number;

    public int Number { get; }
}
