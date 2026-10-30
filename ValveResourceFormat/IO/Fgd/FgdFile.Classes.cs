using System.Globalization;
using System.Linq;
using System.Text;
using ValveResourceFormat.Serialization.KeyValues;
using ValveResourceFormat.Utils;
using Token = ValveResourceFormat.IO.Fgd.FgdTokenizer.Token;

namespace ValveResourceFormat.IO.Fgd;

public sealed partial class FgdFile
{
    // What reading a variable comes to
    private enum VariableResult
    {
        Failed,
        Skipped,
        Read,
    }

    // name(type) = [ ... ] of a class, between its header and its body
    private bool ParseClass(FgdTokenizer tokens, FgdClass gameClass)
    {
        if (!ParseClassHeader(tokens, gameClass))
        {
            return false;
        }

        if (!tokens.Accept("="))
        {
            Error(tokens, "Expecting '='");
            return false;
        }

        if (!tokens.TryReadIdentifier(out var name))
        {
            Error(tokens, "Expecting class name");
            return false;
        }

        gameClass.Name = name.Text;

        if (tokens.Accept(":"))
        {
            gameClass.Description = ParseText(tokens);

            if (tokens.Accept(":"))
            {
                gameClass.HelpLink = ParseText(tokens);
            }
        }

        if (!tokens.Accept("["))
        {
            Error(tokens, "Expecting '['");
            return false;
        }

        if (!ParseClassBody(tokens, gameClass))
        {
            return false;
        }

        if (!tokens.Accept("]"))
        {
            Error(tokens, "Expecting ']'");
            return false;
        }

        return true;
    }

    // What comes before the '=': the classes it is based on, its size and color, its tags and its helpers
    private bool ParseClassHeader(FgdTokenizer tokens, FgdClass gameClass)
    {
        while (tokens.TryReadIdentifier(out var word))
        {
            if (word.Is("halfgridsnap"))
            {
                gameClass.HalfGridSnap = true;
                continue;
            }

            if (tokens.PeekIs(0, "{"))
            {
                if (!ParseBlock(tokens, out var keys, out var text))
                {
                    return false;
                }

                if (word.Is("metadata"))
                {
                    AddMissingKeys(gameClass.Metadata, keys);
                }
                else
                {
                    gameClass.Helpers.Add(new FgdHelper { Name = word.Text, Metadata = keys, MetadataText = text });
                }

                continue;
            }

            if (!tokens.Accept("("))
            {
                Error(tokens, "Expecting '('");
                return false;
            }

            bool parsed;

            if (word.Is("base"))
            {
                parsed = ParseBaseClasses(tokens, gameClass);
            }
            else if (word.Is("size"))
            {
                parsed = ParseSize(tokens, gameClass);
            }
            else if (word.Is("color"))
            {
                parsed = ParseColor(tokens, gameClass);
            }
            else if (word.Is("tags"))
            {
                parsed = ParseTags(tokens, gameClass);
            }
            else
            {
                parsed = ParseHelper(tokens, gameClass, word.Text);
            }

            if (!parsed)
            {
                return false;
            }
        }

        return true;
    }

    private bool ParseBaseClasses(FgdTokenizer tokens, FgdClass gameClass)
    {
        if (!tokens.TryReadIdentifier(out var name))
        {
            Error(tokens, "Expecting base class name");
            return false;
        }

        while (!tokens.Accept("="))
        {
            if (classesByName.GetValueOrDefault(name.Text) is not { } baseClass)
            {
                Error(tokens, $"Undefined base class '{name.Text}");
            }
            else if (!gameClass.BaseClasses.Contains(baseClass))
            {
                gameClass.BaseClasses.Add(baseClass);
            }

            if (tokens.Accept(")"))
            {
                return true;
            }

            if (!tokens.Accept(","))
            {
                Error(tokens, "Expecting ',' or ')'");
                return false;
            }

            if (!tokens.TryReadIdentifier(out name))
            {
                Error(tokens, "Expecting base class name");
                return false;
            }
        }

        Error(tokens, "Using deprecated 'base=prefix' syntax for adding a prefix to a base class");
        return false;
    }

    // size(x y z) is a box centered on the entity, size(a b c, d e f) one from corner to corner
    private bool ParseSize(FgdTokenizer tokens, FgdClass gameClass)
    {
        if (!TryReadNumbers(tokens, out var first))
        {
            Error(tokens, "Expecting numbers 'min_x' 'min_y' 'min_z'");
            return false;
        }

        if (tokens.Accept(","))
        {
            if (!TryReadNumbers(tokens, out var second))
            {
                Error(tokens, "Expecting numbers 'max_x' 'max_y' 'max_z'");
                return false;
            }

            gameClass.Mins = first;
            gameClass.Maxs = second;
        }
        else
        {
            gameClass.Maxs = first * 0.5f;
            gameClass.Mins = -gameClass.Maxs;
        }

        gameClass.HasSize = true;

        if (!tokens.Accept(")"))
        {
            Error(tokens, "Expecting ')'");
            return false;
        }

        return true;
    }

    private static bool TryReadNumbers(FgdTokenizer tokens, out Vector3 numbers)
    {
        numbers = default;

        if (!tokens.TryRead(out var x) || !tokens.TryRead(out var y) || !tokens.TryRead(out var z))
        {
            return false;
        }

        if (!IsNumber(x) || !IsNumber(y) || !IsNumber(z))
        {
            return false;
        }

        numbers = new Vector3(LeadingNumber.ParseFloat(x.Span), LeadingNumber.ParseFloat(y.Span), LeadingNumber.ParseFloat(z.Span));
        return true;
    }

    private static bool IsNumber(Token token) => token.IsInteger || token.IsFloat;

    private bool ParseColor(FgdTokenizer tokens, FgdClass gameClass)
    {
        if (!tokens.TryRead(out var red) || !tokens.TryRead(out var green) || !tokens.TryRead(out var blue)
            || !red.IsInteger || !green.IsInteger || !blue.IsInteger)
        {
            Error(tokens, "Expecting integers 'r' 'g' 'b'");
            return false;
        }

        gameClass.Color = ParseColor(red, green, blue);
        gameClass.HasColor = true;

        if (!tokens.Accept(")"))
        {
            Error(tokens, "Expecting ')'");
            return false;
        }

        return true;
    }

    private bool ParseTags(FgdTokenizer tokens, FgdClass gameClass)
    {
        if (!tokens.TryReadIdentifier(out var tag))
        {
            Error(tokens, "Expecting tag name");
            return false;
        }

        while (true)
        {
            gameClass.AddTag(tag.Text);

            if (tokens.Accept(")"))
            {
                return true;
            }

            if (!tokens.Accept(","))
            {
                Error(tokens, "Expecting ',' or ')'");
                return false;
            }

            if (!tokens.TryReadIdentifier(out tag))
            {
                Error(tokens, "Expecting tag name");
                return false;
            }
        }
    }

    // Every token up to the ')' but the commas is an argument, so "255 255 255" is three of them
    private bool ParseHelper(FgdTokenizer tokens, FgdClass gameClass, string name)
    {
        var helper = new FgdHelper { Name = name };

        while (!tokens.Accept(")"))
        {
            if (tokens.Accept(","))
            {
                continue;
            }

            if (!tokens.TryRead(out var argument))
            {
                Error(tokens, "Expecting ')'");
                return false;
            }

            if (argument.Unquote() is { Length: > 0 } text)
            {
                helper.Arguments.Add(text);
            }
        }

        gameClass.Helpers.Add(helper);
        return true;
    }

    // Strings joined by '+'. It ends without complaint where the next token is not a string.
    private static string ParseText(FgdTokenizer tokens)
    {
        if (!tokens.TryReadString(out var first))
        {
            return string.Empty;
        }

        var text = first.Unquote();

        if (!tokens.Accept("+"))
        {
            return text;
        }

        var builder = new StringBuilder(text);

        while (tokens.TryReadString(out var part))
        {
            builder.Append(part.Unquote());

            if (!tokens.Accept("+"))
            {
                break;
            }
        }

        return builder.ToString();
    }

    private bool ParseClassBody(FgdTokenizer tokens, FgdClass gameClass)
    {
        while (tokens.HasMore && !tokens.PeekIs(0, "]"))
        {
            // A variable can be named input or output, which is then followed by its type
            if (tokens.PeekIs(0, "input") && !tokens.PeekIs(1, "("))
            {
                tokens.Accept("input");

                var input = new FgdInputOutput();

                if (!ParseInputOutput(tokens, gameClass, input))
                {
                    return false;
                }

                gameClass.Inputs.Add(input);
                continue;
            }

            if (tokens.PeekIs(0, "output") && !tokens.PeekIs(1, "("))
            {
                tokens.Accept("output");

                var output = new FgdInputOutput();

                if (!ParseInputOutput(tokens, gameClass, output))
                {
                    return false;
                }

                gameClass.Outputs.Add(output);
                continue;
            }

            var variable = new FgdVariable();

            switch (ParseVariable(tokens, variable))
            {
                case VariableResult.Failed:
                    return false;

                case VariableResult.Read:
                    if (gameClass.FindVariable(variable.Name) is { } existing && existing.DeclaredType != variable.DeclaredType)
                    {
                        Error(tokens, $"{gameClass.Name}: Variable '{variable.Name}' is multiply defined with different types.");
                    }

                    gameClass.AddVariable(variable);
                    break;
            }
        }

        return true;
    }

    private bool ParseInputOutput(FgdTokenizer tokens, FgdClass gameClass, FgdInputOutput inputOutput)
    {
        inputOutput.Metadata["originating_classname"] = gameClass.Name;
        inputOutput.Metadata["originating_filename"] = tokens.FileName;
        inputOutput.Metadata["originating_linenumber"] = tokens.Line;

        if (!tokens.TryReadIdentifier(out var name))
        {
            Error(tokens, "Expecting identifier");
            return false;
        }

        inputOutput.Name = name.Text;

        if (!tokens.Accept("(") || !tokens.TryReadIdentifier(out var type) || !tokens.Accept(")"))
        {
            Error(tokens, "Expecting '(' <type> ')'");
            return false;
        }

        // The game says what an api input or output takes. Some builds name it cpp
        if (type.Is("api") || type.Is("cpp"))
        {
            if (tokens.PeekIs(0, "{") && !ParseInputOutputKeys(tokens, inputOutput))
            {
                return false;
            }

            inputOutput.IsApi = true;
            inputOutput.Type = FgdInputOutputType.Void;
            inputOutput.Metadata["expects_cpp_declaration"] = true;
            return true;
        }

        var typeName = Array.Find(InputOutputTypeNames, entry => type.Is(entry.Name));

        if (typeName.Name == null)
        {
            Error(tokens, $"Bad input/output type '{type.Text}'");
            return false;
        }

        inputOutput.Type = typeName.Type;

        if (tokens.PeekIs(0, "{") && !ParseInputOutputKeys(tokens, inputOutput))
        {
            return false;
        }

        if (tokens.Accept(":"))
        {
            inputOutput.Description = ParseText(tokens);
        }

        return true;
    }

    private bool ParseInputOutputKeys(FgdTokenizer tokens, FgdInputOutput inputOutput)
    {
        if (!ParseBlock(tokens, out var keys, out _))
        {
            Error(tokens, $"Bad metadata for {inputOutput.Name}");
            return false;
        }

        foreach (var (key, value) in keys)
        {
            inputOutput.Metadata[key] = value;
        }

        return true;
    }

    // name(type) [meta] : "long name" : default : "description" = [ values ]
    private VariableResult ParseVariable(FgdTokenizer tokens, FgdVariable variable)
    {
        if (!tokens.TryReadIdentifier(out var name))
        {
            Error(tokens, "Expecting identifier (variable name)");
            return VariableResult.Failed;
        }

        variable.Name = name.Text;

        if (!tokens.Accept("("))
        {
            Error(tokens, "Expecting '('");
            return VariableResult.Failed;
        }

        if (tokens.Accept("*"))
        {
            variable.Metadata["report"] = true;
        }

        if (!tokens.SkipPast(")", out var typeText))
        {
            Error(tokens, "Expecting ')' after type identifier");
            return VariableResult.Failed;
        }

        variable.TypeName = typeText.Trim();

        if (!ParseVariableType(variable, typeText))
        {
            // The variable is kept, with the name of its type
            Error(tokens, $"'{typeText}' is not a valid variable type");
        }

        var type = variable.Type;

        // Older files write these after the type as plain words
        while (tokens.TryPeek(0, out var word) && (word.Is("report") || word.Is("readonly")))
        {
            tokens.TryRead(out _);
            variable.Metadata[word.Text.ToLowerInvariant()] = true;
        }

        if (tokens.PeekIs(0, "["))
        {
            if (!ParseMetaKeys(tokens, variable))
            {
                return VariableResult.Failed;
            }
        }
        else if (tokens.PeekIs(0, "{"))
        {
            if (!ParseBlock(tokens, out var keys, out _))
            {
                return VariableResult.Failed;
            }

            foreach (var (key, value) in keys)
            {
                variable.Metadata[key] = value;
            }
        }

        if (!tokens.Accept(":"))
        {
            variable.Description = variable.Name;
        }
        else
        {
            if (type == FgdVariableType.Flags)
            {
                Error(tokens, "Flag sets do not have long names");
                return VariableResult.Failed;
            }

            variable.LongName = ParseText(tokens);

            // Two colons in a row leave the default out
            if (tokens.Accept(":") && !tokens.PeekIs(0, ":") && !ParseDefault(tokens, variable))
            {
                return VariableResult.Failed;
            }

            if (tokens.Accept(":"))
            {
                variable.Description = ParseText(tokens);
            }
        }

        if (!tokens.Accept("="))
        {
            var missing = type switch
            {
                FgdVariableType.Flags => "flags",
                FgdVariableType.Choices or FgdVariableType.Suggestions => "choices",
                FgdVariableType.IntChoices => "intchoices",
                FgdVariableType.FloatChoices => "floatchoices",
                FgdVariableType.TagList => "tags",
                FgdVariableType.FlagChoices => "flagchoices",
                _ => null,
            };

            if (missing != null)
            {
                Error(tokens, $"No {missing} specified. Expected '=' '[' <{missing}> ']'");
                return VariableResult.Failed;
            }

            return VariableResult.Read;
        }

        if (type == FgdVariableType.Invalid)
        {
            // The values of a type that is not known are read as plain choices
            type = FgdVariableType.Choices;
        }
        else if (!FgdClass.HasChoices(type))
        {
            Error(tokens, $"Didn't expect '=' for non-flags non-choices variable {variable.Name}");
            return VariableResult.Failed;
        }

        if (!tokens.Accept("["))
        {
            Error(tokens, "Expecting '['");
            return VariableResult.Failed;
        }

        if (!ParseChoices(tokens, variable, type))
        {
            return VariableResult.Failed;
        }

        if (!tokens.Accept("]"))
        {
            Error(tokens, "Expecting ']'");
            return VariableResult.Failed;
        }

        CheckDefaultChoice(tokens, variable, type);
        return VariableResult.Read;
    }

    private bool ParseDefault(FgdTokenizer tokens, FgdVariable variable)
    {
        if (!tokens.TryRead(out var token))
        {
            Error(tokens, "Expected a default value");
            return false;
        }

        // Files written for other editors can give a number with a fraction without quotes
        if (!token.IsString && !token.IsFloat)
        {
            Error(tokens, "Expected a string or integer as default value");
            return false;
        }

        var text = token.Unquote();

        switch (GetStorage(variable.DeclaredType))
        {
            case FgdStorage.String:
                variable.DefaultString = text;
                variable.HasDefault = true;
                break;

            case FgdStorage.Integer:
                SetDefaultInteger(variable, LeadingNumber.ParseInteger(text));
                variable.HasDefault = true;
                break;
        }

        return true;
    }

    private static FgdStorage GetStorage(FgdVariableType type)
    {
        foreach (var typeName in FgdTypeName.All)
        {
            if (typeName.Type == type)
            {
                return typeName.Storage;
            }
        }

        return FgdStorage.String;
    }

    // The type is everything between the parentheses, with ':' between its parts
    private static bool ParseVariableType(FgdVariable variable, string text)
    {
        var parts = new FgdTokenizer(text, "(None)", ":");
        var isFirst = true;
        var takesElementType = true;
        var takesSubType = false;

        variable.DeclaredType = FgdVariableType.Invalid;
        variable.ElementTypes.Clear();
        variable.SubType = string.Empty;

        bool Invalid()
        {
            variable.DeclaredType = FgdVariableType.Invalid;
            variable.ElementTypes.Clear();
            variable.SubType = string.Empty;
            return false;
        }

        while (parts.TryRead(out var part))
        {
            if (isFirst && Array.Find(TypeAliases, alias => part.Is(alias.Name)) is { Name: not null } alias)
            {
                variable.DeclaredType = alias.Type;
                variable.SubType = alias.SubType;
                takesElementType = false;
                takesSubType = false;
                break;
            }

            if (!takesElementType)
            {
                if (!takesSubType)
                {
                    return Invalid();
                }

                variable.SubType = part.Text;
                takesSubType = false;
                continue;
            }

            FgdTypeName? found = null;

            foreach (var typeName in FgdTypeName.All)
            {
                if (part.Is(typeName.Name))
                {
                    found = typeName;
                    break;
                }
            }

            if (found is not { } entry)
            {
                return Invalid();
            }

            if (isFirst)
            {
                isFirst = false;
                variable.DeclaredType = entry.Type;
            }
            else
            {
                variable.ElementTypes.Add(entry.Type);
            }

            takesElementType = entry.TakesElementType;
            takesSubType = entry.TakesSubType;

            if ((takesElementType || takesSubType) && !parts.Accept(":"))
            {
                return Invalid();
            }
        }

        if (parts.HasMore || takesElementType || takesSubType)
        {
            return Invalid();
        }

        if (variable.Type is FgdVariableType.Resource or FgdVariableType.ResourceName or FgdVariableType.ResourceChoices)
        {
            foreach (var (name, extension) in ResourceSubTypes)
            {
                if (variable.SubType.Equals(name, StringComparison.OrdinalIgnoreCase))
                {
                    variable.SubType = extension;
                    break;
                }
            }
        }

        return true;
    }

    // [ key = "value", key, ... ] after the type of a variable
    private bool ParseMetaKeys(FgdTokenizer tokens, FgdVariable variable)
    {
        if (!tokens.Accept("["))
        {
            return true;
        }

        while (true)
        {
            if (!tokens.TryReadIdentifier(out var key))
            {
                Error(tokens, "Expecting <metadata keyname>");
            }

            var value = string.Empty;

            if (tokens.Accept("="))
            {
                value = ParseText(tokens);
            }

            if (key.Is("group") || key.Is("min") || key.Is("max") || key.Is("associated_key"))
            {
                variable.Metadata[key.Text.ToLowerInvariant()] = value;
            }
            else if (key.Is("important") || key.Is("randomizable") || key.Is("scale_world") || key.Is("scale_bone_relative")
                || key.Is("readonly") || key.Is("report") || key.Is("hidden"))
            {
                variable.Metadata[key.Text.ToLowerInvariant()] = true;
            }
            else
            {
                // The key is empty where it was not a name
                Error(tokens, $"Unrecognized meta key: {key.Span}");
                return false;
            }

            if (tokens.Accept("]"))
            {
                return true;
            }

            if (!tokens.Accept(","))
            {
                Error(tokens, "Expecting ',' or ']' after metadata");
                return false;
            }
        }
    }

    // The values of a variable that lists them. A value starts with a number or a string; anything else ends the list.
    private bool ParseChoices(FgdTokenizer tokens, FgdVariable variable, FgdVariableType type)
    {
        var hasDefaults = type is FgdVariableType.Flags or FgdVariableType.TagList or FgdVariableType.TagListDynamic;

        variable.Choices = [];

        while (true)
        {
            if (!tokens.TryPeek(0, out var next))
            {
                Error(tokens, "Unexpected end of input");
                return false;
            }

            if (!next.IsInteger && !next.IsString)
            {
                break;
            }

            tokens.TryRead(out var valueToken);

            var valueText = valueToken.Unquote();

            if (!tokens.Accept(":"))
            {
                Error(tokens, "Expecting ':'");
                return false;
            }

            var caption = ParseText(tokens);

            if (!hasDefaults)
            {
                var choiceDescription = string.Empty;

                if (tokens.Accept(":"))
                {
                    choiceDescription = ParseText(tokens);
                }

                variable.Choices.Add(new FgdChoice { ValueText = valueText, Caption = caption, Description = choiceDescription });
                continue;
            }

            var isFlags = type == FgdVariableType.Flags;

            // Files written for other editors can leave out whether a flag is on
            var isDefault = false;

            if (tokens.Accept(":"))
            {
                if (!tokens.TryRead(out var defaultToken) || !defaultToken.IsInteger)
                {
                    Error(tokens, isFlags
                        ? "Expected an integer default value after <description> ':'"
                        : "Expected an integer default value after <friendlyName> ':'");
                    return false;
                }

                isDefault = LeadingNumber.ParseInteger(defaultToken.Span) != 0;
            }
            else if (!isFlags)
            {
                Error(tokens, "Expecting ':'");
                return false;
            }

            var description = string.Empty;

            if (tokens.Accept(":"))
            {
                description = ParseText(tokens);
            }

            if (isFlags)
            {
                var value = LeadingNumber.ParseInteger(valueText);

                variable.Choices.Add(new FgdChoice
                {
                    Value = value,
                    ValueText = value.ToString(CultureInfo.InvariantCulture),
                    Caption = caption,
                    Description = description,
                    IsDefault = isDefault,
                });
            }
            else
            {
                // A tag is listed once, and an empty one not at all
                if (valueText.Length == 0 || variable.Choices.Exists(choice => choice.ValueText.Equals(valueText, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                variable.Choices.Add(new FgdChoice
                {
                    ValueText = valueText,
                    Caption = caption,
                    Description = description,
                    IsDefault = isDefault,
                });
            }
        }

        if (hasDefaults)
        {
            // The default of a set is the values that are on
            var separator = type == FgdVariableType.Flags ? "|" : ",";

            variable.DefaultInteger = 0;
            variable.DefaultString = string.Join(separator, variable.Choices.Where(static choice => choice.IsDefault).Select(static choice => choice.ValueText));
            variable.HasDefault = true;
        }

        return true;
    }

    // A default that is none of the values can be the name one of them is shown under
    private void CheckDefaultChoice(FgdTokenizer tokens, FgdVariable variable, FgdVariableType type)
    {
        if (type is not (FgdVariableType.Choices or FgdVariableType.IntChoices or FgdVariableType.FloatChoices) || !variable.HasDefault)
        {
            return;
        }

        var storage = GetStorage(variable.DeclaredType);
        var text = storage == FgdStorage.Integer
            ? variable.DefaultInteger.ToString(CultureInfo.InvariantCulture)
            : variable.DefaultString;

        if (variable.Choices.Exists(choice => choice.ValueText == text))
        {
            return;
        }

        if (variable.Choices.Find(choice => choice.Caption == text) is not { } named)
        {
            Error(tokens, $"Variable {variable.Name} has bad default value {text} (no choices with that name or value)");
            return;
        }

        if (storage == FgdStorage.Integer)
        {
            SetDefaultInteger(variable, LeadingNumber.ParseInteger(named.ValueText));
        }
        else if (storage == FgdStorage.String)
        {
            variable.DefaultString = named.ValueText;
        }
    }

    // The text of the default is the number that is kept, so it reads the same as the default of a text variable
    private static void SetDefaultInteger(FgdVariable variable, int value)
    {
        variable.DefaultInteger = value;
        variable.DefaultString = value.ToString(CultureInfo.InvariantCulture);
    }
}
