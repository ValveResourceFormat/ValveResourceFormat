using System.Text;
using Sledge.Formats.GameData;
using Sledge.Formats.GameData.Objects;
using VrfFgdParser;

if (args?.Length < 1 || !Directory.Exists(args![0]))
{
    Console.Error.WriteLine("Usage: ./program <path to Steam to find .fgd files in>");
    return 1;
}

var allIcons = new SortedDictionary<string, string>();
var allProperties = new HashSet<string>();
var baseIcons = new Dictionary<string, string>();

foreach (var file in Directory.EnumerateFiles(args[0], "*.fgd", SearchOption.AllDirectories))
{
    var isSource2 = File.Exists(Path.Join(Path.GetDirectoryName(file), "gameinfo.gi"));

    Console.WriteLine();
    Console.ForegroundColor = isSource2 ? ConsoleColor.Green : ConsoleColor.Blue;
    Console.Write("Parsing ");
    Console.Write(file);
    Console.WriteLine(isSource2 ? string.Empty : " (not Source 2)");
    Console.ResetColor();

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
        continue;
    }

    foreach (var _class in fgd.Classes)
    {
        foreach (var property in _class.Properties)
        {
            allProperties.Add(property.Name.ToLowerInvariant());
        }

        if (!isSource2)
        {
            // We don't want icons from non-Source 2 games.
            continue;
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

            if ((behaviour.Name == "studio" || behaviour.Name == "editormodel") && behaviour.Values.Count > 0)
            {
                // TODO: get model property
                value = behaviour.Values[0];

                if (!value.StartsWith("models/"))
                {
                    value = "models/" + value;
                }

                if (value.EndsWith(".mdl"))
                {
                    value = value[..^4] + ".vmdl";
                }

                if (!value.EndsWith(".vmdl"))
                {
                    value += ".vmdl";
                }
            }

            if (value == null && _class.ClassType == ClassType.BaseClass)
            {
                foreach (var baseClass in _class.BaseClasses)
                {
                    if (baseIcons.TryGetValue(baseClass, out value))
                    {
                        Console.WriteLine($"Found {_class.Name} found base icon from {baseClass}");
                        break;
                    }
                }
            }

            if (value != null)
            {
                IDictionary<string, string> icons = _class.ClassType == ClassType.BaseClass ? baseIcons : allIcons;

                if (icons.TryGetValue(_class.Name, out var existingValue))
                {
                    if (existingValue != value)
                    {
                        Console.WriteLine($"Found {_class.Name} with different value: {value} => {existingValue}");
                        continue;
                    }

                    continue;
                }

                icons[_class.Name] = value;
            }
        }
    }
}

Console.WriteLine();
Console.WriteLine($"Found {allIcons.Count} entities with icons");

var iconsString = new StringBuilder();

foreach (var icon in allIcons)
{
    iconsString.Append('"');
    iconsString.Append(icon.Key);
    iconsString.Append('"');
    iconsString.Append(" => ");
    iconsString.Append('"');
    iconsString.Append(icon.Value);
    iconsString.Append('"');
    iconsString.Append(',');
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
