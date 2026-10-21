using System.Globalization;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Text;
using ValveKeyValue;
using ValveResourceFormat.IO;
using ValveResourceFormat.IO.Fgd;
using ValveResourceFormat.Serialization.KeyValues;

if (GameFolderLocator.SteamPath == null)
{
    Console.Error.WriteLine("Steam was not found.");
    return 1;
}

var installedGames = GameFolderLocator.FindAllSteamGames();

// Apps that share a folder are the one with the lowest app id, as the renderer finds them
var games = installedGames
    .Where(static game => Directory.Exists(game.GamePath))
    .GroupBy(static game => game.GamePath, StringComparer.OrdinalIgnoreCase)
    .Select(static folder => folder.MinBy(static game => game.AppID))
    .ToList();

var allProperties = new HashSet<string>();
var entityMaterials = new Dictionary<string, string>();

// The table the renderer embeds is written over, and the games not installed here keep the classes it has of them
var classesPath = Path.GetFullPath(Path.Join(Path.GetDirectoryName(GetSourcePath()), "..", "..", "Renderer", "Resources", "entity_classes.kv3"));
var previous = File.Exists(classesPath) ? KVDocumentExtensions.ParseKV3(classesPath).Root : new KVObject();
var previousDefinitions = (previous.GetArray("classes") ?? []).ToDictionary(static data => data.GetStringProperty("id", data.GetStringProperty("name")), StringComparer.Ordinal);
var previousTables = (previous.GetArray("games") ?? []).ToDictionary(static table => table.GetInt32Property("app_id"));

// Every version of each class by its name, with the games that declare it so, under a key of its own until
// every game is read and the ids it is written under are known
var definitions = new Dictionary<string, List<(string Text, string Key, KVObject Data, SortedSet<int> AppIds)>>(StringComparer.Ordinal);
var gameTables = new List<KVObject>();

var installed = games.ToDictionary(static game => game.AppID);
var appIds = installed.Keys.Union(previousTables.Keys).Order();

foreach (var appId in appIds)
{
    var table = installed.TryGetValue(appId, out var game) ? ParseGame(game) : null;

    // A game whose tools are not installed here keeps what was found of it before
    if (table == null && previousTables.TryGetValue(appId, out var previousTable))
    {
        Console.WriteLine($"Keeping the classes of {appId}, which is not installed with its FGD files");
        table = KeepTable(previousTable);
    }

    if (table != null)
    {
        gameTables.Add(table);
    }
}

Console.WriteLine();

WriteClasses();
WriteProperties();
WriteMaterials();

return 0;

// The classes of an installed game, or null where none of its gameinfo files names FGD files that are there
KVObject? ParseGame(GameFolderLocator.SteamLibraryGameInfo game)
{
    Console.WriteLine($"Searching {game.AppID} {game.AppName} ({game.GamePath})");

    // The folder is walked once for both, as most of the time goes to walking it
    var files = Directory.EnumerateFiles(game.GamePath, "*", SearchOption.AllDirectories)
        .Where(static path => path.EndsWith(".fgd", StringComparison.OrdinalIgnoreCase) || Path.GetFileName(path).Equals("gameinfo.gi", StringComparison.OrdinalIgnoreCase))
        .ToList();

    foreach (var file in files.Where(static path => path.EndsWith(".fgd", StringComparison.OrdinalIgnoreCase)))
    {
        ParseFile(file);
    }

    // The classes every game shares are read as part of each game, with what the game changes of them, and an
    // addon is made with the classes of its game
    var gameInfos = files
        .Where(static path => path.EndsWith("gameinfo.gi", StringComparison.OrdinalIgnoreCase)
            && Path.GetFileName(Path.GetDirectoryName(path)) != "core"
            && !Path.GetDirectoryName(Path.GetDirectoryName(path))!.EndsWith("_addons", StringComparison.OrdinalIgnoreCase))
        .Order(StringComparer.Ordinal);

    foreach (var gameInfo in gameInfos)
    {
        if (ParseGameClasses(game, gameInfo) is { } table)
        {
            return table;
        }
    }

    return null;
}

// The classes of one game as Hammer builds them
KVObject? ParseGameClasses(GameFolderLocator.SteamLibraryGameInfo game, string gameInfo)
{
    using var fileLoader = new GameFileLoader(null, gameInfo);

    if (fileLoader.LoadFgd() is not { } fgd)
    {
        return null;
    }

    // Of the classes a class is based on, only the ones an entity can be required to be placed under are read
    var requiredParentClasses = fgd.Classes
        .Select(static gameClass => GetText(gameClass.Metadata, "required_parent_class"))
        .OfType<string>()
        .Where(static name => name.Length > 0)
        .ToHashSet(StringComparer.OrdinalIgnoreCase);

    var classIds = KVObject.Array();

    foreach (var gameClass in fgd.Classes)
    {
        if (ConvertClass(gameClass, requiredParentClasses) is { } data)
        {
            classIds.Add(AddDefinition(gameClass.Name, data, game.AppID));
        }
    }

    Console.WriteLine($"Found {classIds.Count} classes in {Path.GetDirectoryName(gameInfo)}");

    var table = new KVObject { { "app_id", game.AppID } };

    if (fgd.GridNav is var (edgeSize, offsetX, offsetY, pickHeight))
    {
        table.Add("gridnav", new KVObject
        {
            { "edge_size", edgeSize },
            { "offset_x", offsetX },
            { "offset_y", offsetY },
            { "pick_height", pickHeight },
        });
    }

    table.Add("classes", classIds);
    return table;
}

// The table of a game as it was written before, with its classes added again among the ones written now
KVObject KeepTable(KVObject previousTable)
{
    var appId = previousTable.GetInt32Property("app_id");
    var table = new KVObject();

    foreach (var (name, value) in previousTable)
    {
        table.Add(name, name == "classes"
            ? KVObject.Array((previousTable.GetArray("classes") ?? []).Select(id => new KVObject(KeepDefinition((string)id, appId))))
            : value);
    }

    return table;
}

// A class as it was written before, under the id it is given among the classes written now
string KeepDefinition(string previousId, int appId)
{
    var previousData = previousDefinitions.GetValueOrDefault(previousId)
        ?? throw new InvalidDataException($"'{classesPath}' names class '{previousId}', which it does not declare.");

    // It is written as it would be by a game that is installed, which gives it its id last
    var data = new KVObject();

    foreach (var (name, value) in previousData)
    {
        if (name != "id")
        {
            data.Add(name, value);
        }
    }

    return AddDefinition(data.GetStringProperty("name"), data, appId);
}

// The key of the version of a class a game declares, which is shared by every game that declares it alike
string AddDefinition(string name, KVObject data, int appId)
{
    var text = data.ToKV3String();

    if (!definitions.TryGetValue(name, out var versions))
    {
        versions = [];
        definitions.Add(name, versions);
    }

    foreach (var version in versions)
    {
        if (version.Text == text)
        {
            version.AppIds.Add(appId);
            return version.Key;
        }
    }

    // Class names never have this character, so a key is never the name of a class
    var key = string.Create(CultureInfo.InvariantCulture, $"{name}#{versions.Count}");

    versions.Add((text, key, data, [appId]));
    return key;
}

// What an entity of a class is in the editor, where it is not the most common, a point; null for a class only
// other classes are based on
static string? GetKind(FgdClass gameClass) => gameClass.Type switch
{
    FgdClassType.PointClass or FgdClassType.NpcClass or FgdClassType.FilterClass or FgdClassType.MoveClass
        or FgdClassType.KeyFrameClass or FgdClassType.NavLinkClass or FgdClassType.ChoreoAnchorClass => "point",
    FgdClassType.SolidClass => "solid",
    FgdClassType.PathClass => "path",
    FgdClassType.PathNodeClass => "path_node",
    FgdClassType.CableClass => "cable",
    _ => null,
};

// Every class a class is based on, directly or through others
static IEnumerable<FgdClass> GetAllBaseClasses(FgdClass gameClass)
    => gameClass.BaseClasses.SelectMany(static baseClass => GetAllBaseClasses(baseClass).Prepend(baseClass));

// A class as the renderer reads it, which is only what an entity of it is drawn with, or null for a class only
// other classes are based on
static KVObject? ConvertClass(FgdClass gameClass, HashSet<string> requiredParentClasses)
{
    if (GetKind(gameClass) is not { } kind)
    {
        return null;
    }

    var data = new KVObject { { "name", gameClass.Name } };

    if (kind != "point")
    {
        data.Add("kind", kind);
    }

    if (gameClass.HasSize)
    {
        data.Add("mins", ToArray(gameClass.Mins));
        data.Add("maxs", ToArray(gameClass.Maxs));
    }

    var helpers = KVObject.Array();

    foreach (var helper in gameClass.Helpers)
    {
        helpers.Add(ConvertHelper(helper.Name, helper.Arguments, helper.Metadata));
    }

    foreach (var variable in gameClass.Variables)
    {
        // A key of one of these types is drawn by a helper of its own, which is given its name
        var helperName = variable.DeclaredType switch
        {
            FgdVariableType.VecLine => "vecline",
            FgdVariableType.Axis => "axis",
            FgdVariableType.LocalAxis => "localaxis",
            _ => null,
        };

        if (helperName != null)
        {
            // A key without a name gives the helper none
            helpers.Add(ConvertHelper(helperName, variable.Name.Length > 0 ? [variable.Name] : [], null));
        }
    }

    if (helpers.Count > 0)
    {
        data.Add("helpers", helpers);
    }

    if (GetText(gameClass.Metadata, "required_parent_class") is { Length: > 0 } requiredParentClass)
    {
        data.Add("required_parent_class", requiredParentClass);
    }

    if (GetText(gameClass.Metadata, "auto_apply_material") is { Length: > 0 } autoApplyMaterial)
    {
        data.Add("auto_apply_material", autoApplyMaterial);
    }

    // The keys that name the animation the model plays, the one that matters most first, and of keys that matter
    // as much the one declared first
    var previewAnimationKeys = gameClass.Variables
        .Where(static variable => variable.Name.Length > 0 && variable.Metadata.GetBooleanProperty("is_preview_animation"))
        .OrderByDescending(static variable => variable.Metadata.GetInt32Property("preview_animation_priority"))
        .Select(static variable => new KVObject(variable.Name))
        .ToList();

    if (previewAnimationKeys.Count > 0)
    {
        data.Add("preview_animation_keys", KVObject.Array(previewAnimationKeys));
    }

    var baseClasses = GetAllBaseClasses(gameClass)
        .Select(static baseClass => baseClass.Name)
        .Where(requiredParentClasses.Contains)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .Select(static name => new KVObject(name))
        .ToList();

    if (baseClasses.Count > 0)
    {
        data.Add("base_classes", KVObject.Array(baseClasses));
    }

    return data;
}

static KVObject ConvertHelper(string name, IReadOnlyList<string> arguments, KVObject? metadata)
{
    var helper = new KVObject { { "name", name } };

    if (arguments.Count > 0)
    {
        helper.Add("arguments", KVObject.Array(arguments.Select(static argument => new KVObject(argument))));
    }

    if (metadata != null)
    {
        helper.Add("metadata", metadata);
    }

    return helper;
}

static KVObject ToArray(Vector3 vector) => KVObject.Array([vector.X, vector.Y, vector.Z]);

// The classes of every game, written as the table the renderer shows entities with
void WriteClasses()
{
    Console.WriteLine($"Found {definitions.Count} classes in {gameTables.Count} games");

    // A class every game declares alike is written once under its name. Where games declare it differently, each
    // version is written under its name and the first game that declares it so, and no version is the plain one.
    var ids = new Dictionary<string, string>(StringComparer.Ordinal);

    foreach (var (name, versions) in definitions)
    {
        foreach (var version in versions)
        {
            ids.Add(version.Key, versions.Count == 1 ? name : string.Create(CultureInfo.InvariantCulture, $"{name}:{version.AppIds.Min}"));
        }
    }

    if (ids.Values.Distinct(StringComparer.Ordinal).Count() != ids.Count)
    {
        throw new InvalidOperationException("Two versions of a class were given the same id.");
    }

    KVObject MapClassIds(KVObject table)
    {
        var mapped = new KVObject();

        foreach (var (name, value) in table)
        {
            mapped.Add(name, name == "classes" ? KVObject.Array(table.GetArray("classes").Select(key => new KVObject(ids[(string)key]))) : value);
        }

        return mapped;
    }

    var classes = definitions
        .SelectMany(static versions => versions.Value)
        .Select(version =>
        {
            var id = ids[version.Key];

            if (id != version.Data.GetStringProperty("name"))
            {
                version.Data.Add("id", id);
            }

            return (Id: id, version.Data);
        })
        .OrderBy(static version => version.Id, StringComparer.Ordinal)
        .Select(static version => version.Data);

    var root = new KVObject
    {
        { "games", KVObject.Array(gameTables.Select(MapClassIds)) },
        { "classes", KVObject.Array(classes) },
    };

    // The comment goes after the header, which has to be the first line
    var text = root.ToKV3String();
    var headerEnd = text.IndexOf('\n', StringComparison.Ordinal) + 1;

    File.WriteAllText(classesPath, string.Concat(text.AsSpan(0, headerEnd), "// <auto-generated/>\n// Use Misc/VrfFgdParser project\n", text.AsSpan(headerEnd)));
}

static string GetSourcePath([CallerFilePath] string path = "") => path;

// A key of a block of keys as text, or null when it is not there
static string? GetText(KVObject? keys, string key)
    => keys != null && keys.ContainsKey(key) ? keys[key].ToString(CultureInfo.InvariantCulture) : null;

// The property names and trigger materials of every Source 2 game, whichever game declares them
void ParseFile(string file)
{
    var directory = Path.GetDirectoryName(file)!;
    var isSource2 = File.Exists(Path.Join(directory, "gameinfo.gi"));

    Console.WriteLine();
    Console.ForegroundColor = isSource2 ? ConsoleColor.Green : ConsoleColor.Blue;
    Console.Write("Parsing ");
    Console.Write(file);
    Console.WriteLine(isSource2 ? string.Empty : " (not Source 2)");
    Console.ResetColor();

    if (!isSource2)
    {
        return;
    }

    // Each file is read on its own, opening what it includes through the game it is in
    using var fileLoader = new GameFileLoader(null, file);
    var fgd = new ForgeGameData(fileLoader.GetFileStream);

    fgd.Load(file);

    foreach (var error in fgd.Errors)
    {
        Console.WriteLine($"{Path.GetFileName(error.FileName)}({error.Line}): {error.Message}");
    }

    foreach (var gameClass in fgd.Classes)
    {
        foreach (var variable in gameClass.Variables)
        {
            allProperties.Add(variable.Name.ToLowerInvariant());
        }

        if (GetText(gameClass.Metadata, "auto_apply_material") is { } material && material != "materials/tools/toolstrigger.vmat")
        {
            entityMaterials[gameClass.Name] = material;
        }
    }
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
