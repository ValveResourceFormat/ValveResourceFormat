using System;
using System.Collections.Generic;
using System.Text;

namespace ValveResourceFormat.SourceGenerators.Localization;

/// <summary>A row of a localization file.</summary>
internal sealed class CsvRow(int line, string key, string text)
{
    /// <summary>Zero-based line the row starts on.</summary>
    public int Line { get; } = line;

    public string Key { get; } = key;

    public string Text { get; } = text;
}

/// <summary>Reads headerless two column <c>key,text</c> files, with quoted fields as translation platforms export them.</summary>
internal static class CsvReader
{
    private static readonly char[] FieldEnd = [',', '\r', '\n'];

    /// <summary>Reads all rows, skipping blank lines. Returns false with the line of the first malformed row.</summary>
    public static bool TryRead(string content, List<CsvRow> rows, out string error, out int errorLine)
    {
        error = string.Empty;
        errorLine = 0;

        var position = content.Length > 0 && content[0] == '﻿' ? 1 : 0;
        var line = 0;
        var fields = new List<string>(2);

        while (position < content.Length)
        {
            var rowLine = line;
            fields.Clear();

            while (true)
            {
                if (position < content.Length && content[position] == '"')
                {
                    if (!TryReadQuotedField(content, ref position, ref line, out var quoted))
                    {
                        error = "Unterminated quoted field";
                        errorLine = rowLine;
                        return false;
                    }

                    fields.Add(quoted);

                    if (position < content.Length && Array.IndexOf(FieldEnd, content[position]) < 0)
                    {
                        error = "Unexpected text after a quoted field";
                        errorLine = rowLine;
                        return false;
                    }
                }
                else
                {
                    var end = content.IndexOfAny(FieldEnd, position);
                    end = end < 0 ? content.Length : end;

                    fields.Add(content.Substring(position, end - position));
                    position = end;
                }

                if (position < content.Length && content[position] == ',')
                {
                    position++;
                    continue;
                }

                break;
            }

            if (position < content.Length)
            {
                if (content[position] == '\r')
                {
                    position++;
                }

                if (position < content.Length && content[position] == '\n')
                {
                    position++;
                }

                line++;
            }

            if (fields.Count == 1 && fields[0].Length == 0)
            {
                continue;
            }

            if (fields.Count != 2)
            {
                error = "A row needs exactly two fields, key and text";
                errorLine = rowLine;
                return false;
            }

            rows.Add(new CsvRow(rowLine, fields[0], fields[1]));
        }

        return true;
    }

    private static bool TryReadQuotedField(string content, ref int position, ref int line, out string field)
    {
        var builder = new StringBuilder();
        position++;

        while (position < content.Length)
        {
            var character = content[position];

            if (character == '"')
            {
                if (position + 1 < content.Length && content[position + 1] == '"')
                {
                    builder.Append('"');
                    position += 2;
                    continue;
                }

                position++;
                field = builder.ToString();
                return true;
            }

            // Keep newlines as LF whatever line endings the checkout uses
            if (character == '\r' && position + 1 < content.Length && content[position + 1] == '\n')
            {
                position++;
                continue;
            }

            if (character == '\n')
            {
                line++;
            }

            builder.Append(character);
            position++;
        }

        field = string.Empty;
        return false;
    }
}
