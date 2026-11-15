using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using GUI.Utils;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using ValveResourceFormat.SourceGenerators.Localization;

namespace ValveResourceFormat.SourceGenerators;

/// <summary>
/// Generates the language classes read through <c>Translations.Locale</c> from the localization files, <c>en.csv</c> and one <c>&lt;culture&gt;.csv</c> per language.
/// </summary>
/// <remarks>
/// English becomes the class <c>English</c> with a virtual member per key. Every translation becomes a class
/// <c>Language_&lt;culture&gt;</c> deriving from it that overrides the keys it translates, so a missing translation falls back to English.
/// A message without arguments is a property. A message with arguments is a method returning a string and an overload
/// formatting into a span, taking an <c>int</c> for plural arguments and a <c>string</c> for the others.
/// A plural block is a <c>switch</c> on the plural rule of the language, each branch writing the whole message.
/// Every language is parsed here, so an invalid translation is a build error.
/// Nothing is generated when there is no <c>en.csv</c>.
/// </remarks>
[Generator(LanguageNames.CSharp)]
public sealed class StringsGenerator : IIncrementalGenerator
{
    private const string EnglishName = "en";

    private static readonly DiagnosticDescriptor MalformedCsv = new(
        "VRFSTR001",
        "Malformed localization file",
        "{0}",
        "Localization",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor DuplicateKey = new(
        "VRFSTR002",
        "Duplicate localization key",
        "Key '{0}' is defined more than once",
        "Localization",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor InvalidKey = new(
        "VRFSTR003",
        "Invalid localization key",
        "Key '{0}' is not a valid C# identifier",
        "Localization",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor MalformedMessage = new(
        "VRFSTR004",
        "Malformed localization message",
        "Message '{0}': {1}",
        "Localization",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor UnknownKey = new(
        "VRFSTR005",
        "Unknown localization key",
        "Key '{0}' is not in en.csv",
        "Localization",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    /// <summary>A key of a localization file with its parsed message.</summary>
    private sealed class Entry(string key, ParsedMessage message)
    {
        public string Key { get; } = key;

        public ParsedMessage Message { get; } = message;
    }

    /// <summary>A translation, with the parsed message by key.</summary>
    private sealed class Language(string name)
    {
        public string Name { get; } = name;

        public string Code { get; } = GetLanguageCode(name);

        public Dictionary<string, ParsedMessage> Messages { get; } = [];

        public string ClassName => "Language_" + Name.Replace('-', '_');
    }

    /// <inheritdoc/>
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var files = context.AdditionalTextsProvider
            .Where(static text => string.Equals(Path.GetExtension(text.Path), ".csv", StringComparison.OrdinalIgnoreCase))
            .Select(static (text, cancellationToken) => (text.Path, Content: text.GetText(cancellationToken)?.ToString()))
            .Collect();

        context.RegisterSourceOutput(files, static (context, files) =>
        {
            var source = Generate(context, files);

            if (source != null)
            {
                context.AddSource("Strings.g.cs", SourceText.From(source, Encoding.UTF8));
            }
        });
    }

    private static string? Generate(SourceProductionContext context, ImmutableArray<(string Path, string? Content)> files)
    {
        string? englishPath = null;
        string? englishContent = null;

        foreach (var (path, content) in files)
        {
            if (content != null && Path.GetFileNameWithoutExtension(path) == EnglishName)
            {
                englishPath = path;
                englishContent = content;
            }
        }

        if (englishPath == null || englishContent == null)
        {
            return null;
        }

        var hasErrors = false;
        var english = ReadEnglish(context, englishPath, englishContent, ref hasErrors);

        if (english == null)
        {
            return null;
        }

        var englishByKey = new Dictionary<string, Entry>();

        foreach (var entry in english)
        {
            englishByKey.Add(entry.Key, entry);
        }

        var languages = new List<Language>();

        foreach (var (path, content) in files)
        {
            var name = Path.GetFileNameWithoutExtension(path);

            if (content == null || name == EnglishName)
            {
                continue;
            }

            languages.Add(ReadLanguage(context, path, name, content, englishByKey, ref hasErrors));
        }

        if (hasErrors)
        {
            return null;
        }

        languages.Sort(static (a, b) => string.CompareOrdinal(a.Name, b.Name));
        return Emit(english, languages);
    }

    private static List<CsvRow>? ReadRows(SourceProductionContext context, string path, string content)
    {
        var rows = new List<CsvRow>();

        if (CsvReader.TryRead(content, rows, out var error, out var errorLine))
        {
            return rows;
        }

        context.ReportDiagnostic(Diagnostic.Create(MalformedCsv, GetLocation(path, errorLine), error));
        return null;
    }

    // Parses a message and checks its plurals against the language, reporting why it is invalid
    private static ParsedMessage? ParseMessage(SourceProductionContext context, Location location, CsvRow row, List<MessageArgumentInfo>? expected, string languageCode)
    {
        var parsed = MessageParser.Parse(row.Text, expected, out var parseError);
        var error = parsed == null ? parseError : ValidatePlurals(parsed, languageCode);

        if (error != null)
        {
            context.ReportDiagnostic(Diagnostic.Create(MalformedMessage, location, row.Key, error));
            return null;
        }

        return parsed;
    }

    private static List<Entry>? ReadEnglish(SourceProductionContext context, string path, string content, ref bool hasErrors)
    {
        var rows = ReadRows(context, path, content);

        if (rows == null)
        {
            return null;
        }

        var entries = new List<Entry>();
        var keys = new HashSet<string>();

        foreach (var row in rows)
        {
            var location = GetLocation(path, row.Line);

            if (!SyntaxFacts.IsValidIdentifier(row.Key))
            {
                context.ReportDiagnostic(Diagnostic.Create(InvalidKey, location, row.Key));
                hasErrors = true;
                continue;
            }

            if (!keys.Add(row.Key))
            {
                context.ReportDiagnostic(Diagnostic.Create(DuplicateKey, location, row.Key));
                hasErrors = true;
                continue;
            }

            var parsed = ParseMessage(context, location, row, null, EnglishName);

            if (parsed == null)
            {
                hasErrors = true;
                continue;
            }

            entries.Add(new Entry(row.Key, parsed));
        }

        return entries;
    }

    private static Language ReadLanguage(SourceProductionContext context, string path, string name, string content, Dictionary<string, Entry> englishByKey, ref bool hasErrors)
    {
        var language = new Language(name);
        var rows = ReadRows(context, path, content);

        if (rows == null)
        {
            hasErrors = true;
            return language;
        }

        foreach (var row in rows)
        {
            var location = GetLocation(path, row.Line);

            if (!englishByKey.TryGetValue(row.Key, out var englishEntry))
            {
                context.ReportDiagnostic(Diagnostic.Create(UnknownKey, location, row.Key));
                hasErrors = true;
                continue;
            }

            // An empty translation is a string the translator has not filled in yet
            if (row.Text.Length == 0)
            {
                continue;
            }

            if (language.Messages.ContainsKey(row.Key))
            {
                context.ReportDiagnostic(Diagnostic.Create(DuplicateKey, location, row.Key));
                hasErrors = true;
                continue;
            }

            var parsed = ParseMessage(context, location, row, englishEntry.Message.Arguments, language.Code);

            if (parsed == null)
            {
                hasErrors = true;
                continue;
            }

            language.Messages.Add(row.Key, parsed);
        }

        return language;
    }

    private static string Emit(List<Entry> english, List<Language> languages)
    {
        var source = new StringBuilder();
        source.Append("// <auto-generated/>\n");
        source.Append("#nullable enable\n\n");
        source.Append("using System;\n");
        source.Append("using System.Globalization;\n\n");
        source.Append("namespace GUI.Utils;\n\n");

        source.Append("internal class English\n{\n");
        AppendMembers(source, english, null);
        source.Append("}\n\n");

        foreach (var language in languages)
        {
            source.Append("internal sealed class ").Append(language.ClassName).Append(" : English\n{\n");
            AppendMembers(source, english, language);
            source.Append("}\n\n");
        }

        AppendCreateLanguage(source, languages);

        return source.ToString();
    }

    // Members of English when language is null, otherwise the overrides of the keys the language translates
    private static void AppendMembers(StringBuilder source, List<Entry> english, Language? language)
    {
        var modifier = language == null ? "virtual" : "override";
        var pluralRule = GetPluralRuleName(language?.Code ?? EnglishName);

        foreach (var entry in english)
        {
            var message = entry.Message;

            if (language != null && !language.Messages.TryGetValue(entry.Key, out message))
            {
                continue;
            }

            if (message.IsPlainText)
            {
                AppendProperty(source, modifier, entry.Key, message.PlainText);
            }
            else
            {
                AppendMethods(source, modifier, entry.Key, entry.Message.Arguments, message, pluralRule);
            }
        }
    }

    private static void AppendProperty(StringBuilder source, string modifier, string key, string text)
    {
        source.Append("    public ").Append(modifier).Append(" string ").Append(key).Append(" => ").Append(Literal(text)).Append(";\n");
    }

    private static void AppendMethods(StringBuilder source, string modifier, string key, List<MessageArgumentInfo> arguments, ParsedMessage message, string pluralRule)
    {
        source.Append("    public ").Append(modifier).Append(" string ").Append(key).Append('(').Append(GetParameters(arguments, "string")).Append(')');
        AppendBody(source, message.Parts, arguments, pluralRule, isSpan: false);

        // Text arguments are spans so per-frame callers can pass numbers formatted into a stack buffer
        source.Append("    public ").Append(modifier).Append(" bool ").Append(key).Append("(Span<char> destination, out int charsWritten, ").Append(GetParameters(arguments, "ReadOnlySpan<char>")).Append(')');
        AppendBody(source, message.Parts, arguments, pluralRule, isSpan: true);
    }

    // An expression body for a message without plural blocks, otherwise a block of switches
    private static void AppendBody(StringBuilder source, MessagePart[] parts, List<MessageArgumentInfo> arguments, string pluralRule, bool isSpan)
    {
        if (!parts.Any(static part => part.Kind == MessagePartKind.Plural))
        {
            source.Append("\n        => ").Append(GetWriteExpression(parts, arguments, isSpan)).Append(";\n");
            return;
        }

        source.Append("\n    {\n");
        AppendBranches(source, 2, [.. parts], arguments, pluralRule, isSpan);
        source.Append("    }\n");
    }

    // Writes the statements that pick the branches of the first plural block, nesting for the next ones
    private static void AppendBranches(StringBuilder source, int indent, List<MessagePart> parts, List<MessageArgumentInfo> arguments, string pluralRule, bool isSpan)
    {
        var pluralIndex = parts.FindIndex(static part => part.Kind == MessagePartKind.Plural);

        if (pluralIndex < 0)
        {
            Indent(source, indent).Append("return ").Append(GetWriteExpression(parts.ToArray(), arguments, isSpan)).Append(";\n");
            return;
        }

        var plural = parts[pluralIndex];
        var count = GetParameterName(arguments[plural.ArgumentIndex]);
        PluralBranch? otherBranch = null;
        var hasExact = false;
        var hasCategory = false;

        foreach (var branch in plural.Branches)
        {
            if (branch.IsExact)
            {
                hasExact = true;
            }
            else if (branch.Category == PluralCategory.Other)
            {
                otherBranch = branch;
            }
            else
            {
                hasCategory = true;
            }
        }

        if (otherBranch == null)
        {
            throw new InvalidOperationException("Plural has no 'other' branch");
        }

        if (hasExact)
        {
            Indent(source, indent).Append("switch (").Append(count).Append(")\n");
            Indent(source, indent).Append("{\n");

            foreach (var branch in plural.Branches)
            {
                if (branch.IsExact)
                {
                    Indent(source, indent + 1).Append("case ").Append(branch.ExactValue.ToString(CultureInfo.InvariantCulture)).Append(":\n");
                    AppendBranches(source, indent + 2, Splice(parts, pluralIndex, branch), arguments, pluralRule, isSpan);
                }
            }

            Indent(source, indent).Append("}\n\n");
        }

        if (!hasCategory)
        {
            AppendBranches(source, indent, Splice(parts, pluralIndex, otherBranch), arguments, pluralRule, isSpan);
            return;
        }

        Indent(source, indent).Append("switch (PluralRules.").Append(pluralRule).Append('(').Append(count).Append("))\n");
        Indent(source, indent).Append("{\n");

        foreach (var branch in plural.Branches)
        {
            if (!branch.IsExact && branch.Category != PluralCategory.Other)
            {
                Indent(source, indent + 1).Append("case PluralCategory.").Append(branch.Category.ToString()).Append(":\n");
                AppendBranches(source, indent + 2, Splice(parts, pluralIndex, branch), arguments, pluralRule, isSpan);
            }
        }

        Indent(source, indent + 1).Append("default:\n");
        AppendBranches(source, indent + 2, Splice(parts, pluralIndex, otherBranch), arguments, pluralRule, isSpan);
        Indent(source, indent).Append("}\n");
    }

    // Replaces the plural block with the parts of a branch, where # becomes the count argument
    private static List<MessagePart> Splice(List<MessagePart> parts, int pluralIndex, PluralBranch branch)
    {
        var result = new List<MessagePart>(parts.Count + branch.Parts.Length);
        var argumentIndex = parts[pluralIndex].ArgumentIndex;

        for (var i = 0; i < parts.Count; i++)
        {
            if (i != pluralIndex)
            {
                result.Add(parts[i]);
                continue;
            }

            foreach (var part in branch.Parts)
            {
                result.Add(part.Kind == MessagePartKind.Count ? MessagePart.CreateArgument(argumentIndex) : part);
            }
        }

        return result;
    }

    private static string GetWriteExpression(MessagePart[] parts, List<MessageArgumentInfo> arguments, bool isSpan)
    {
        var text = new StringBuilder();

        foreach (var part in parts)
        {
            if (part.Kind == MessagePartKind.Text)
            {
                // Double braces for the interpolated string and escape quotes, which FormatLiteral leaves when quote is false
                var literal = SymbolDisplay.FormatLiteral(part.Text.Replace("{", "{{").Replace("}", "}}"), quote: false);
                text.Append(literal.Replace("\"", "\\\""));
            }
            else
            {
                text.Append('{').Append(GetParameterName(arguments[part.ArgumentIndex])).Append('}');
            }
        }

        return isSpan
            ? $"destination.TryWrite(CultureInfo.InvariantCulture, $\"{text}\", out charsWritten)"
            : $"string.Create(CultureInfo.InvariantCulture, $\"{text}\")";
    }

    private static string GetParameters(List<MessageArgumentInfo> arguments, string textType)
    {
        var parameters = new StringBuilder();

        foreach (var argument in arguments)
        {
            if (parameters.Length > 0)
            {
                parameters.Append(", ");
            }

            parameters.Append(argument.IsPlural ? "int" : textType).Append(' ').Append(GetParameterName(argument));
        }

        return parameters.ToString();
    }

    private static string GetParameterName(MessageArgumentInfo argument)
        => SyntaxFacts.GetKeywordKind(argument.Name) == SyntaxKind.None ? argument.Name : "@" + argument.Name;

    private static void AppendCreateLanguage(StringBuilder source, List<Language> languages)
    {
        source.Append("internal static partial class Translations\n{\n");
        source.Append("    private static English? CreateLanguage(string name) => name switch\n    {\n");

        foreach (var language in languages)
        {
            source.Append("        ").Append(Literal(language.Name)).Append(" => new ").Append(language.ClassName).Append("(),\n");
        }

        source.Append("        _ => null,\n    };\n");
        source.Append("}\n");
    }

    private static string GetLanguageCode(string cultureName) => cultureName.Split('-')[0];

    // The PluralRules method of a language code, such as SelectRu
    private static string GetPluralRuleName(string code) => "Select" + char.ToUpperInvariant(code[0]) + code.Substring(1);

    // Returns the reason a message's plural categories differ from those of its language, or null
    private static string? ValidatePlurals(ParsedMessage message, string code)
    {
        var categories = PluralRules.GetCategories(code);

        foreach (var part in message.Parts)
        {
            if (part.Kind != MessagePartKind.Plural)
            {
                continue;
            }

            if (categories == null)
            {
                return "Language '" + code + "' has no plural rules";
            }

            foreach (var branch in part.Branches)
            {
                if (!branch.IsExact && Array.IndexOf(categories, branch.Category) < 0)
                {
                    return "Language '" + code + "' has no plural category '" + branch.Category.ToString().ToLowerInvariant() + "'";
                }
            }

            foreach (var category in categories)
            {
                if (!part.Branches.Any(branch => !branch.IsExact && branch.Category == category))
                {
                    return "Plural is missing category '" + category.ToString().ToLowerInvariant() + "' of language '" + code + "'";
                }
            }
        }

        return null;
    }

    private static StringBuilder Indent(StringBuilder source, int level) => source.Append(' ', level * 4);

    private static Location GetLocation(string path, int line)
    {
        var position = new LinePosition(line, 0);
        return Location.Create(path, default, new LinePositionSpan(position, position));
    }

    private static string Literal(string value) => SymbolDisplay.FormatLiteral(value, quote: true);
}
