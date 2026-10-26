using System.Text;

namespace HDBF.Tools.Validate;

/// <summary>Collects validation problems. Warnings do not change the exit code.</summary>
internal sealed class Report
{
    private readonly List<string> _errors = new();
    private readonly List<string> _warnings = new();

    public IReadOnlyList<string> Errors => _errors;

    public IReadOnlyList<string> Warnings => _warnings;

    public void Error(string category, string message) => _errors.Add($"[{category}] {message}");

    public void Warn(string category, string message) => _warnings.Add($"[{category}] {message}");

    public int Finish(TextWriter output)
    {
        output.WriteLine();

        if (_warnings.Count == 0)
        {
            output.WriteLine("WARNINGS: none");
        }
        else
        {
            output.WriteLine($"WARNINGS ({_warnings.Count}):");
            foreach (var warning in _warnings)
            {
                output.WriteLine("  " + warning);
            }
        }

        output.WriteLine();

        if (_errors.Count == 0)
        {
            output.WriteLine("RESULT: OK - data/ is valid.");
            return 0;
        }

        output.WriteLine($"ERRORS ({_errors.Count}):");
        foreach (var error in _errors)
        {
            output.WriteLine("  " + error);
        }

        output.WriteLine();
        output.WriteLine("RESULT: FAILED - fix the problems above.");
        return 1;
    }
}

/// <summary>Minimal RFC 4180 CSV reader, enough for the game's localization files.</summary>
internal static class Csv
{
    /// <summary>
    /// Parses a CSV document into rows of fields. Quoted fields may contain commas, doubled
    /// quotes and newlines; a UTF-8 BOM is stripped; CRLF and LF are both accepted.
    /// </summary>
    public static List<string[]> Parse(string text)
    {
        var rows = new List<string[]>();
        var row = new List<string>();
        var field = new StringBuilder();
        var inQuotes = false;
        var index = 0;

        if (text.Length > 0 && text[0] == '\uFEFF')
        {
            index = 1;
        }

        while (index < text.Length)
        {
            var ch = text[index];

            if (inQuotes)
            {
                if (ch == '"')
                {
                    if (index + 1 < text.Length && text[index + 1] == '"')
                    {
                        field.Append('"');
                        index += 2;
                        continue;
                    }

                    inQuotes = false;
                    index++;
                    continue;
                }

                field.Append(ch);
                index++;
                continue;
            }

            switch (ch)
            {
                case '"':
                    inQuotes = true;
                    index++;
                    break;

                case ',':
                    row.Add(field.ToString());
                    field.Clear();
                    index++;
                    break;

                case '\r':
                    index++;
                    break;

                case '\n':
                    row.Add(field.ToString());
                    field.Clear();
                    rows.Add(row.ToArray());
                    row.Clear();
                    index++;
                    break;

                default:
                    field.Append(ch);
                    index++;
                    break;
            }
        }

        if (field.Length > 0 || row.Count > 0)
        {
            row.Add(field.ToString());
            rows.Add(row.ToArray());
        }

        return rows;
    }
}
