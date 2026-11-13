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

HashSet<string> editorModelClasses =
[
    "info_player_counterterrorist",
    "info_player_terrorist",
    "info_deathmatch_spawn",
    "info_armsrace_counterterrorist",
    "info_armsrace_terrorist",
    "info_enemy_terrorist_spawn",
    "info_hostage_spawn",
    "point_scale_reference_human",
    "team_select",
    "end_of_match",
    "terrorist_team_intro",
    "terrorist_team_intro_variant2",
    "counterterrorist_team_intro",
    "counterterrorist_team_intro_variant2",
    "terrorist_wingman_intro",
    "counterterrorist_wingman_intro",
    "terrorist_rush_intro",
    "counterterrorist_rush_intro",
    "info_player_start_dota",
    "info_player_start_goodguys",
    "info_player_start_badguys",
    "info_roquelaire_perch",
    "func_nav_gen_proj",
    "prop_handpose",
];

// Overwrites the entity_classes.kv3 embedded in the library. Games not installed here keep the classes already in it.
var classesPath = Path.GetFullPath(Path.Join(Path.GetDirectoryName(GetSourcePath()), "..", "..", "ValveResourceFormat", "IO", "Fgd", "entity_classes.kv3"));
var previous = File.Exists(classesPath) ? KVDocumentExtensions.ParseKV3(classesPath).Root : new KVObject();
var previousDefinitions = (previous.GetArray("classes") ?? []).ToDictionary(static data => data.GetStringProperty("id", data.GetStringProperty("name")), StringComparer.Ordinal);
var previousTables = (previous.GetArray("games") ?? []).ToDictionary(static table => table.GetInt32Property("app_id"));

// Versions of each class by class name, with the app ids that declare that version. A version has a temporary key
// until every game is read and the final ids are known.
var definitions = new Dictionary<string, List<(string Text, string Key, KVObject Data, SortedSet<int> AppIds)>>(StringComparer.Ordinal);
var gameTables = new List<KVObject>();

var installed = games.ToDictionary(static game => game.AppID);
var appIds = installed.Keys.Union(previousTables.Keys).Order();

foreach (var appId in appIds)
{
    var table = installed.TryGetValue(appId, out var game) ? ParseGame(game) : null;

    // A game not installed with its FGD files keeps its previous table
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

return 0;

// Class table of an installed game, or null if no gameinfo.gi names FGD files that exist
KVObject? ParseGame(GameFolderLocator.SteamLibraryGameInfo game)
{
    Console.WriteLine($"Searching {game.AppID} {game.AppName} ({game.GamePath})");

    // Walk the folder once for both .fgd and gameinfo.gi files, as the walk takes most of the time
    var files = Directory.EnumerateFiles(game.GamePath, "*", SearchOption.AllDirectories)
        .Where(static path => path.EndsWith(".fgd", StringComparison.OrdinalIgnoreCase) || Path.GetFileName(path).Equals("gameinfo.gi", StringComparison.OrdinalIgnoreCase))
        .ToList();

    foreach (var file in files.Where(static path => path.EndsWith(".fgd", StringComparison.OrdinalIgnoreCase)))
    {
        ParseFile(file);
    }

    // Skip the gameinfo.gi of core and of addons. Classes shared by all games are read as part of each game, with
    // that game's changes, and an addon uses the classes of its game.
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

// Class table of one game, loaded through the Hammer block of its gameinfo.gi
KVObject? ParseGameClasses(GameFolderLocator.SteamLibraryGameInfo game, string gameInfo)
{
    if (LoadFgd(gameInfo) is not { } fgd)
    {
        return null;
    }

    // Names used as a required_parent_class. Only base classes with these names are stored.
    var requiredParentClasses = fgd.Classes
        .Select(static gameClass => gameClass.Metadata.GetStringProperty("required_parent_class"))
        .OfType<string>()
        .Where(static name => name.Length > 0)
        .ToHashSet(StringComparer.OrdinalIgnoreCase);

    var classIds = KVObject.Array();

    foreach (var gameClass in fgd.Classes)
    {
        if (ConvertClass(gameClass, requiredParentClasses) is { } data)
        {
            FixValveShit(data);
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

// Loads the FGD files named by the Hammer block of a gameinfo.gi, through the folders and packages the game mounts.
// Null if none of the named files exist.
static FgdFile? LoadFgd(string gameInfo)
{
    KVObject gameInfoData;

    using (var stream = File.OpenRead(gameInfo))
    {
        gameInfoData = KVSerializer.Create(KVSerializationFormat.KeyValues1Text).Deserialize(stream);
    }

    if (!gameInfoData.TryGetValue("Hammer", out var hammer) || hammer == null)
    {
        return null;
    }

    using var fileLoader = new GameFileLoader(null, gameInfo);

    // A game can name files it does not ship, which are not worth a warning
    var fgd = new FgdFile(file => fileLoader.FindFile(file, logNotFound: false) switch
    {
        { PathOnDisk: { } path } => File.OpenRead(path),
        { Package: { } package, PackageEntry: { } entry } => package.GetMemoryMappedStreamIfPossible(entry),
        _ => null,
    });

    return fgd.LoadGameFiles(hammer) ? fgd : null;
}

// Copies the previously written table of a game, adding each of its classes to definitions again
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

// Adds a previously written class to definitions and returns its key
string KeepDefinition(string previousId, int appId)
{
    var previousData = previousDefinitions.GetValueOrDefault(previousId)
        ?? throw new InvalidDataException($"'{classesPath}' names class '{previousId}', which it does not declare.");

    // Drop the id. A definition from an installed game gets its id last, in WriteClasses.
    var data = new KVObject();

    foreach (var (name, value) in previousData)
    {
        if (name != "id")
        {
            data.Add(name, value);
        }
    }

    FixValveShit(data);
    return AddDefinition(data.GetStringProperty("name"), data, appId);
}

// Adds the version of a class that a game declares and returns its key. Games that declare an identical version share the key.
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

// Editor kind of an entity of the class, with point the most common. Null for a class that is only a base class.
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

// Class data the renderer needs to draw an entity of the class. Null for a class that is only a base class.
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
        // A key of these types is drawn by a helper named after the type, with the key name as argument
        var helperName = variable.DeclaredType switch
        {
            FgdVariableType.VecLine => "vecline",
            FgdVariableType.Axis => "axis",
            FgdVariableType.LocalAxis => "localaxis",
            _ => null,
        };

        if (helperName != null)
        {
            // A key without a name gives the helper no argument
            helpers.Add(ConvertHelper(helperName, variable.Name.Length > 0 ? [variable.Name] : [], null));
        }
    }

    if (helpers.Count > 0)
    {
        data.Add("helpers", helpers);
    }

    if (gameClass.Metadata.GetStringProperty("required_parent_class") is { Length: > 0 } requiredParentClass)
    {
        data.Add("required_parent_class", requiredParentClass);
    }

    if (gameClass.Metadata.GetStringProperty("auto_apply_material") is { Length: > 0 } autoApplyMaterial)
    {
        data.Add("auto_apply_material", autoApplyMaterial);
    }

    // Keys that name the preview animation, ordered by descending preview_animation_priority and then by declaration
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

void FixValveShit(KVObject data)
{
    if (!editorModelClasses.Contains(data.GetStringProperty("name")) || data.GetArray("helpers") is not { } helpers)
    {
        return;
    }

    foreach (var helper in helpers)
    {
        if (helper.GetStringProperty("name") is not ("model" or "studio"))
        {
            continue;
        }

        helper["name"] = new KVObject("editormodel");

        // model("editormodel") names no model, so the entity's own is drawn
        if (helper.GetArray("arguments") is [{ } placeholder] && placeholder.ToString() == "editormodel")
        {
            helper.Remove("arguments");
        }
    }
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

// Writes the class definitions and the per-game tables to entity_classes.kv3
void WriteClasses()
{
    Console.WriteLine($"Found {definitions.Count} classes in {gameTables.Count} games");

    // A class that all games declare identically uses its name as id. Where games declare it differently, each
    // version uses "name:appid" with the lowest app id that declares it, and no version uses the plain name.
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

// Collects the lowercase property names of every Source 2 game, whichever game declares them
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

    // Each file is read separately, with its includes resolved through the game that contains it
    using var fileLoader = new GameFileLoader(null, file);
    var fgd = new FgdFile(fileLoader.GetFileStream);

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
    }
}

void WriteProperties()
{
    Console.WriteLine($"Found {allProperties.Count} unique properties");

    var propertiesString = new StringBuilder();

    foreach (var property in allProperties.Order())
    {
        propertiesString.Append('"');
        propertiesString.Append(property);
        propertiesString.Append('"');
        propertiesString.Append(',');
        propertiesString.AppendLine();
    }

    File.WriteAllText("properties.txt", propertiesString.ToString());
}
