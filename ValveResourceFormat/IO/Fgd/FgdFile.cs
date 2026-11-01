using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using ValveKeyValue;
using ValveResourceFormat.Serialization.KeyValues;
using ValveResourceFormat.Utils;
using Token = ValveResourceFormat.IO.Fgd.FgdTokenizer.Token;

namespace ValveResourceFormat.IO.Fgd;

/// <summary>
/// The classes a game declares for its tools in FGD files: the entity classes of the
/// map editor with their variables, inputs, outputs and the helpers they are drawn with. Files are read the
/// way Hammer reads them.
/// </summary>
public sealed partial class FgdFile
{
    private const string BreakCharacters = " \t\n\r@()[]{}=&,:*|;'!";
    private const int MaxIncludeDepth = 64;

    private static readonly (string Name, FgdInputOutputType Type)[] InputOutputTypeNames =
    [
        ("void", FgdInputOutputType.Void),
        ("integer", FgdInputOutputType.Integer),
        ("bool", FgdInputOutputType.Boolean),
        ("boolean", FgdInputOutputType.Boolean),
        ("string", FgdInputOutputType.String),
        ("float", FgdInputOutputType.Float),
        ("vector", FgdInputOutputType.Vector),
        ("target_destination", FgdInputOutputType.TargetDestination),
        ("color255", FgdInputOutputType.Color255),
        ("ehandle", FgdInputOutputType.TargetDestination),
        ("script", FgdInputOutputType.Script),
    ];

    // Names a variable type could go by before it became a kind of resource
    private static readonly (string Name, FgdVariableType Type, string SubType)[] TypeAliases =
    [
        ("decal", FgdVariableType.Resource, "vmat"),
        ("sprite", FgdVariableType.Resource, "vmat"),
        ("studio", FgdVariableType.Resource, "vmdl"),
        ("material", FgdVariableType.Resource, "vmat"),
        ("particlesystem", FgdVariableType.Resource, "vpcf"),
        ("ehandle", FgdVariableType.TargetDestination, string.Empty),
    ];

    private static readonly string[] VisGroupFilterTypes = ["toolsMaterial", "entityTag", "assetTag"];

    private static readonly (string Name, string Extension)[] ResourceSubTypes =
    [
        ("texture", "vtex"),
        ("material", "vmat"),
        ("mesh", "vmesh"),
        ("particle", "vpcf"),
        ("model", "vmdl"),
        ("collisionmesh", "vphzc"),
        ("sky", "vsky"),
        ("map", "vmap"),
        ("postprocessing", "vpost"),
        ("snapshot", "vsnap"),
    ];

    private readonly Func<string, Stream?>? openFile;

    private readonly Dictionary<string, FgdClass> classesByName = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<FgdClass> extensions = [];

    /// <summary>Gets the classes, in the order they are declared.</summary>
    public List<FgdClass> Classes { get; } = [];

    /// <summary>Gets the groups entity classes are listed under.</summary>
    public List<FgdEntityGroup> EntityGroups { get; } = [];

    /// <summary>Gets the grid a game lays its navigation out on, where a file declares one.</summary>
    public (int EdgeSize, int OffsetX, int OffsetY, int PickHeight)? GridNav { get; private set; }

    /// <summary>Gets the bounds of a map along each axis, where a file of an older game declares them.</summary>
    public (int Low, int High)? MapSize { get; private set; }

    /// <summary>Gets the filters a file declares for what is shown of a map, as the keys they are declared with.</summary>
    public List<KVObject> VisGroupFilters { get; } = [];

    /// <summary>Gets the material folders the material browser leaves out, where a file of an older game declares them.</summary>
    public List<string> MaterialExclusions { get; } = [];

    /// <summary>Gets the groups that show or hide entities by their class, where a file of an older game declares them.</summary>
    public List<FgdAutoVisGroup> AutoVisGroups { get; } = [];

    /// <summary>Gets the help file of each class that names one, by the name of the class.</summary>
    public Dictionary<string, string> HelpFiles { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Gets the version a file declares for the editor to check it against, where one does.</summary>
    public int? Version { get; private set; }

    /// <summary>
    /// Gets what could not be read. Reading carries on past it at the next section, or the next key of a class
    /// where only the key is wrong, so everything else of a file is still read.
    /// </summary>
    public List<FgdError> Errors { get; } = [];

    /// <summary>
    /// Initializes classes that find the files they read on disk: a file named without a folder is looked for
    /// next to the file that includes it, and then in the current folder.
    /// </summary>
    public FgdFile()
    {
    }

    /// <summary>
    /// Initializes classes that open the files named without a folder through a file system first, such as
    /// the one a game mounts, before they are looked for on disk.
    /// </summary>
    /// <param name="openFile">Opens a file by its name, or gives <see langword="null"/> where there is none.</param>
    public FgdFile(Func<string, Stream?> openFile)
    {
        ArgumentNullException.ThrowIfNull(openFile);

        this.openFile = openFile;
    }

    /// <summary>
    /// Gets or sets the classes <see cref="FindClass"/> looks in for a class none is declared under here.
    /// Classes are still only based on the classes of their own FGD files.
    /// </summary>
    public FgdFile? Fallback { get; set; }

    /// <summary>
    /// Finds a class by its name, without regard to case, here and then in <see cref="Fallback"/>.
    /// </summary>
    /// <param name="name">Name of the class.</param>
    /// <returns>The class, or <see langword="null"/> when none is declared under that name.</returns>
    public FgdClass? FindClass(string name)
        => classesByName.GetValueOrDefault(name) ?? Fallback?.FindClass(name);

    /// <summary>
    /// Reads a file and the files it includes. Classes are not given what they take from the classes they
    /// are based on until <see cref="ResolveBaseClasses"/> is called, once every file is read.
    /// </summary>
    /// <param name="fileName">The file, as a path or as a name to open through the file system of the classes.</param>
    /// <returns>Whether the file was read without errors.</returns>
    public bool Load(string fileName)
    {
        var errorCount = Errors.Count;

        return LoadFile(fileName, depth: 0) && Errors.Count == errorCount;
    }

    /// <summary>Reads FGD text.</summary>
    /// <param name="text">The text of an FGD file.</param>
    /// <param name="fileName">Name the text is reported under.</param>
    /// <returns>Whether the text was read without errors.</returns>
    public bool LoadText(string text, string fileName)
    {
        ArgumentNullException.ThrowIfNull(text);

        var errorCount = Errors.Count;

        ParseFile(new FgdTokenizer(text, fileName, BreakCharacters), depth: 0);
        return Errors.Count == errorCount;
    }

    /// <summary>
    /// Adds the <see cref="FgdClassType.ExtendClass"/> classes read so far to the classes they extend, then
    /// gives every class what it takes from the classes it is based on: their variables, inputs and outputs,
    /// their tags and keys, and their size and color where it declares none. Helpers are not taken. It can be
    /// called again after more files are read: what a class has by then counts as its own, so it keeps the
    /// variables it already took over the ones its bases have since changed, and gains the ones they added.
    /// </summary>
    public void ResolveBaseClasses()
    {
        foreach (var extension in extensions)
        {
            if (classesByName.GetValueOrDefault(extension.Name) is { } existing)
            {
                Extend(existing, extension);
            }
        }

        extensions.Clear();

        foreach (var gameClass in Classes)
        {
            var variables = gameClass.Variables;
            var inputs = gameClass.Inputs;
            var outputs = gameClass.Outputs;

            gameClass.Variables = [];
            gameClass.Inputs = [];
            gameClass.Outputs = [];

            foreach (var baseClass in gameClass.BaseClasses)
            {
                AddBase(gameClass, baseClass);
            }

            foreach (var variable in variables)
            {
                gameClass.AddVariable(variable);
            }

            AddOwn(gameClass.Inputs, inputs);
            AddOwn(gameClass.Outputs, outputs);
        }
    }

    /// <summary>Lists the classes with what they declare, and what could not be read.</summary>
    /// <returns>The listing.</returns>
    public override string ToString()
    {
        var builder = new StringBuilder();

        foreach (var error in Errors)
        {
            builder.AppendLine(CultureInfo.InvariantCulture, $"{error.FileName}({error.Line}): {error.Message}");
        }

        foreach (var gameClass in Classes)
        {
            builder.Append(CultureInfo.InvariantCulture, $"@{gameClass.Type}");

            if (gameClass.BaseClasses.Count > 0)
            {
                builder.Append(CultureInfo.InvariantCulture, $" base({string.Join(", ", gameClass.BaseClasses.Select(static baseClass => baseClass.Name))})");
            }

            foreach (var helper in gameClass.Helpers)
            {
                builder.Append(CultureInfo.InvariantCulture, $" {helper.Name}({string.Join(", ", helper.Arguments)})");
            }

            builder.AppendLine(CultureInfo.InvariantCulture, $" = {gameClass.Name}");

            foreach (var variable in gameClass.Variables)
            {
                builder.AppendLine(CultureInfo.InvariantCulture, $"	{variable.Name}({variable.TypeName})");
            }

            foreach (var input in gameClass.Inputs)
            {
                builder.AppendLine(CultureInfo.InvariantCulture, $"	input {input.Name}({(input.IsApi ? "api" : input.Type.ToString().ToLowerInvariant())})");
            }

            foreach (var output in gameClass.Outputs)
            {
                builder.AppendLine(CultureInfo.InvariantCulture, $"	output {output.Name}({(output.IsApi ? "api" : output.Type.ToString().ToLowerInvariant())})");
            }
        }

        return builder.ToString();
    }

    private static void AddOwn(List<FgdInputOutput> all, List<FgdInputOutput> own)
    {
        foreach (var inputOutput in own)
        {
            if (FindByName(all, inputOutput.Name) is { } inherited)
            {
                inherited.CopyFrom(inputOutput);
            }
            else
            {
                all.Add(inputOutput);
            }
        }
    }

    private static void AddBase(FgdClass gameClass, FgdClass baseClass)
    {
        foreach (var variable in baseClass.Variables)
        {
            gameClass.AddVariable(variable);
        }

        AddMissing(gameClass.Inputs, baseClass.Inputs);
        AddMissing(gameClass.Outputs, baseClass.Outputs);

        if (!gameClass.HasSize && baseClass.HasSize)
        {
            gameClass.Mins = baseClass.Mins;
            gameClass.Maxs = baseClass.Maxs;
            gameClass.HasSize = true;
        }

        // The first class it is based on gives the color, even where that one never declared any
        if (!gameClass.HasColor)
        {
            gameClass.Color = baseClass.Color;
            gameClass.HasColor = true;
        }

        foreach (var tag in baseClass.Tags)
        {
            gameClass.AddTag(tag);
        }

        AddMissingKeys(gameClass.Metadata, baseClass.Metadata);
    }

    private static void AddMissing(List<FgdInputOutput> all, List<FgdInputOutput> inherited)
    {
        foreach (var inputOutput in inherited)
        {
            if (FindByName(all, inputOutput.Name) == null)
            {
                var copy = new FgdInputOutput();
                copy.CopyFrom(inputOutput);
                all.Add(copy);
            }
        }
    }

    private static FgdInputOutput? FindByName(List<FgdInputOutput> all, string name)
    {
        foreach (var inputOutput in all)
        {
            if (inputOutput.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                return inputOutput;
            }
        }

        return null;
    }

    private static void AddMissingKeys(KVObject keys, KVObject other)
    {
        foreach (var (key, value) in other)
        {
            if (!keys.ContainsKey(key))
            {
                keys.Add(key, value);
            }
        }
    }

    // A name is opened through the file system first, then looked for next to the file that includes it, and
    // then in the current folder
    private Stream? OpenFile(string fileName, string? includedFrom, out string path)
    {
        path = fileName;

        if (!Path.IsPathRooted(fileName))
        {
            if (openFile?.Invoke(fileName) is { } stream)
            {
                return stream;
            }

            if (includedFrom != null && Path.GetDirectoryName(includedFrom) is { Length: > 0 } directory && File.Exists(Path.Join(directory, fileName)))
            {
                path = Path.Join(directory, fileName);
            }
        }

        return File.Exists(path) ? File.OpenRead(path) : null;
    }

    private bool Exists(string fileName)
    {
        using var stream = OpenFile(fileName, includedFrom: null, out _);

        return stream != null;
    }

    private bool LoadFile(string fileName, int depth, string? includedFrom = null)
    {
        if (depth > MaxIncludeDepth)
        {
            return false;
        }

        using var stream = OpenFile(fileName, includedFrom, out var path);

        if (stream == null)
        {
            if (depth == 0)
            {
                Errors.Add(new FgdError(fileName, 0, $"Unable to find fgd file {fileName}!"));
            }

            return false;
        }

        using var reader = new StreamReader(stream);

        ParseFile(new FgdTokenizer(reader.ReadToEnd(), path, BreakCharacters), depth);
        return true;
    }

    private void Error(FgdTokenizer tokens, string message)
        => Errors.Add(new FgdError(tokens.FileName, tokens.Line, message));

    // A section that cannot be read is reported and given up on, and reading carries on at the next one
    private void ParseFile(FgdTokenizer tokens, int depth)
    {
        while (tokens.HasMore)
        {
            if (!ParseSection(tokens, depth) && !tokens.SkipTo("@"))
            {
                return;
            }
        }
    }

    private bool ParseSection(FgdTokenizer tokens, int depth)
    {
        if (!tokens.Accept("@"))
        {
            Error(tokens, "Expected '@'");
            return false;
        }

        if (!tokens.TryReadIdentifier(out var section))
        {
            Error(tokens, "Expected identifier after '@'");
            return false;
        }

        // A class is declared in the section named after its type. A name never starts with a digit, so no number
        // is taken for a type
        if (Enum.TryParse<FgdClassType>(section.Span, ignoreCase: true, out var classType))
        {
            return ParseClassDeclaration(tokens, classType);
        }

        return section.Text.ToLowerInvariant() switch
        {
            "exclude" => ParseExclude(tokens),
            "include" => ParseInclude(tokens, depth),
            // Only the files of older games have these sections
            "mapsize" => ParseMapSize(tokens),
            "materialexclusion" => ParseMaterialExclusion(tokens),
            "autovisgroup" => ParseAutoVisGroup(tokens),
            "helpinfo" => ParseHelpInfo(tokens),
            "version" => ParseVersion(tokens),
            "gridnav" => ParseGridNav(tokens),
            "visgroupfilter" => ParseVisGroupFilter(tokens),
            "entitygroup" => ParseEntityGroup(tokens),
            _ => UnrecognizedSection(tokens, section),
        };
    }

    private bool UnrecognizedSection(FgdTokenizer tokens, Token section)
    {
        Error(tokens, $"Unrecognized section name {section.Text}");
        return false;
    }

    private bool ParseInclude(FgdTokenizer tokens, int depth)
    {
        if (!tokens.TryRead(out var include) || !include.IsString)
        {
            Error(tokens, "Expected string literal for include directive");
            return false;
        }

        var includeName = include.Unquote();

        if (!LoadFile(includeName, depth + 1, tokens.FileName))
        {
            Error(tokens, $"Error including file: {includeName}");
        }

        return true;
    }

    private bool ParseClassDeclaration(FgdTokenizer tokens, FgdClassType type)
    {
        var gameClass = new FgdClass();

        if (!ParseClass(tokens, gameClass))
        {
            return false;
        }

        gameClass.Type = type;

        if (type == FgdClassType.ExtendClass)
        {
            extensions.Add(gameClass);
            return true;
        }

        switch (type)
        {
            case FgdClassType.PointClass:
                gameClass.AddTag("point");
                break;
            case FgdClassType.SolidClass:
                gameClass.AddTag("solid");
                break;
            case FgdClassType.NpcClass:
                gameClass.AddTag("point");
                gameClass.AddTag("npc");
                break;
            case FgdClassType.FilterClass:
                gameClass.AddTag("filter");
                break;
            case FgdClassType.MoveClass:
                gameClass.AddTag("move");
                break;
            case FgdClassType.KeyFrameClass:
                gameClass.AddTag("keyframe");
                break;
            case FgdClassType.PathClass:
                gameClass.AddTag("path");
                break;
            case FgdClassType.PathNodeClass:
                gameClass.AddTag("pathnode");
                break;
            case FgdClassType.CableClass:
                gameClass.AddTag("cable");
                break;
            case FgdClassType.NavLinkClass:
                gameClass.AddTag("navlink");
                break;
        }

        var existing = classesByName.GetValueOrDefault(gameClass.Name);

        if (existing != null)
        {
            if (type == FgdClassType.OverrideClass)
            {
                Override(existing, gameClass);
                return true;
            }

            Error(tokens, $"Error, duplicate class '{gameClass.Name}'. Use @OverrideClass to add keys to an existing class.");
            return true;
        }

        if (type == FgdClassType.OverrideClass)
        {
            Error(tokens, $"'OverrideClass' was specified but class '{gameClass.Name}' has not been previously defined.");
            return true;
        }

        Classes.Add(gameClass);
        classesByName[gameClass.Name] = gameClass;
        return true;
    }

    // An override adds to a class that exists, replacing what it declares again
    private static void Override(FgdClass existing, FgdClass overriding)
    {
        foreach (var helper in overriding.Helpers)
        {
            // A helper an override declares reads its block of keys, which is empty where it was given arguments
            helper.Metadata ??= new KVObject();

            if (existing.Helpers.Find(other => other.Name.Equals(helper.Name, StringComparison.OrdinalIgnoreCase)) is { } replaced)
            {
                replaced.Metadata = helper.Metadata;
                replaced.MetadataText = helper.MetadataText;
                replaced.Arguments = [.. helper.Arguments];
            }
            else
            {
                existing.Helpers.Add(helper);
            }
        }

        ReplaceVariables(existing, overriding.Variables);
        AddOwn(existing.Inputs, overriding.Inputs);
        AddOwn(existing.Outputs, overriding.Outputs);

        if (overriding.HasColor)
        {
            existing.Color = overriding.Color;
        }

        if (overriding.HasSize)
        {
            existing.Mins = overriding.Mins;
            existing.Maxs = overriding.Maxs;
        }

        if (overriding.Description.Length > 0)
        {
            existing.Description = overriding.Description;
        }

        foreach (var tag in overriding.Tags)
        {
            existing.AddTag(tag);
        }

        AddMissingKeys(overriding.Metadata, existing.Metadata);
        existing.Metadata = overriding.Metadata;
    }

    // An extension adds every helper unless one of the same name has the same arguments, and replaces the
    // variables it declares again
    private static void Extend(FgdClass existing, FgdClass extension)
    {
        foreach (var helper in extension.Helpers)
        {
            var exists = existing.Helpers.Exists(other => other.Name.Equals(helper.Name, StringComparison.OrdinalIgnoreCase)
                && other.Arguments.SequenceEqual(helper.Arguments, StringComparer.OrdinalIgnoreCase));

            if (!exists)
            {
                existing.Helpers.Add(helper);
            }
        }

        ReplaceVariables(existing, extension.Variables);
    }

    // The variables declared again replace the ones of the same name, or remove them where they are declared
    // with remove_key
    private static void ReplaceVariables(FgdClass existing, List<FgdVariable> variables)
    {
        foreach (var variable in variables)
        {
            var index = existing.IndexOfVariable(variable.Name);

            if (index < 0)
            {
                existing.AddVariable(variable);
            }
            else if (variable.DeclaredType == FgdVariableType.RemoveKey)
            {
                existing.Variables.RemoveAt(index);
            }
            else
            {
                existing.Variables[index] = variable;
            }
        }
    }

    private bool ParseExclude(FgdTokenizer tokens)
    {
        if (!tokens.TryReadIdentifier(out var name))
        {
            Error(tokens, "Expecting class name following @exclude");
            return false;
        }

        // Unlike everywhere else, the name has to be written the way the class is declared
        var index = Classes.FindIndex(gameClass => gameClass.Name.Equals(name.Text, StringComparison.Ordinal));

        if (index < 0)
        {
            Error(tokens, $"Unknown class '{name.Text}' specified by @exclude");
            return true;
        }

        if (Classes[index].Type == FgdClassType.BaseClass)
        {
            Error(tokens, $"Specified class '{name.Text}' is a base class, @exclude cannot be used for base classes");
            return false;
        }

        Classes.RemoveAt(index);
        classesByName.Remove(name.Text);
        return true;
    }

    private bool ParseGridNav(FgdTokenizer tokens)
    {
        var values = new Token[4];
        var read = tokens.Accept("(");

        for (var i = 0; read && i < values.Length; i++)
        {
            read = tokens.TryRead(out values[i]) && tokens.Accept(i == values.Length - 1 ? ")" : ",");
        }

        if (!read || !values.All(static value => value.IsInteger))
        {
            Error(tokens, "Expecting '(' <edgesize> ',' <offsetx> ',' <offsety> ',' <pickheight> ')'");
            return false;
        }

        GridNav = (LeadingNumber.ParseInteger(values[0].Span), LeadingNumber.ParseInteger(values[1].Span), LeadingNumber.ParseInteger(values[2].Span), LeadingNumber.ParseInteger(values[3].Span));
        return true;
    }

    private bool ParseMapSize(FgdTokenizer tokens)
    {
        if (!tokens.Accept("(") || !tokens.TryRead(out var low) || !tokens.Accept(",") || !tokens.TryRead(out var high) || !tokens.Accept(")")
            || !low.IsInteger || !high.IsInteger)
        {
            Error(tokens, "Expecting '(' <min> ',' <max> ')'");
            return false;
        }

        MapSize = (LeadingNumber.ParseInteger(low.Span), LeadingNumber.ParseInteger(high.Span));
        return true;
    }

    // ( "class", "help file" )
    private bool ParseHelpInfo(FgdTokenizer tokens)
    {
        if (!tokens.Accept("(") || !tokens.TryReadString(out var className) || !tokens.Accept(",")
            || !tokens.TryReadString(out var helpFile) || !tokens.Accept(")"))
        {
            Error(tokens, "Expecting '(' <class> ',' <help file> ')'");
            return false;
        }

        HelpFiles[className.Unquote()] = helpFile.Unquote();
        return true;
    }

    private bool ParseVersion(FgdTokenizer tokens)
    {
        if (!tokens.Accept("(") || !tokens.TryRead(out var version) || !version.IsInteger || !tokens.Accept(")"))
        {
            Error(tokens, "Expecting '(' <version> ')'");
            return false;
        }

        Version = LeadingNumber.ParseInteger(version.Span);
        return true;
    }

    // [ "string" "string" ... ]
    private bool ParseStringList(FgdTokenizer tokens, out List<string> strings)
    {
        strings = [];

        if (!tokens.Accept("["))
        {
            Error(tokens, "Expecting '['");
            return false;
        }

        while (tokens.TryReadString(out var text))
        {
            strings.Add(text.Unquote());
        }

        if (!tokens.Accept("]"))
        {
            Error(tokens, "Expecting ']'");
            return false;
        }

        return true;
    }

    private bool ParseMaterialExclusion(FgdTokenizer tokens)
    {
        if (!ParseStringList(tokens, out var folders))
        {
            return false;
        }

        foreach (var folder in folders)
        {
            if (!MaterialExclusions.Contains(folder, StringComparer.OrdinalIgnoreCase))
            {
                MaterialExclusions.Add(folder);
            }
        }

        return true;
    }

    // = "parent" [ "name" [ "class" "class" ... ] "name" [ ... ] ]
    private bool ParseAutoVisGroup(FgdTokenizer tokens)
    {
        if (!tokens.Accept("=") || !tokens.TryReadString(out var parent) || !tokens.Accept("["))
        {
            Error(tokens, "Expecting '=' <parent> '['");
            return false;
        }

        var parentName = parent.Unquote();

        while (tokens.TryReadString(out var name))
        {
            if (!ParseStringList(tokens, out var classes))
            {
                return false;
            }

            AutoVisGroups.Add(new FgdAutoVisGroup(parentName, name.Unquote(), classes));
        }

        if (!tokens.Accept("]"))
        {
            Error(tokens, "Expecting ']'");
            return false;
        }

        return true;
    }

    private bool ParseVisGroupFilter(FgdTokenizer tokens)
    {
        if (!ParseBlock(tokens, out var filter, out _))
        {
            return false;
        }

        var filterType = filter.ContainsKey("filter_type") ? filter.GetStringProperty("filter_type") : null;

        if (string.IsNullOrEmpty(filterType))
        {
            Error(tokens, "Error: missing 'filter_type' key in @VisGroupFilter specification");
            return false;
        }

        if (!Array.Exists(VisGroupFilterTypes, type => type.Equals(filterType, StringComparison.OrdinalIgnoreCase)))
        {
            Error(tokens, $"Error: unknown vis group filter type: '{filterType}' in @VisGroupFilter specification");
            return false;
        }

        if (!filter.ContainsKey("group") || string.IsNullOrEmpty(filter.GetStringProperty("group")))
        {
            Error(tokens, "Error: missing 'group' key in @VisGroupFilter specification");
            return false;
        }

        VisGroupFilters.Add(filter);
        return true;
    }

    private bool ParseEntityGroup(FgdTokenizer tokens)
    {
        if (!tokens.TryRead(out var name) || name.Unquote() is not { Length: > 0 } groupName)
        {
            Error(tokens, "Expecting group name following @entitygroup");
            return false;
        }

        var startExpanded = false;

        if (tokens.PeekIs(0, "{"))
        {
            if (!tokens.TryReadBlock("{", "}", out var text) || !tokens.HasMore)
            {
                Error(tokens, "Expected matching '{' '}'");
                return false;
            }

            if (!KVDocumentExtensions.TryParseKV3WithoutHeader(text, out var keys, out var problem))
            {
                Error(tokens, $"KV3 syntax error: {problem} while parsing @entitygroup {groupName}");
                return false;
            }

            startExpanded = keys.ContainsKey("start_expanded") && keys.GetBooleanProperty("start_expanded");
        }

        EntityGroups.Add(new FgdEntityGroup(groupName, startExpanded));
        return true;
    }

    // A block of keys in braces, in the syntax of KeyValues3 without its header
    private bool ParseBlock(FgdTokenizer tokens, out KVObject keys, out string text)
    {
        var line = tokens.Line;

        if (!tokens.TryReadBlock("{", "}", out text) || !tokens.HasMore)
        {
            keys = new KVObject();
            Error(tokens, string.Create(CultureInfo.InvariantCulture, $"Expected matching '{{' '}}' (started on line {line})"));
            return false;
        }

        if (!KVDocumentExtensions.TryParseKV3WithoutHeader(text, out keys, out var problem))
        {
            Error(tokens, $"KV3 syntax error: {problem}");
            return false;
        }

        return true;
    }

    private static Color32 ParseColor(Token red, Token green, Token blue)
        => new((byte)LeadingNumber.ParseInteger(red.Span), (byte)LeadingNumber.ParseInteger(green.Span), (byte)LeadingNumber.ParseInteger(blue.Span));
}
