using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using GUI.Utils;
using Microsoft.CodeAnalysis.CSharp;

namespace ValveResourceFormat.SourceGenerators.Localization;

internal enum MessagePartKind
{
    /// <summary>Literal text.</summary>
    Text,
    /// <summary>A <c>{name}</c> argument.</summary>
    Argument,
    /// <summary>The <c>#</c> of a plural branch.</summary>
    Count,
    /// <summary>A <c>{name, plural, ...}</c> block.</summary>
    Plural,
}

/// <summary>A piece of a parsed message.</summary>
internal sealed class MessagePart
{
    public MessagePartKind Kind { get; }

    /// <summary>The literal text of a <see cref="MessagePartKind.Text"/> part.</summary>
    public string Text { get; } = string.Empty;

    /// <summary>Index into <see cref="ParsedMessage.Arguments"/>, for argument and plural parts.</summary>
    public int ArgumentIndex { get; }

    public PluralBranch[] Branches { get; } = [];

    private MessagePart(MessagePartKind kind, string text, int argumentIndex, PluralBranch[] branches)
    {
        Kind = kind;
        Text = text;
        ArgumentIndex = argumentIndex;
        Branches = branches;
    }

    public static MessagePart CreateText(string text) => new(MessagePartKind.Text, text, -1, []);

    public static MessagePart CreateArgument(int argumentIndex) => new(MessagePartKind.Argument, string.Empty, argumentIndex, []);

    public static MessagePart CreateCount() => new(MessagePartKind.Count, string.Empty, -1, []);

    public static MessagePart CreatePlural(int argumentIndex, PluralBranch[] branches) => new(MessagePartKind.Plural, string.Empty, argumentIndex, branches);
}

/// <summary>A branch of a plural block, selected by an exact <c>=N</c> value or a plural category.</summary>
internal sealed class PluralBranch(bool isExact, int exactValue, PluralCategory category, MessagePart[] parts)
{
    public bool IsExact { get; } = isExact;

    public int ExactValue { get; } = exactValue;

    public PluralCategory Category { get; } = category;

    public MessagePart[] Parts { get; } = parts;
}

internal sealed class MessageArgumentInfo(string name)
{
    public string Name { get; } = name;

    /// <summary>Whether the argument selects a plural block, which makes it an <c>int</c>.</summary>
    public bool IsPlural { get; set; }

    public bool IsUsed { get; set; }
}

internal sealed class ParsedMessage(MessagePart[] parts, List<MessageArgumentInfo> arguments)
{
    public MessagePart[] Parts { get; } = parts;

    /// <summary>Arguments in the order they first appear.</summary>
    public List<MessageArgumentInfo> Arguments { get; } = arguments;

    /// <summary>Whether the message is only literal text.</summary>
    public bool IsPlainText => Parts.Length == 0 || (Parts.Length == 1 && Parts[0].Kind == MessagePartKind.Text);

    public string PlainText => Parts.Length == 0 ? string.Empty : Parts[0].Text;
}

/// <summary>Parses the ICU MessageFormat 1 subset used by the localization files: text, <c>{name}</c> and <c>{name, plural, ...}</c>.</summary>
internal sealed class MessageParser
{
    private static readonly char[] WordEnd = [' ', '\t', '\r', '\n', ',', '{', '}'];

    private readonly string message;
    private readonly List<MessageArgumentInfo> arguments = [];
    private readonly bool hasExpectedArguments;
    private int position;
    private string error = string.Empty;

    private MessageParser(string message, IReadOnlyList<MessageArgumentInfo>? expected)
    {
        this.message = message;

        if (expected != null)
        {
            hasExpectedArguments = true;

            foreach (var argument in expected)
            {
                arguments.Add(new MessageArgumentInfo(argument.Name));
            }
        }
    }

    /// <summary>
    /// Parses a message, or returns null with the reason in <paramref name="error"/>.
    /// With <paramref name="expected"/>, the message must use the same arguments, with a plural only where the expected one is.
    /// </summary>
    public static ParsedMessage? Parse(string message, IReadOnlyList<MessageArgumentInfo>? expected, out string error)
    {
        var parser = new MessageParser(message, expected);
        var parts = new List<MessagePart>();

        if (!parser.ParseParts(false, parts))
        {
            error = parser.error;
            return null;
        }

        if (expected != null)
        {
            for (var i = 0; i < expected.Count; i++)
            {
                var argument = parser.arguments[i];

                if (!argument.IsUsed)
                {
                    error = "Argument '" + argument.Name + "' of the English text is missing";
                    return null;
                }

                if (argument.IsPlural && !expected[i].IsPlural)
                {
                    error = "Argument '" + argument.Name + "' is not a plural in the English text";
                    return null;
                }
            }
        }

        error = string.Empty;
        return new ParsedMessage([.. parts], parser.arguments);
    }

    private bool Fail(string reason)
    {
        error = reason + " at position " + (position + 1).ToString(CultureInfo.InvariantCulture);
        return false;
    }

    private bool ParseParts(bool inPlural, List<MessagePart> parts)
    {
        var text = new StringBuilder();

        while (position < message.Length)
        {
            var character = message[position];

            if (character == '\'')
            {
                ParseApostrophe(text, inPlural);
            }
            else if (character == '{')
            {
                FlushText(text, parts);

                if (!ParseArgument(inPlural, parts))
                {
                    return false;
                }
            }
            else if (character == '}')
            {
                // The closing brace of a plural branch is left for the caller
                if (inPlural)
                {
                    break;
                }

                return Fail("Unmatched '}'");
            }
            else if (character == '#' && inPlural)
            {
                FlushText(text, parts);
                parts.Add(MessagePart.CreateCount());
                position++;
            }
            else
            {
                text.Append(character);
                position++;
            }
        }

        FlushText(text, parts);
        return true;
    }

    private static void FlushText(StringBuilder text, List<MessagePart> parts)
    {
        if (text.Length == 0)
        {
            return;
        }

        parts.Add(MessagePart.CreateText(text.ToString()));
        text.Clear();
    }

    // ICU apostrophes: '' is a literal apostrophe, one before a brace (or # in a plural) quotes up to the next apostrophe
    private void ParseApostrophe(StringBuilder text, bool inPlural)
    {
        var next = position + 1 < message.Length ? message[position + 1] : '\0';

        if (next == '\'')
        {
            text.Append('\'');
            position += 2;
            return;
        }

        if (next is not ('{' or '}') && !(inPlural && next == '#'))
        {
            text.Append('\'');
            position++;
            return;
        }

        position++;

        while (position < message.Length)
        {
            var character = message[position];

            if (character == '\'')
            {
                if (position + 1 < message.Length && message[position + 1] == '\'')
                {
                    text.Append('\'');
                    position += 2;
                    continue;
                }

                position++;
                return;
            }

            text.Append(character);
            position++;
        }
    }

    private bool ParseArgument(bool inPlural, List<MessagePart> parts)
    {
        position++;
        SkipWhitespace();

        var name = ReadWord();

        if (name.Length == 0)
        {
            return Fail("Missing argument name");
        }

        // The name becomes a parameter, which also rejects positional arguments such as {0}
        if (!SyntaxFacts.IsValidIdentifier(name))
        {
            return Fail("Argument '" + name + "' must be a name, positional arguments are not supported");
        }

        var argumentIndex = GetArgumentIndex(name);

        if (argumentIndex < 0)
        {
            return Fail("Argument '" + name + "' is not in the English text");
        }

        SkipWhitespace();

        if (Peek() == '}')
        {
            position++;
            arguments[argumentIndex].IsUsed = true;
            parts.Add(MessagePart.CreateArgument(argumentIndex));
            return true;
        }

        if (Peek() != ',')
        {
            return Fail("Expected '}' or ','");
        }

        position++;
        SkipWhitespace();

        var type = ReadWord();

        if (type != "plural")
        {
            return Fail("Argument type '" + type + "' is not supported");
        }

        if (inPlural)
        {
            return Fail("Nested plurals are not supported");
        }

        SkipWhitespace();

        if (Peek() != ',')
        {
            return Fail("Expected ',' after 'plural'");
        }

        position++;
        arguments[argumentIndex].IsUsed = true;
        arguments[argumentIndex].IsPlural = true;

        var branches = new List<PluralBranch>();
        var hasOther = false;

        while (true)
        {
            SkipWhitespace();

            if (Peek() == '}')
            {
                position++;
                break;
            }

            var selector = ReadWord();

            if (selector.Length == 0)
            {
                return Fail("Expected a plural selector");
            }

            if (selector.StartsWith("offset:", StringComparison.Ordinal))
            {
                return Fail("Plural offsets are not supported");
            }

            if (!ParseBranchSelector(selector, out var isExact, out var exactValue, out var category))
            {
                return false;
            }

            foreach (var other in branches)
            {
                if (other.IsExact == isExact && (isExact ? other.ExactValue == exactValue : other.Category == category))
                {
                    return Fail("Duplicate plural selector '" + selector + "'");
                }
            }

            SkipWhitespace();

            if (Peek() != '{')
            {
                return Fail("Expected '{' after '" + selector + "'");
            }

            position++;

            var branchParts = new List<MessagePart>();

            if (!ParseParts(true, branchParts))
            {
                return false;
            }

            if (Peek() != '}')
            {
                return Fail("Unterminated plural branch");
            }

            position++;
            branches.Add(new PluralBranch(isExact, exactValue, category, [.. branchParts]));
            hasOther |= !isExact && category == PluralCategory.Other;
        }

        if (!hasOther)
        {
            return Fail("Plural is missing the 'other' selector");
        }

        parts.Add(MessagePart.CreatePlural(argumentIndex, [.. branches]));
        return true;
    }

    private bool ParseBranchSelector(string selector, out bool isExact, out int exactValue, out PluralCategory category)
    {
        isExact = false;
        exactValue = 0;
        category = PluralCategory.Other;

        if (selector[0] == '=')
        {
            isExact = true;

            if (!int.TryParse(selector.Substring(1), NumberStyles.None, CultureInfo.InvariantCulture, out exactValue))
            {
                return Fail("Invalid exact plural selector '" + selector + "'");
            }

            return true;
        }

        switch (selector)
        {
            case "zero":
                category = PluralCategory.Zero;
                return true;
            case "one":
                category = PluralCategory.One;
                return true;
            case "two":
                category = PluralCategory.Two;
                return true;
            case "few":
                category = PluralCategory.Few;
                return true;
            case "many":
                category = PluralCategory.Many;
                return true;
            case "other":
                return true;
            default:
                return Fail("Unknown plural selector '" + selector + "'");
        }
    }

    // Returns -1 for a name that is not an expected argument
    private int GetArgumentIndex(string name)
    {
        for (var i = 0; i < arguments.Count; i++)
        {
            if (arguments[i].Name == name)
            {
                return i;
            }
        }

        if (hasExpectedArguments)
        {
            return -1;
        }

        arguments.Add(new MessageArgumentInfo(name));
        return arguments.Count - 1;
    }

    private char Peek() => position < message.Length ? message[position] : '\0';

    private void SkipWhitespace()
    {
        while (position < message.Length && char.IsWhiteSpace(message[position]))
        {
            position++;
        }
    }

    private string ReadWord()
    {
        var start = position;
        var end = message.IndexOfAny(WordEnd, position);
        position = end < 0 ? message.Length : end;

        return message.Substring(start, position - start);
    }
}
