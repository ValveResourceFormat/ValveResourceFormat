using System.Globalization;
using System.Text;
using Sledge.Formats.GameData;
using Sledge.Formats.GameData.Objects;
using ValveResourceFormat.IO;
using VrfFgdParser;

var pathsToCheck = args ?? [];

if (pathsToCheck.Length < 1)
{
    Console.Error.WriteLine("Usage: ./program [path to Steam to find .fgd files in]");

    if (GameFolderLocator.SteamPath == null)
    {
        return 1;
    }

    Console.Error.WriteLine($"No path specified, searching Steam libraries.");

    pathsToCheck = [.. GameFolderLocator.FindAllSteamGames().Select(static x => x.SteamPath).ToHashSet()];
}

var allEntities = new SortedDictionary<string, EntityInfo>();
var allProperties = new HashSet<string>();
var baseEntities = new Dictionary<string, EntityInfo>();
var entityMaterials = new Dictionary<string, string>();
var classHelpers = new Dictionary<string, HelperInfo>();

foreach (var arg in pathsToCheck)
{
    if (!Directory.Exists(arg))
    {
        Console.Error.WriteLine($"'{arg}' does not exist.");
        continue;
    }

    Console.WriteLine($"Searching {arg}");

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

        ParseHelpers(_class);

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

            // "studio()" is the entity's real in-game model. "editormodel()"/"model()" are Hammer-only
            // visualization aids that don't represent the entity's actual appearance.
            var isStudioValue = false;

            if ((behaviour.Name == "studio" || behaviour.Name == "editormodel" || behaviour.Name == "model") && behaviour.Values.Count > 0)
            {
                value = behaviour.Values[0];

                if (value is not "editormodel" and not "static")
                {
                    if (value.EndsWith(".mdl", StringComparison.Ordinal))
                    {
                        value = value[..^4] + ".vmdl";
                    }

                    if (!value.EndsWith(".vmdl", StringComparison.Ordinal))
                    {
                        value += ".vmdl";
                    }

                    isStudioValue = behaviour.Name == "studio";
                }
                else
                {
                    value = null;
                }
            }

            if (value == null && _class.ClassType != ClassType.BaseClass)
            {
                foreach (var baseClass in _class.BaseClasses)
                {
                    if (baseEntities.TryGetValue(baseClass, out var values) && values.Icons.Count > 0)
                    {
                        value = values.Icons.First(); // TODO: more than one
                        isStudioValue = values.IsStudio;
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
                    existingIcons.IsStudio |= isStudioValue;
                }
                else
                {
                    icons[_class.Name] = new();
                    icons[_class.Name].Icons.Add(value);
                    icons[_class.Name].IsStudio = isStudioValue;
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

// The shape helpers a class declares, positional or as a block of keys. Inherited ones are resolved on write.
void ParseHelpers(GameDataClass _class)
{
    if (!classHelpers.TryGetValue(_class.Name, out var info))
    {
        info = new HelperInfo();
        classHelpers[_class.Name] = info;
    }

    info.IsConcrete |= _class.ClassType != ClassType.BaseClass;

    foreach (var baseClass in _class.BaseClasses)
    {
        AddUnique(info.Bases, baseClass);
    }

    foreach (var behaviour in _class.Behaviours)
    {
        var values = behaviour.Values;
        var key = values.Count > 0 ? values[0].ToLowerInvariant() : null;
        var key2 = values.Count > 1 ? values[1].ToLowerInvariant() : null;

        switch (behaviour.Name)
        {
            case "sphere" when key != null && !float.TryParse(key, CultureInfo.InvariantCulture, out _):
                AddHelper(info.Spheres, key, $"new(\"{key}\", {HelperColor(values, 1)})");
                break;

            case "sphere" when key == null && !HasDictionary(_class, "sphere"):
                AddHelper(info.Spheres, "radius", "new(\"radius\", Color32.White)");
                break;

            case "leansphere" when key != null:
                AddHelper(info.Spheres, key, $"new(\"{key}\", {HelperColor(values, 1)}, Lean: true)");
                break;

            case "vecline_local" when key != null:
                AddHelper(info.OffsetLines, key, $"new(\"{key}\", {HelperColor(values, 1)})");
                break;

            case "box_oriented" or "box_world_aligned" or "wirebox" or "wirebox_local" when key2 != null:
                var wire = behaviour.Name.StartsWith("wirebox", StringComparison.Ordinal) ? ", Wire: true" : string.Empty;
                AddHelper(info.Boxes, $"{key}|{key2}", $"new(HammerEntity.BoxSpace.{BoxSpace(behaviour.Name)}, \"{key}\", \"{key2}\"{wire})");
                break;

            case "centered_box_oriented" when key != null:
                AddHelper(info.Boxes, key, $"new(HammerEntity.BoxSpace.Oriented, SizeKey: \"{key}\")");
                break;

            case "drawangles" or "drawangles_local" when !HasDictionary(_class, behaviour.Name):
                AddAngles(info, key, key2, behaviour.Name == "drawangles_local");
                break;

            case "barnlight" or "rectlight" or "omnilight" or "lightcone" or "volumetric_fog_controller":
                info.Shapes.Add(behaviour.Name);
                break;
        }
    }

    foreach (var dict in _class.Dictionaries)
    {
        string? Get(string key) => dict.TryGetValue(key, out var value) ? Convert.ToString(value.Value, CultureInfo.InvariantCulture)?.ToLowerInvariant() : null;

        switch (dict.Name)
        {
            case "sphere" or "leansphere":
                var radius = Get("radius") ?? "radius";
                var lean = dict.Name == "leansphere" ? ", Lean: true" : string.Empty;
                var edgeFade = Get("edge_fade") is { } edgeFadeKey ? $", EdgeFadeKey: \"{edgeFadeKey}\"" : string.Empty;
                AddHelper(info.Spheres, radius, $"new(\"{radius}\", {DictionaryColor(dict)}{lean}{edgeFade})");
                break;

            case "box_oriented" or "box_world_aligned":
                var fields = new List<string> { $"HammerEntity.BoxSpace.{BoxSpace(dict.Name)}", $"\"{Get("box_min")}\"", $"\"{Get("box_max")}\"" };

                if (Get("edge_fades") is { } edgeFades)
                {
                    fields.Add($"EdgeFadesKey: \"{edgeFades}\"");
                }

                if (Get("single_edge_fade") is { } singleEdgeFade)
                {
                    fields.Add($"EdgeFadeKey: \"{singleEdgeFade}\"");
                }

                AddHelper(info.Boxes, $"{Get("box_min")}|{Get("box_max")}", $"new({string.Join(", ", fields)})");
                break;

            case "centered_box_oriented":
                AddHelper(info.Boxes, (Get("box_size") ?? string.Empty), $"new(HammerEntity.BoxSpace.Oriented, SizeKey: \"{Get("box_size")}\")");
                break;

            case "drawangles" or "drawangles_local":
                AddAngles(info, Get("angles_key"), null, dict.Name == "drawangles_local");
                break;
        }
    }
}

static bool HasDictionary(GameDataClass _class, string name) => _class.Dictionaries.Any(dict => dict.Name == name);

static string BoxSpace(string helper) => helper switch
{
    "box_oriented" => "Oriented",
    "box_world_aligned" or "wirebox_local" => "WorldAligned",
    _ => "World",
};

// Positional helpers give the color as three numbers after the key, white when left out
static string HelperColor(List<string> values, int start)
{
    if (values.Count < start + 3 || !values.Skip(start).Take(3).All(value => int.TryParse(value, out _)))
    {
        return "Color32.White";
    }

    return ConstructColor([.. values.Skip(start).Take(3)]);
}

static string DictionaryColor(GameDataDictionary dict)
{
    if (!dict.TryGetValue("color", out var color) || color.Value is string || color.Value is not System.Collections.IEnumerable components)
    {
        return "Color32.White";
    }

    return ConstructColor([.. components.Cast<object>().Select(component => Convert.ToString(component, CultureInfo.InvariantCulture)!)]);
}

static void AddAngles(HelperInfo info, string? key, string? isLocalKey, bool isLocal)
{
    // Games disagree on whether doors take local angles; keep the key that says so where any of them has it
    var existing = info.Angles;
    info.Angles = (key ?? existing?.Key, isLocalKey ?? existing?.IsLocalKey, isLocal || existing?.IsLocal == true);
}

// Keeps the longest form of a helper the games declare differently, such as a sphere one of them gives an edge fade
static void AddHelper(List<(string Id, string Code)> list, string id, string code)
{
    var index = list.FindIndex(helper => helper.Id == id);

    if (index < 0)
    {
        list.Add((id, code));
    }
    else if (code.Length > list[index].Code.Length)
    {
        list[index] = (id, code);
    }
}

static void AddUnique(List<string> list, string value)
{
    if (!list.Contains(value))
    {
        list.Add(value);
    }
}

HelperInfo ResolveHelpers(string name, HashSet<string>? seen = null)
{
    var resolved = new HelperInfo();
    seen ??= [];

    if (!seen.Add(name) || !classHelpers.TryGetValue(name, out var own))
    {
        return resolved;
    }

    var inherited = own.Bases.Select(baseClass => ResolveHelpers(baseClass, seen)).ToList();

    // A class's own helpers of a kind replace the ones it inherits
    foreach (var select in new Func<HelperInfo, List<(string Id, string Code)>>[] { static x => x.Spheres, static x => x.OffsetLines, static x => x.Boxes })
    {
        var sources = select(own).Count > 0 ? [own] : inherited;

        foreach (var (id, code) in sources.SelectMany(select))
        {
            AddHelper(select(resolved), id, code);
        }
    }

    foreach (var source in inherited.Prepend(own))
    {
        resolved.Shapes.UnionWith(source.Shapes);
        resolved.Angles ??= source.Angles;
    }

    return resolved;
}

void WriteEntities()
{
    Console.WriteLine($"Found {allEntities.Count} entities");

    foreach (var (name, info) in classHelpers)
    {
        if (info.IsConcrete && !allEntities.ContainsKey(name))
        {
            var helpers = ResolveHelpers(name);

            if (!helpers.IsEmpty)
            {
                allEntities[name] = new();
            }
        }
    }

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

        if (icon.Value.IsStudio)
        {
            fields.Add("Studio = true");
        }

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

        var helpers = ResolveHelpers(icon.Key);

        if (helpers.Shapes.Count > 0)
        {
            fields.Add($"Shapes = {string.Join(" | ", helpers.Shapes.Order().Select(static shape => "HammerEntity.Shape." + ShapeName(shape)))}");
        }

        if (helpers.Spheres.Count > 0)
        {
            fields.Add($"Spheres = [{string.Join(", ", helpers.Spheres.Select(static helper => helper.Code))}]");
        }

        if (helpers.Boxes.Count > 0)
        {
            fields.Add($"Boxes = [{string.Join(", ", helpers.Boxes.Select(static helper => helper.Code))}]");
        }

        if (helpers.OffsetLines.Count > 0)
        {
            fields.Add($"OffsetLines = [{string.Join(", ", helpers.OffsetLines.Select(static helper => helper.Code))}]");
        }

        if (helpers.Angles is var (key, isLocalKey, isLocal))
        {
            var angleFields = new List<string>();

            if (key != null)
            {
                angleFields.Add($"\"{key}\"");
            }

            if (isLocalKey != null)
            {
                angleFields.Add($"IsLocalKey: \"{isLocalKey}\"");
            }

            if (isLocal)
            {
                angleFields.Add("IsLocal: true");
            }

            fields.Add($"Direction = new({string.Join(", ", angleFields)})");
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

static string ShapeName(string helper) => helper switch
{
    "barnlight" => "BarnLight",
    "rectlight" => "RectLight",
    "omnilight" => "OmniLight",
    "lightcone" => "LightCone",
    "volumetric_fog_controller" => "VolumetricFogController",
    _ => throw new InvalidDataException(helper),
};

class EntityInfo
{
    public HashSet<string> Icons = [];
    public bool IsStudio;
    public string? Color;
    public HashSet<string> Lines = [];
}

class HelperInfo
{
    public List<string> Bases = [];
    public bool IsConcrete;
    public List<(string Id, string Code)> Spheres = [];
    public List<(string Id, string Code)> OffsetLines = [];
    public List<(string Id, string Code)> Boxes = [];
    public SortedSet<string> Shapes = [];
    public (string? Key, string? IsLocalKey, bool IsLocal)? Angles;

    public bool IsEmpty => Spheres.Count == 0 && OffsetLines.Count == 0 && Boxes.Count == 0 && Shapes.Count == 0 && Angles == null;
}
