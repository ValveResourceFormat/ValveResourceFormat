using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;

string CurrentFileName([CallerFilePath] string csFilePath = "")
{
    return csFilePath ?? throw new InvalidOperationException();
}

string WalkUpDir(string path, int levels)
{
    var currentPath = path;
    for (var i = 0; i < levels; i++)
    {
        currentPath = Path.GetDirectoryName(currentPath) ?? throw new InvalidOperationException("Cannot walk up any further.");
    }

    return currentPath;
}

HashSet<string> enumTypes = [];
Dictionary<string, string?> classHierarchies = [];

string RootClass(string @class)
{
    var rootParent = @class;
    while (classHierarchies.TryGetValue(rootParent, out var parent) && parent is not null)
    {
        rootParent = parent;
    }

    return rootParent;
}


void ConvertAnimLib()
{
    var sourceDir = @"C:/Users/USER/Downloads/animlib";
    var sourceNameSpace = "Nm";

    var destinationProject = WalkUpDir(CurrentFileName(), 3) + "/Renderer";
    var destinationFolder = "AnimLib";
    var destinationNameSpace = $"ValveResourceFormat.Renderer.{destinationFolder.Replace('/', '.')}";
    var destinationDir = destinationProject + "/" + destinationFolder;

    // create dir
    Directory.CreateDirectory(destinationDir);

    // clear folder
    foreach (var file in Directory.GetFiles(destinationDir, "*.cs"))
    {
        File.Delete(file);
    }

    bool IsRootEnum(string enumName)
    {
        return enumTypes.Contains(enumName);
    }

    string ChooseFolder(string fileName, string destinationDirLocal)
    {
        fileName = fileName.Split("__", StringSplitOptions.RemoveEmptyEntries)[0];

        if (IsRootEnum(fileName))
        {
            destinationDirLocal += "/Enums";
            Directory.CreateDirectory(destinationDirLocal);
        }

        Dictionary<string, string> classFolderMapping = new()
        {
            { "GraphNode", "Nodes"},
            { "Event", "Events" },
            { "PoseTask", "Tasks" },
        };

        if (classFolderMapping.TryGetValue(RootClass(fileName), out var folder))
        {
            destinationDirLocal += $"/{folder}";
            Directory.CreateDirectory(destinationDirLocal);
        }

        return destinationDirLocal;
    }

    // for each .h file
    var headerFiles = Directory.GetFiles(sourceDir, "*.h");

    // pre process
    foreach (var file in headerFiles)
    {
        using var reader = new StreamReader(file);
        string? line;

        var classRegex = ClassRegex();
        var enumRegex = EnumRegex();

        while ((line = reader.ReadLine()) != null)
        {
            line = line.Trim();

            // Skip empty lines and comments
            if (string.IsNullOrWhiteSpace(line) || line.StartsWith("//", StringComparison.Ordinal))
            {
                continue;
            }

            var match = classRegex.Match(line);
            if (match.Success)
            {
                var @class = match.Groups["identifier"].Value;
                var baseClass = match.Groups["baseIdentifier"].Success ? match.Groups["baseIdentifier"].Value : null;

                classHierarchies[ConvertClassName(@class, sourceNameSpace)] = baseClass is not null ? ConvertClassName(baseClass, sourceNameSpace) : null;
                continue;
            }

            match = enumRegex.Match(line);
            if (match.Success)
            {
                var identifier = match.Groups["identifier"].Value;
                enumTypes.Add(ConvertClassName(identifier, sourceNameSpace));
                continue;
            }
        }
    }

    // dump
    foreach (var file in headerFiles)
    {
        var fileName = Path.GetFileNameWithoutExtension(file);
        fileName = ConvertClassName(fileName, sourceNameSpace);

        var isEnum = enumTypes.Contains(fileName);

        var destinationDirLocal = ChooseFolder(fileName, destinationDir);

        var destFile = Path.Combine(destinationDirLocal, fileName + ".cs");

        using var reader = new StreamReader(file);
        using var writer = new StreamWriter(destFile);

        if (!isEnum)
        {
            writer.WriteLine($"using ValveResourceFormat.Serialization.KeyValues;"); // global using?
        }
        writer.WriteLine($"namespace {destinationNameSpace};");
        writer.WriteLine();

        ConvertSchemaOutputToCsharp(reader, writer, sourceNameSpace);
        Console.WriteLine($"Created {Path.GetFileName(destFile)}");
    }

    // build project
    Process.Start("dotnet", $"build \"{destinationProject}\" /p:NoWarn=CA1812")?.WaitForExit();
}

var matchingTypes = new HashSet<string>()
{
    "bool", "float", "double",
};

var simpleTypeMap = new Dictionary<string, string>()
{
    { "uint8_t", "byte" },
    { "uint8", "byte" },
    { "int8_t", "sbyte" },
    { "uint16_t", "ushort" },
    { "uint16", "ushort" },
    { "int16_t", "short" },
    { "int16", "short" },
    { "uint32_t", "uint" },
    { "int32_t", "int" },
    { "int32", "int" },
    { "uint32", "uint" },
    { "float32", "float" },
    { "uint64_t", "ulong" },
    { "int64_t", "long" },

    { "CGlobalSymbol", "GlobalSymbol" },
    { "CUtlStringToken", "GlobalSymbol" },
    { "CUtlString", "string" },
    { "CUtlBinaryBlock", "byte[]" },
    { "Vector2D", "Vector2" },
    { "Vector", "Vector3" },
    { "CTransform", "Transform" },
    { "CResourceName", "string" },

    { "ParticleAttachment_t", "Particles.ParticleAttachment" },
    { "CPiecewiseCurve", "Particles.Utils.PiecewiseCurve" },
    { "KeyValues3", "KVObject" },

    // todo
    { "CStrongHandle", "string" },
    { "CStrongHandleVoid", "string" },
};

/*
    CNmVelocityBlendNode::CDefinition -> VelocityBlendNode__Definition
*/
string ConvertClassName(string cStyleClassName, string cStyleNamespacePreffix = "")
{
    if (matchingTypes.Contains(cStyleClassName))
    {
        return cStyleClassName;
    }

    if (simpleTypeMap.TryGetValue(cStyleClassName, out var simpleType))
    {
        return simpleType;
    }

    cStyleClassName = cStyleClassName.Trim();
    cStyleClassName = cStyleClassName.Replace("::", "__", StringComparison.Ordinal);

    var sb = new System.Text.StringBuilder(cStyleClassName.Length);
    var parts = cStyleClassName.Split("__", StringSplitOptions.RemoveEmptyEntries);
    foreach (var part in parts)
    {
        var cleanPart = part;

        if (cleanPart.StartsWith('C') && cleanPart.Length > 1 && char.IsUpper(cleanPart[1]))
        {
            cleanPart = cleanPart[1..];
        }

        if (cleanPart.StartsWith(cStyleNamespacePreffix, StringComparison.Ordinal))
        {
            cleanPart = cleanPart[cStyleNamespacePreffix.Length..];
        }

        if (cleanPart.EndsWith("_t", StringComparison.Ordinal))
        {
            cleanPart = cleanPart[..^2];
        }

        if (sb.Length > 0)
        {
            sb.Append("__");
        }

        sb.Append(cleanPart);
    }

    var clean = sb.ToString();
    clean = clean.Replace("__Definition", "");
    return clean;
}

string ConvertHungarianNotation(string name)
{
    var stripInitial = "m_";
    Span<string> stripType = ["n", "b", "fl", "h", "s", "sz"];

    var newName = name;

    if (newName.StartsWith(stripInitial, StringComparison.Ordinal))
    {
        newName = newName[stripInitial.Length..];
    }

    foreach (var type in stripType)
    {
        if (newName.StartsWith(type, StringComparison.Ordinal) && newName.Length > type.Length && char.IsUpper(newName[type.Length]))
        {
            newName = newName[type.Length..];
            break;
        }
    }

    // capitalize first letter
    if (newName.Length > 0)
    {
        newName = char.ToUpper(newName[0], CultureInfo.InvariantCulture) + newName[1..];
    }

    return newName;
}

/*
    class CNmVelocityBlendNode::CDefinition : public CNmParameterizedBlendNode::CDefinition
    {
    };
    ------->
    class VelocityBlendNode__Definition : ParameterizedBlendNode__Definition
    {
    }
*/
void ConvertSchemaOutputToCsharp(StreamReader reader, StreamWriter writer, string cStyleNamespacePreffix = "")
{
    string? line;

    var classRegex = ClassRegex();
    var enumRegex = EnumRegex();
    var memberTemplateRegex = PropertyTypeRegex();

    var writeEnum = false;
    var writeClassMembers = false;
    var convertedClass = string.Empty;
    var hasBaseClass = false;
    List<string> memberParserLines = [];

    while ((line = reader.ReadLine()) != null)
    {
        var rawLine = line;
        line = line.Trim();

        // Skip empty lines and comments
        if (string.IsNullOrWhiteSpace(line) || line.StartsWith("//", StringComparison.Ordinal))
        {
            continue;
        }

        var match = classRegex.Match(line);
        if (match.Success)
        {
            var @class = match.Groups["identifier"].Value;
            var baseClass = match.Groups["baseIdentifier"].Success ? match.Groups["baseIdentifier"].Value : null;

            convertedClass = ConvertClassName(@class, cStyleNamespacePreffix);

            var isFinal = classHierarchies.ContainsValue(convertedClass) == false;
            hasBaseClass = classHierarchies.TryGetValue(convertedClass, out var baseClassName) && baseClassName is not null;

            var useStruct = false; //isFinal && !hasBaseClass;
            var partialImplementation = RootClass(convertedClass) == "GraphNode";
            var csClassType = useStruct ? "readonly partial struct" : (partialImplementation ? "partial class" : "class");

            var csClass = $"{csClassType} {convertedClass}";
            if (!string.IsNullOrEmpty(baseClass))
            {
                csClass += $" : {ConvertClassName(baseClass, cStyleNamespacePreffix)}";
                hasBaseClass = true;
            }
            writer.WriteLine(csClass);
            writer.WriteLine("{");
            writeClassMembers = true;
            continue;
        }

        match = enumRegex.Match(line);
        if (match.Success)
        {
            var identifier = match.Groups["identifier"].Value;
            var baseType = match.Groups["baseType"].Success ? match.Groups["baseType"].Value : null;

            var csEnum = $"enum {ConvertClassName(identifier, cStyleNamespacePreffix)}";
            if (!string.IsNullOrEmpty(baseType))
            {
                csEnum += $" : {ConvertClassName(baseType)}";
            }

            writer.WriteLine(csEnum);
            writeEnum = true;
            continue;
        }

        // todo: move enums to /Enums/ folder?
        if (writeEnum)
        {
            writer.WriteLine(rawLine == "};" ? "}" : rawLine.Replace("\t", "    "));
            continue;
        }

        if (writeClassMembers)
        {
            if (line == "};")
            {
                if (memberParserLines.Count > 0)
                {
                    // kvobject constructor
                    writer.WriteLine();

                    var baseCtor = hasBaseClass ? $" : base(data)" : string.Empty;
                    writer.WriteLine($"    public {convertedClass}(KVObject data){baseCtor}");
                    writer.WriteLine("    {");
                    foreach (var __line in memberParserLines)
                    {
                        writer.WriteLine($"        {__line}");
                    }
                    writer.WriteLine("    }");
                }
                else
                {
                    writer.WriteLine(hasBaseClass
                        ? $"    public {convertedClass}(KVObject data) : base(data) {{ }}"
                        : $"    public {convertedClass}(KVObject _) {{ }}"
                    );
                }
                writer.WriteLine("}");
                writeClassMembers = false;
                continue;
            }

            // member line
            // e.g.   uint32_t m_nSomeValue;
            var semicolonIndex = line.IndexOf(';', StringComparison.Ordinal);
            if (semicolonIndex < 0)
            {
                continue;
            }

            var memberLine = line[..semicolonIndex].Trim();

            match = memberTemplateRegex.Match(memberLine);
            if (match.Success)
            {
                var type = match.Groups["type"].Value; // CStrongHandle
                var args = match.Groups["args"].Value; // InfoForResourceTypeIParticleSystemDefinition
                var name = match.Groups["name"].Value; // m_hParticleSystem
                // args can be split by ',' for multiple template arguments

                // template types
                if (type is "CUtlLeanVector" or "CUtlLeanVectorFixedGrowable" or "CUtlVector" or "CUtlVectorFixedGrowable")
                {
                    var templateArgs = args.Split(',', 2, StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
                    var itemType = ConvertClassName(templateArgs[0], cStyleNamespacePreffix);
                    type = $"{itemType}[]";
                    args = null;
                }

                var newName = ConvertHungarianNotation(name);

                // convert type
                var csType = ConvertClassName(type, cStyleNamespacePreffix);

                var argComment = args is not null && args.Length > 0 ? $" // {args}" : string.Empty;

                // write property
                writer.WriteLine($"    public {csType} {newName} {{ get; }}{argComment}");

                var hasHandWrittenImpl = csType is "Transform" or "Range";

                if (classHierarchies.ContainsKey(csType) || hasHandWrittenImpl)
                {
                    memberParserLines.Add($"{newName} = new(data.GetProperty<KVObject>(\"{name}\"));");
                    continue;
                }

                if (enumTypes.Contains(csType))
                {
                    memberParserLines.Add($"{newName} = data.GetEnumValue<{csType}>(\"{name}\");");
                    continue;
                }

                if (csType.EndsWith("[]", StringComparison.Ordinal))
                {
                    var itemType = csType[..^2];
                    if (classHierarchies.ContainsKey(itemType))
                    {
                        memberParserLines.Add($"{newName} = [.. System.Linq.Enumerable.Select(data.GetArray<KVObject>(\"{name}\"), kv => new {itemType}(kv))];");
                        continue;
                    }

                    if (enumTypes.Contains(itemType))
                    {
                        memberParserLines.Add($"enum array error");
                        continue;
                    }

                    memberParserLines.Add($"{newName} = data.GetArray<{itemType}>(\"{name}\");");
                    continue;
                }

                memberParserLines.Add(csType switch
                {
                    "bool" => $"{newName} = data.GetProperty<bool>(\"{name}\");",
                    "string" => $"{newName} = data.GetProperty<string>(\"{name}\");",
                    "short" => $"{newName} = data.GetInt16Property(\"{name}\");",
                    "int" => $"{newName} = data.GetInt32Property(\"{name}\");",
                    "uint" => $"{newName} = data.GetUInt32Property(\"{name}\");",
                    "float" => $"{newName} = data.GetFloatProperty(\"{name}\");",
                    "GlobalSymbol" => $"{newName} = data.GetProperty<string>(\"{name}\");",
                    "Transform" => $"{newName} = new(data.GetProperty<KVObject>(\"{name}\"));",
                    "Vector3" => $"{newName} = data.GetSubCollection(\"{name}\").ToVector3();",
                    "byte" => $"{newName} = data.GetByteProperty(\"{name}\");",
                    "Quaternion" => $"{newName} = data.GetSubCollection(\"{name}\").ToQuaternion();",
                    "Particles.Utils.PiecewiseCurve" => $"{newName} = new(data.GetProperty<KVObject>(\"{name}\"), false);",
                    "KVObject" => $"{newName} = data.GetProperty<KVObject>(\"{name}\");",
                    _ => $"//{newName} = {name};",
                });
            }
        }

    }
}

Test();
Console.WriteLine("Creating classes...");
ConvertAnimLib();

partial class Program
{
    [System.Text.RegularExpressions.GeneratedRegex(@"class (?<identifier>[\w:]+)(?: : public (?<baseIdentifier>[\w:]+))?")]
    private static partial System.Text.RegularExpressions.Regex ClassRegex();

    [System.Text.RegularExpressions.GeneratedRegex(@"enum (?<identifier>[\w:]+)(?: : (?<baseType>[\w:]+))?")]
    private static partial System.Text.RegularExpressions.Regex EnumRegex();

    [System.Text.RegularExpressions.GeneratedRegex(@"^\s*(?<type>[\w:]+)(?:\s*<\s*(?<args>[\w:\s,<>]+?)\s*>)?\s+(?<name>\w+)", System.Text.RegularExpressions.RegexOptions.None)]
    private static partial System.Text.RegularExpressions.Regex PropertyTypeRegex();


    public static void Test()
    {
        Span<(string, string?, string, string)> propertyTests = [
            ("CStrongHandle", "InfoForResourceTypeIParticleSystemDefinition", "m_hParticleSystem", "CStrongHandle< InfoForResourceTypeIParticleSystemDefinition > m_hParticleSystem"),
            ("CUtlString", null, "m_name", "CUtlString m_name"),
            ("CNmParticleEvent::Type_t", null, "m_type", "CNmParticleEvent::Type_t m_type"),
        ];

        var regex = PropertyTypeRegex();
        foreach (var (eType, eTemplateArgs, eName, test) in propertyTests)
        {
            var match = regex.Match(test);
            Debug.Assert(match.Success, $"Failed to match '{test}'");

            var type = match.Groups["type"].Value;
            var args = match.Groups["args"].Value;
            var name = match.Groups["name"].Value;

            Debug.Assert(type == eType, $"Expected type '{eType}', got '{type}'");
            Debug.Assert(args == (eTemplateArgs ?? ""), $"Expected args '{eTemplateArgs}', got '{args}'");
            Debug.Assert(name == eName, $"Expected name '{eName}', got '{name}'");
        }
    }
}
