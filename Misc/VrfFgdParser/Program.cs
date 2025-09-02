using System.Globalization;
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
var entityMaterials = new Dictionary<string, string>();

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
WriteMaterials();

return 0;

static string ConstructColor(List<string> values)
{
    var color = string.Empty;

    if (values.All(x => x == "255"))
    {
        return "Color32.White";
    }

    if (values.Count == 3)
    {
        color = $"new Color32({values[0]}, {values[1]}, {values[2]})";
    }
    else if (values.Count == 4)
    {
        color = $"new Color32({values[0]}, {values[1]}, {values[2]}, {values[3]})";
    }
    else
    {
        throw new InvalidDataException();
    }

    return color;
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

                if (!value.StartsWith("materials/", StringComparison.Ordinal))
                {
                    value = "materials/" + value;
                }

                if (value.EndsWith(".vmt", StringComparison.Ordinal))
                {
                    value = value[..^4] + ".vmat";
                }

                if (!value.EndsWith(".vmat", StringComparison.Ordinal))
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

            foreach (var dict in _class.Dictionaries)
            {
                if (dict.Name == "metadata" && dict.TryGetValue("auto_apply_material", out var autoApplyMaterial))
                {
                    var material = (string)autoApplyMaterial.Value;

                    if (material != "materials/tools/toolstrigger.vmat")
                    {
                        entityMaterials[_class.Name] = material;
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
                    color = ConstructColor(behaviour.Values);
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

                    if (!colors.TryGetValue(_class.Name, out var colorValue))
                    {
                        colorValue = new();
                        colors[_class.Name] = colorValue;
                    }

                    colorValue.Color ??= color;
                }
            }

            // Line - TODO: Look up from base class?
            {
                if (behaviour.Name == "line" && behaviour.Values.Count > 3)
                {
                    var color = ConstructColor([.. behaviour.Values.Take(3)]);
                    var line = string.Empty;

                    if (behaviour.Values.Count == 5)
                    {
                        line = $"new HammerEntity.Line({color}, \"{behaviour.Values[3]}\", \"{behaviour.Values[4]}\")";
                    }
                    else if (behaviour.Values.Count == 7)
                    {
                        line = $"new HammerEntity.Line({color}, \"{behaviour.Values[3]}\", \"{behaviour.Values[4]}\", \"{behaviour.Values[5]}\", \"{behaviour.Values[6]}\")";
                    }

                    IDictionary<string, EntityInfo> entity = _class.ClassType == ClassType.BaseClass ? baseEntities : allEntities;

                    if (!entity.TryGetValue(_class.Name, out var lineValue))
                    {
                        lineValue = new();
                        entity[_class.Name] = lineValue;
                    }

                    lineValue.Lines.Add(line);
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
            fields.Add($"Icons = [\"{iconsStr}\"]");
        }

        if (icon.Value.Color != null)
        {
            fields.Add($"Color = {icon.Value.Color}");
        }

        if (icon.Value.Lines.Count > 0)
        {
            var linesStr = string.Join(", ", icon.Value.Lines);
            fields.Add($"Lines = [{linesStr}]");
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

void WriteMaterials()
{
    Console.WriteLine($"Found {entityMaterials.Count} entity materials");

    var str = new StringBuilder();

    foreach (var (name, material) in entityMaterials.OrderBy(x => x.Key))
    {
        str.AppendLine(CultureInfo.InvariantCulture, $"\"{name}\" => \"{material}\"");
    }

    File.WriteAllText("entity_materials.txt", str.ToString());
}

class EntityInfo
{
    public HashSet<string> Icons = [];
    public string? Color;
    public HashSet<string> Lines = [];
}
