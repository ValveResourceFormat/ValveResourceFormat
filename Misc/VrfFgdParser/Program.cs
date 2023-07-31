using System.Text;
using Sledge.Formats.GameData;
using Sledge.Formats.GameData.Objects;
using VrfFgdParser;

if (args?.Length < 1)
{
    Console.Error.WriteLine("Usage: ./program <path to Steam to find .fgd files in>");
    return 1;
}

var allIcons = new SortedDictionary<string, List<string>>();
var allProperties = new HashSet<string>();
var baseIcons = new Dictionary<string, List<string>>();

foreach (var arg in args!)
{
    if (!Directory.Exists(arg))
    {
        Console.Error.WriteLine($"'{arg}' does not exist.");
        continue;
    }

    foreach (var file in Directory.EnumerateFiles(arg, "*.fgd", SearchOption.AllDirectories))
    {
        ParseFile(file);
    }
}

void ParseFile(string file)
{
    var isSource2 = File.Exists(Path.Join(Path.GetDirectoryName(file), "gameinfo.gi"));

    Console.WriteLine();
    Console.ForegroundColor = isSource2 ? ConsoleColor.Green : ConsoleColor.Blue;
    Console.Write("Parsing ");
    Console.Write(file);
    Console.WriteLine(isSource2 ? string.Empty : " (not Source 2)");
    Console.ResetColor();

    if (!isSource2)
    {
        // We don't want icons from non-Source 2 games.
        return;
    }

    GameDefinition fgd;

    try
    {
        using var stream = File.OpenRead(file);
        using var reader = new StreamReader(stream);

        var fgdFormatter = new FgdFormat(new FgdFileResolver(file));
        fgd = fgdFormatter.Read(reader);
    }
    catch (Exception e)
    {
        Console.WriteLine(e);
        return;
    }

    foreach (var _class in fgd.Classes)
    {
        foreach (var property in _class.Properties)
        {
            allProperties.Add(property.Name.ToLowerInvariant());
        }

        foreach (var behaviour in _class.Behaviours)
        {
            string? value = null;

            if (behaviour.Name == "iconsprite" && behaviour.Values.Count > 0)
            {
                value = behaviour.Values[0];

                if (!value.StartsWith("materials/"))
                {
                    value = "materials/" + value;
                }

                if (value.EndsWith(".vmt"))
                {
                    value = value[..^4] + ".vmat";
                }

                if (!value.EndsWith(".vmat"))
                {
                    value += ".vmat";
                }
            }

            if (behaviour.Name == "iconsprite" && behaviour.Values.Count == 0) // TODO: Hack, needs library support
            {
                foreach (var dict in _class.Dictionaries)
                {
                    if (dict.Name != "iconsprite")
                    {
                        continue;
                    }

                    foreach (var dictValue in dict)
                    {
                        if (dictValue.Key == "image")
                        {
                            value = (string)dictValue.Value.Value;
                        }
                    }
                }
            }

            if ((behaviour.Name == "studio" || behaviour.Name == "editormodel" || behaviour.Name == "model") && behaviour.Values.Count > 0)
            {
                value = behaviour.Values[0];

                if (!value.StartsWith("models/", StringComparison.Ordinal))
                {
                    value = "models/" + value;
                }

                if (value.EndsWith(".mdl", StringComparison.Ordinal))
                {
                    value = value[..^4] + ".vmdl";
                }

                if (!value.EndsWith(".vmdl", StringComparison.Ordinal))
                {
                    value += ".vmdl";
                }
            }

            if (value == null && _class.ClassType != ClassType.BaseClass)
            {
                foreach (var baseClass in _class.BaseClasses)
                {
                    if (baseIcons.TryGetValue(baseClass, out var values))
                    {
                        value = values[0]; // TODO: more than one
                        Console.WriteLine($"Found {_class.Name} base icon from {baseClass}");
                        break;
                    }
                }
            }

            if (value != null)
            {
                IDictionary<string, List<string>> icons = _class.ClassType == ClassType.BaseClass ? baseIcons : allIcons;

                if (icons.TryGetValue(_class.Name, out var existingIcons))
                {
                    if (!existingIcons.Contains(value))
                    {
                        existingIcons.Add(value);
                    }

                    continue;
                }

                icons[_class.Name] = new List<string> { value };
            }
        }
    }
}

Console.WriteLine();
Console.WriteLine($"Found {allIcons.Count} entities with icons");

var iconsString = new StringBuilder();

foreach (var icon in allIcons)
{
    icon.Value.Sort((a, b) =>
    {
        var aContains = a.Contains(icon.Key, StringComparison.Ordinal) ? 1 : 0;
        var bContains = b.Contains(icon.Key, StringComparison.Ordinal) ? 1 : 0;
        var aMdl = a.EndsWith(".vmdl", StringComparison.Ordinal) ? 1 : 0;
        var bMdl = b.EndsWith(".vmdl", StringComparison.Ordinal) ? 1 : 0;

        Console.WriteLine($"{a} <=> {b}");

        if (aContains != bContains)
        {
            return bContains - aContains;
        }

        if (aMdl != bMdl)
        {
            return bMdl - aMdl;
        }

        return string.CompareOrdinal(a, b);
    });

    iconsString.Append('"');
    iconsString.Append(icon.Key);
    iconsString.Append('"');
    iconsString.Append(" => new[] { ");
    iconsString.Append('"');
    iconsString.Append(string.Join("\", \"", icon.Value));
    iconsString.Append("\" },");
    iconsString.AppendLine();
}

File.WriteAllText("icons.txt", iconsString.ToString());

Console.WriteLine($"Found {allProperties.Count} unique properties");

var propertiesString = new StringBuilder();

foreach (var property in allProperties.OrderBy(x => x))
{
    propertiesString.Append('"');
    propertiesString.Append(property);
    propertiesString.Append('"');
    propertiesString.Append(',');
    propertiesString.AppendLine();
}

File.WriteAllText("properties.txt", propertiesString.ToString());

return 0;
