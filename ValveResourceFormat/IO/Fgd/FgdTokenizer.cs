using System.Text;

namespace ValveResourceFormat.IO.Fgd;

/// <summary>
/// Splits the text of an FGD file into tokens the way Hammer does: words, quoted strings, and the
/// characters that stand for themselves.
/// </summary>
internal sealed class FgdTokenizer
{
    /// <summary>
    /// A token, as a span of the text it was read from. A string keeps its quotes until it is unquoted.
    /// </summary>
    public readonly record struct Token(string Source, int Start, int End)
    {
        public ReadOnlySpan<char> Span => Source.AsSpan(Start, End - Start);

        public string Text => Source[Start..End];

        /// <summary>Whether the token is a string in double quotes.</summary>
        public bool IsString => End - Start >= 2 && Source[Start] == '"' && Source[End - 1] == '"';

        /// <summary>Whether the token is an optional sign followed by digits only.</summary>
        public bool IsInteger
        {
            get
            {
                var span = Span;

                if (span.IsEmpty)
                {
                    return false;
                }

                if (span[0] is '+' or '-')
                {
                    span = span[1..];
                }

                foreach (var character in span)
                {
                    if (!char.IsAsciiDigit(character))
                    {
                        return false;
                    }
                }

                return true;
            }
        }

        /// <summary>Whether the token is made of nothing but what a decimal number is written with.</summary>
        public bool IsFloat
        {
            get
            {
                var span = Span;

                if (span.IsEmpty)
                {
                    return false;
                }

                foreach (var character in span)
                {
                    if (!char.IsAsciiDigit(character) && character is not ('+' or '-' or '.' or 'E' or 'e'))
                    {
                        return false;
                    }
                }

                return true;
            }
        }

        /// <summary>Whether the token is a name: letters, digits after the first character, and <c>_ . :</c>.</summary>
        public bool IsIdentifier
        {
            get
            {
                var span = Span;

                if (span.IsEmpty)
                {
                    return false;
                }

                for (var i = 0; i < span.Length; i++)
                {
                    var character = span[i];

                    if (!char.IsAsciiLetter(character) && character is not ('_' or '.' or ':') && (i == 0 || !char.IsAsciiDigit(character)))
                    {
                        return false;
                    }
                }

                return true;
            }
        }

        public bool Is(string text) => Span.Equals(text, StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// The text a string stands for: without its quotes, and with what a backslash escapes.
        /// </summary>
        public string Unquote()
        {
            var span = Span;

            if (span.Length >= 6 && span.StartsWith("\"\"\"") && span.EndsWith("\"\"\""))
            {
                var block = new StringBuilder(span[3..^3].ToString()).Replace("\r\n", "\n");

                // The line breaks next to the quotes are not part of the text
                if (block.Length > 0)
                {
                    block.Remove(0, 1);
                }

                if (block.Length > 0)
                {
                    block.Remove(block.Length - 1, 1);
                }

                return block.Replace("\\\"\"\"", "\"\"\"").ToString();
            }

            var text = new StringBuilder(span.Length);
            var escaped = false;

            for (var i = 0; i < span.Length; i++)
            {
                var character = span[i];

                if ((i == 0 || i == span.Length - 1) && character is '"' or '\'')
                {
                    continue;
                }

                if (!escaped)
                {
                    if (character == '\\')
                    {
                        escaped = true;
                        continue;
                    }
                }
                else
                {
                    character = character switch
                    {
                        'n' => '\n',
                        't' => '\t',
                        _ => character,
                    };

                    escaped = false;
                }

                text.Append(character);
            }

            return text.ToString();
        }
    }

    private readonly string text;
    private readonly List<Token> queue = [];
    private string breakCharacters;
    private int position;

    /// <summary>Gets the name of the file the text is from.</summary>
    public string FileName { get; }

    public FgdTokenizer(string text, string fileName, string breakCharacters)
    {
        this.text = text;
        this.breakCharacters = breakCharacters;
        FileName = fileName;
    }

    /// <summary>Gets whether there is another token.</summary>
    public bool HasMore => Fill(1);

    /// <summary>Gets the line the next token is on, counted from one.</summary>
    public int Line
    {
        get
        {
            var end = Fill(1) ? queue[0].Start : position;
            var line = 1;

            for (var i = 0; i < end && i < text.Length; i++)
            {
                if (text[i] == '\n')
                {
                    line++;
                }
            }

            return line;
        }
    }

    /// <summary>Looks at a token ahead without reading it.</summary>
    public bool TryPeek(int index, out Token token)
    {
        if (Fill(index + 1))
        {
            token = queue[index];
            return true;
        }

        token = default;
        return false;
    }

    public bool PeekIs(int index, string value) => TryPeek(index, out var token) && token.Is(value);

    /// <summary>Reads the next token, whatever it is.</summary>
    public bool TryRead(out Token token)
    {
        if (!Fill(1))
        {
            token = default;
            return false;
        }

        token = queue[0];
        queue.RemoveAt(0);
        return true;
    }

    /// <summary>Reads the next token if it is a name, and leaves it where it is otherwise.</summary>
    public bool TryReadIdentifier(out Token token)
    {
        if (TryPeek(0, out token) && token.IsIdentifier)
        {
            queue.RemoveAt(0);
            return true;
        }

        token = default;
        return false;
    }

    /// <summary>Reads the next token if it is the given text, without regard to case.</summary>
    public bool Accept(string value)
    {
        if (!PeekIs(0, value))
        {
            return false;
        }

        queue.RemoveAt(0);
        return true;
    }

    /// <summary>
    /// Reads tokens up to and including the first that is the given text, and gives the text that was
    /// passed over, from the start of the first token to the start of the one that was looked for.
    /// </summary>
    public bool SkipPast(string value, out string skipped)
    {
        skipped = string.Empty;

        if (!Fill(1))
        {
            return false;
        }

        var start = queue[0].Start;

        while (!PeekIs(0, value))
        {
            if (!TryRead(out _))
            {
                return false;
            }
        }

        skipped = text[start..queue[0].Start];
        queue.RemoveAt(0);
        return true;
    }

    /// <summary>Reads tokens up to the first that is the given text, which is left to be read next.</summary>
    public bool SkipTo(string value)
    {
        while (!PeekIs(0, value))
        {
            if (!TryRead(out _))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Reads a block that starts with the opening token next and runs to the closing token that matches
    /// it, and gives its text with both of them.
    /// </summary>
    public bool TryReadBlock(string open, string close, out string block)
    {
        block = string.Empty;

        if (!PeekIs(0, open))
        {
            return false;
        }

        var start = queue[0].Start;
        var depth = 0;

        while (TryRead(out var token))
        {
            if (token.Is(open))
            {
                depth++;
            }
            else if (token.Is(close) && --depth == 0)
            {
                block = text[start..token.End];
                return true;
            }
        }

        return false;
    }

    private bool Fill(int count)
    {
        while (queue.Count < count)
        {
            if (!TryLex(out var token))
            {
                return false;
            }

            queue.Add(token);
        }

        return true;
    }

    private bool IsBreak(char character) => character is <= ' ' or >= (char)0x80 || breakCharacters.Contains(character, StringComparison.Ordinal);

    private bool TryLex(out Token token)
    {
        token = default;

        do
        {
            while (position < text.Length && IsSpace(text[position]))
            {
                position++;
            }

            if (position >= text.Length)
            {
                return false;
            }
        }
        while (SkipBlockComment() || SkipLineComment());

        var start = position;
        var first = text[position++];

        if (first == '\0')
        {
            return false;
        }

        if (first == '"' && TryLexBlockString())
        {
            token = new Token(text, start, position);
            return true;
        }

        if (first is '"' or '\'')
        {
            LexString(first);
        }
        else if (!IsBreak(first))
        {
            var previous = '\0';

            while (position < text.Length)
            {
                var character = text[position];

                if (IsBreak(character) || (character is '"' or '\'' && previous != '\\'))
                {
                    break;
                }

                previous = character;
                position++;
            }
        }

        token = new Token(text, start, position);
        return true;
    }

    // A string over several lines: three quotes and a line break, up to a line break and three quotes
    private bool TryLexBlockString()
    {
        var rest = text.AsSpan(position);
        int opening;

        if (rest.StartsWith("\"\"\n"))
        {
            opening = 3;
        }
        else if (rest.StartsWith("\"\"\r\n"))
        {
            opening = 4;
        }
        else
        {
            return false;
        }

        position += opening;

        if (text.AsSpan(position).StartsWith("\"\"\""))
        {
            position += 3;
            return true;
        }

        var escaped = false;

        while (position < text.Length)
        {
            if (!escaped)
            {
                rest = text.AsSpan(position);

                if (rest.StartsWith("\n\"\"\""))
                {
                    position += 4;
                    return true;
                }

                if (rest.StartsWith("\r\n\"\"\""))
                {
                    position += 5;
                    return true;
                }
            }

            escaped = text[position++] == '\\';
        }

        return true;
    }

    // Up to the quote the string opened with. A backslash keeps the character after it from ending the string.
    private void LexString(char quote)
    {
        var previous = '\0';
        var escaped = false;

        while (position < text.Length)
        {
            var character = text[position++];

            if (previous != '\\' || escaped)
            {
                escaped = false;

                if (character == quote)
                {
                    return;
                }
            }
            else
            {
                escaped = true;
            }

            if (character == '\0')
            {
                return;
            }

            previous = character;
        }
    }

    private bool SkipBlockComment()
    {
        if (position + 4 > text.Length || text[position] != '/' || text[position + 1] != '*')
        {
            return false;
        }

        var close = text.IndexOf("*/", position + 2, StringComparison.Ordinal);

        position = close < 0 ? text.Length : close + 2;
        return true;
    }

    private bool SkipLineComment()
    {
        if (position + 2 > text.Length || text[position] != '/' || text[position + 1] != '/')
        {
            return false;
        }

        position += 2;

        while (position < text.Length)
        {
            if (text[position] == '\n')
            {
                var previous = text[position - 1];

                // A backslash at the end of the line carries the comment onto the next one
                if (previous != '\\' && (previous != '\r' || text[position - 2] != '\\'))
                {
                    position++;
                    break;
                }
            }

            position++;
        }

        return true;
    }

    private static bool IsSpace(char character) => character is ' ' or '\t' or '\n' or '\v' or '\f' or '\r';
}
