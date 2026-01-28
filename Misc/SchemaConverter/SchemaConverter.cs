using System.Diagnostics;
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

    // for each .h file
    foreach (var file in Directory.GetFiles(sourceDir, "*.h"))
    {
        var fileName = Path.GetFileNameWithoutExtension(file);
        fileName = ConvertClassName(fileName, sourceNameSpace);
        var destFile = Path.Combine(destinationDir, fileName + ".cs");

        using var reader = new StreamReader(file);
        using var writer = new StreamWriter(destFile);

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
    { "int8_t", "sbyte" },
    { "uint16_t", "ushort" },
    { "int16_t", "short" },
    { "uint32_t", "uint" },
    { "int32_t", "int" },
    { "uint64_t", "ulong" },
    { "int64_t", "long" },
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

    return sb.ToString();
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
    bool writeEnum = false;

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

            var csClass = $"class {ConvertClassName(@class, cStyleNamespacePreffix)}";
            if (!string.IsNullOrEmpty(baseClass))
            {
                csClass += $" : {ConvertClassName(baseClass, cStyleNamespacePreffix)}";
            }
            writer.WriteLine(csClass);
            writer.WriteLine("{");
            writer.WriteLine("}");
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
            writer.WriteLine(rawLine == "};" ? "}" : rawLine);
        }
    }
}

Console.WriteLine("Creating classes...");
ConvertAnimLib();

partial class Program
{
    [System.Text.RegularExpressions.GeneratedRegex(@"class (?<identifier>[\w:]+)(?: : public (?<baseIdentifier>[\w:]+))?")]
    private static partial System.Text.RegularExpressions.Regex ClassRegex();

    [System.Text.RegularExpressions.GeneratedRegex(@"enum (?<identifier>[\w:]+)(?: : (?<baseType>[\w:]+))?")]
    private static partial System.Text.RegularExpressions.Regex EnumRegex();
}
