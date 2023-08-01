using System.Text;
using Sledge.Formats.GameData;
using Sledge.Formats.GameData.Objects;
using VrfFgdParser;

if (args?.Length < 1)
{
    Console.Error.WriteLine("Usage: ./program <path to Steam to find .fgd files in>");
    return 1;
}

var allEntities = new SortedDictionary<string, EntityInfo>();
var allProperties = new HashSet<string>();
var baseEntities = new Dictionary<string, EntityInfo>();

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

Console.WriteLine();

WriteEntities();
WriteProperties();

return 0;

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

                if (!value.StartsWith("models/", StringComparison.Ordinal) && !value.StartsWith("characters/models/", StringComparison.Ordinal))
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
                    if (baseEntities.TryGetValue(baseClass, out var values) && values.Icons.Count > 0)
                    {
                        value = values.Icons.First(); // TODO: more than one
                        Console.WriteLine($"Found {_class.Name} base icon from {baseClass}");
                        break;
                    }
                }
            }

            if (value != null)
            {
                IDictionary<string, EntityInfo> icons = _class.ClassType == ClassType.BaseClass ? baseEntities : allEntities;

                if (icons.TryGetValue(_class.Name, out var existingIcons))
                {
                    existingIcons.Icons.Add(value);
                }
                else
                {
                    icons[_class.Name] = new();
                    icons[_class.Name].Icons.Add(value);
                }
            }

            // Color
            {
                string? color = null;

                if (behaviour.Name == "color" && behaviour.Values.Count >= 3)
                {
                    color = $"new Vector3({behaviour.Values[0]}, {behaviour.Values[1]}, {behaviour.Values[2]})";
                }

                if (color == null && _class.ClassType != ClassType.BaseClass)
                {
                    foreach (var baseClass in _class.BaseClasses)
                    {
                        if (allEntities.TryGetValue(baseClass, out var colorEntity) && colorEntity.Color != null)
                        {
                            color = colorEntity.Color;
                            Console.WriteLine($"Found {_class.Name} base color from {baseClass}");
                            break;
                        }
                    }
                }

                if (color != null)
                {
                    IDictionary<string, EntityInfo> colors = _class.ClassType == ClassType.BaseClass ? baseEntities : allEntities;

                    if (!colors.ContainsKey(_class.Name))
                    {
                        colors[_class.Name] = new();
                    }

                    colors[_class.Name].Color ??= color;
                }
            }
        }
    }
}

void WriteEntities()
{
    Console.WriteLine($"Found {allEntities.Count} entities");

    var str = new StringBuilder();

    foreach (var icon in allEntities)
    {
        var icons = icon.Value.Icons.ToList();
        icons.Sort((a, b) =>
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

        var fields = new List<string>();

        if (icons.Count > 0)
        {
            var iconsStr = string.Join(@""", """, icons);
            fields.Add($"Icons = new[] {{ \"{iconsStr}\" }}");
        }

        if (icon.Value.Color != null)
        {
            fields.Add($"Color = {icon.Value.Color}");
        }

        str.Append("            ");
        str.Append('"');
        str.Append(icon.Key);
        str.Append('"');
        str.Append(" => new() { ");
        str.Append(string.Join(", ", fields));
        str.Append(" },");
        str.AppendLine();
    }

    File.WriteAllText("entities.txt", str.ToString());
}

void WriteProperties()
{
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
}

class EntityInfo
{
    public HashSet<string> Icons = new();
    public string? Color;
}
