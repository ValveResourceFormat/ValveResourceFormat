using System.Globalization;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;
using ValveKeyValue;
using ValveResourceFormat.GoldSrc;
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

// The tables the libraries embed are written over, and the games not installed here keep the classes they have of them
var sourceFolder = Path.Join(Path.GetDirectoryName(GetSourcePath()), "..", "..");
var source2File = new ClassFile(Path.GetFullPath(Path.Join(sourceFolder, "ValveResourceFormat", "IO", "Fgd", "entity_classes.kv3")));
var source1File = new ClassFile(Path.GetFullPath(Path.Join(sourceFolder, "ValveResourceFormat.Source1", "Entities", "entity_classes.kv3")));
var previousTables = source2File.PreviousTables.Concat(source1File.PreviousTables).ToDictionary();

var parsedTables = new Dictionary<int, KVObject>();

foreach (var game in games)
{
    foreach (var gameTable in ParseGame(game))
    {
        parsedTables.TryAdd(gameTable.GetInt32Property("app_id"), gameTable);
    }
}

var appIds = parsedTables.Keys.Union(previousTables.Keys).Order();

foreach (var appId in appIds)
{
    var table = parsedTables.GetValueOrDefault(appId);

    // A game whose tools are not installed here keeps what was found of it before
    if (table == null && previousTables.TryGetValue(appId, out var previousTable))
    {
        Console.WriteLine($"Keeping the classes of {appId}, which is not installed with its FGD files");
        table = KeepTable(previousTable);
    }

    if (table != null)
    {
        GetFile(GetFamily(table)).GameTables.Add(table);
    }
}

Console.WriteLine();

WriteClasses(source2File);
WriteClasses(source1File);
WriteProperties();

return 0;

// The tables of an installed game, none where its gameinfo files name no FGD files that are there
List<KVObject> ParseGame(GameFolderLocator.SteamLibraryGameInfo game)
{
    Console.WriteLine($"Searching {game.AppID} {game.AppName} ({game.GamePath})");

    // The folder is walked once for all of them, as most of the time goes to walking it
    var files = Directory.EnumerateFiles(game.GamePath, "*", SearchOption.AllDirectories)
        .Where(static path => path.EndsWith(".fgd", StringComparison.OrdinalIgnoreCase)
            || Path.GetFileName(path).Equals("gameinfo.gi", StringComparison.OrdinalIgnoreCase)
            || Path.GetFileName(path).Equals("gameinfo.txt", StringComparison.OrdinalIgnoreCase)
            || Path.GetFileName(path).Equals("liblist.gam", StringComparison.OrdinalIgnoreCase))
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
            return [table];
        }
    }

    var source1Tables = ParseSource1Game(game, files);

    return source1Tables.Count > 0 ? source1Tables : ParseGoldSrcGame(files);
}

// The tables of a GoldSrc game, one for each mod folder that has a liblist.gam and FGD files next to it
List<KVObject> ParseGoldSrcGame(List<string> files)
{
    var tables = new List<KVObject>();

    foreach (var liblist in files.Where(static path => Path.GetFileName(path).Equals("liblist.gam", StringComparison.OrdinalIgnoreCase)).Order(StringComparer.Ordinal))
    {
        var modFolder = Path.GetDirectoryName(liblist)!;
        var appId = GoldSrcGameApps.GetAppId(Path.GetFileName(modFolder));

        // The FGD files GoldSrc games ship are partial, so a table written before is kept as it is
        if (appId == 0 || previousTables.ContainsKey(appId) || tables.Exists(table => table.GetInt32Property("app_id") == appId))
        {
            continue;
        }

        var fgdPaths = files.Where(path => path.EndsWith(".fgd", StringComparison.OrdinalIgnoreCase)
            && string.Equals(Path.GetDirectoryName(path), modFolder, StringComparison.OrdinalIgnoreCase)).Order(StringComparer.Ordinal).ToList();

        if (fgdPaths.Count == 0)
        {
            continue;
        }

        if (LoadFgdFiles(fgdPaths) is { } fgd)
        {
            tables.Add(ConvertTable(fgd, appId, modFolder, GameFamily.GoldSrc));
        }
    }

    return tables;
}

// The tables of a Source 1 game, one for each app id its mods name, as the folders of a game hold mods of several
List<KVObject> ParseSource1Game(GameFolderLocator.SteamLibraryGameInfo game, List<string> files)
{
    var tables = new List<KVObject>();

    var gameInfos = files
        .Where(static path => Path.GetFileName(path).Equals("gameinfo.txt", StringComparison.OrdinalIgnoreCase))
        .Order(StringComparer.Ordinal);

    foreach (var gameInfo in gameInfos)
    {
        var data = ReadGameInfo(gameInfo);
        var appId = GetSource1AppId(data) ?? game.AppID;

        if (tables.Exists(table => table.GetInt32Property("app_id") == appId))
        {
            continue;
        }

        if (LoadSource1Fgd(gameInfo, data) is { } fgd)
        {
            tables.Add(ConvertTable(fgd, appId, Path.GetDirectoryName(gameInfo)!, GameFamily.Source1));
        }
    }

    return tables;
}

static KVObject ReadGameInfo(string gameInfo)
{
    using var stream = File.OpenRead(gameInfo);

    return KVSerializer.Create(KVSerializationFormat.KeyValues1Text).Deserialize(stream);
}

// A key of a block of a Source 1 gameinfo, which is not written with one case in every game
static KVObject? FindKey(KVObject block, string key)
{
    foreach (var (name, value) in block)
    {
        if (name.Equals(key, StringComparison.OrdinalIgnoreCase))
        {
            return value;
        }
    }

    return null;
}

static int? GetSource1AppId(KVObject gameInfo)
{
    var appId = FindKey(gameInfo, "FileSystem") is { } fileSystem ? FindKey(fileSystem, "SteamAppId") : null;

    return int.TryParse(appId?.ToString(CultureInfo.InvariantCulture), NumberStyles.Integer, CultureInfo.InvariantCulture, out var id) ? id : null;
}

// The FGD files Hammer loads for a Source 1 game, which are next to its tools: the one its gameinfo names, else the
// ones no other of them includes
static FgdFile? LoadSource1Fgd(string gameInfo, KVObject gameInfoData)
{
    var modFolder = Path.GetDirectoryName(Path.GetFullPath(gameInfo))!;
    var toolsFolder = Path.Join(Path.GetDirectoryName(modFolder), "bin");

    if (!Directory.Exists(toolsFolder))
    {
        return null;
    }

    List<string> paths;

    if (FindKey(gameInfoData, "GameData")?.ToString(CultureInfo.InvariantCulture) is { Length: > 0 } gameData)
    {
        paths = [Path.Join(toolsFolder, gameData)];
    }
    else
    {
        paths = [.. Directory.EnumerateFiles(toolsFolder, "*.fgd").Order(StringComparer.OrdinalIgnoreCase)];

        var included = paths
            .SelectMany(static path => Regex.Matches(File.ReadAllText(path), @"@include\s+""([^""]+)""", RegexOptions.IgnoreCase))
            .Select(static match => Path.GetFileName(match.Groups[1].Value))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        paths.RemoveAll(path => included.Contains(Path.GetFileName(path)));
    }

    var existingPaths = paths.FindAll(File.Exists);

    if (existingPaths.Count == 0)
    {
        return null;
    }

    return LoadFgdFiles(existingPaths);
}

static FgdFile? LoadFgdFiles(List<string> paths)
{
    var fgdFiles = new KVObject();

    foreach (var path in paths)
    {
        fgdFiles.Add(path, true);
    }

    var fgd = new FgdFile();

    return fgd.LoadGameFiles(new KVObject { { "fgd_files", fgdFiles } }) ? fgd : null;
}

// The classes of one game as Hammer builds them
KVObject? ParseGameClasses(GameFolderLocator.SteamLibraryGameInfo game, string gameInfo)
{
    if (LoadFgd(gameInfo) is not { } fgd)
    {
        return null;
    }

    return ConvertTable(fgd, game.AppID, Path.GetDirectoryName(gameInfo)!, GameFamily.Source2);
}

KVObject ConvertTable(FgdFile fgd, int appId, string location, GameFamily family)
{
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
            FixValveShit(data);
            classIds.Add(AddDefinition(gameClass.Name, data, appId, family));
        }
    }

    Console.WriteLine($"Found {classIds.Count} classes of {appId} in {location}");

    var table = new KVObject { { "app_id", appId } };

    // Source 1 games are merged with each other and not with the Source 2 games
    if (family == GameFamily.Source1)
    {
        table.Add("source1", true);
    }

    if (family == GameFamily.GoldSrc)
    {
        table.Add("goldsrc", true);
    }

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

// The classes of the FGD files the Hammer block of a gameinfo names, opened through the folders and packages the
// game mounts, or null where it names none that are there
static FgdFile? LoadFgd(string gameInfo)
{
    if (!ReadGameInfo(gameInfo).TryGetValue("Hammer", out var hammer) || hammer == null)
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

// The table of a game as it was written before, with its classes added again among the ones written now
KVObject KeepTable(KVObject previousTable)
{
    var appId = previousTable.GetInt32Property("app_id");
    var family = GetFamily(previousTable);
    var table = new KVObject();

    foreach (var (name, value) in previousTable)
    {
        table.Add(name, name == "classes"
            ? KVObject.Array((previousTable.GetArray("classes") ?? []).Select(id => new KVObject(KeepDefinition((string)id, appId, family))))
            : value);
    }

    return table;
}

// A class as it was written before, under the id it is given among the classes written now
string KeepDefinition(string previousId, int appId, GameFamily family)
{
    var file = GetFile(family);
    var previousData = file.PreviousDefinitions.GetValueOrDefault(previousId)
        ?? throw new InvalidDataException($"'{file.Path}' names class '{previousId}', which it does not declare.");

    // It is written as it would be by a game that is installed, which gives it its id last
    var data = new KVObject();

    foreach (var (name, value) in previousData)
    {
        if (name != "id")
        {
            data.Add(name, value);
        }
    }

    FixValveShit(data);
    return AddDefinition(data.GetStringProperty("name"), data, appId, family);
}

// The key of the version of a class a game declares, which is shared by every game that declares it alike
string AddDefinition(string name, KVObject data, int appId, GameFamily family)
{
    var text = data.ToKV3String();
    var definitions = GetFile(family).Definitions;

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

// The classes of every game, written as the table the renderer shows entities with
void WriteClasses(ClassFile file)
{
    var definitions = file.Definitions;

    Console.WriteLine($"Found {definitions.Count} classes in {file.GameTables.Count} games for {file.Path}");

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
        { "games", KVObject.Array(file.GameTables.Select(MapClassIds)) },
        { "classes", KVObject.Array(classes) },
    };

    // The comment goes after the header, which has to be the first line
    var text = root.ToKV3String();
    var headerEnd = text.IndexOf('\n', StringComparison.Ordinal) + 1;

    File.WriteAllText(file.Path, string.Concat(text.AsSpan(0, headerEnd), "// <auto-generated/>\n// Use Misc/VrfFgdParser project\n", text.AsSpan(headerEnd)));
}

static string GetSourcePath([CallerFilePath] string path = "") => path;

// Source 2 games are written to the table of the core library, Source 1 and GoldSrc games to the one of the Source 1 library
ClassFile GetFile(GameFamily family) => family == GameFamily.Source2 ? source2File : source1File;

static GameFamily GetFamily(KVObject table)
{
    if (table.GetBooleanProperty("goldsrc"))
    {
        return GameFamily.GoldSrc;
    }

    return table.GetBooleanProperty("source1") ? GameFamily.Source1 : GameFamily.Source2;
}

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

enum GameFamily
{
    Source2,
    Source1,
    GoldSrc,
}

// A table file with the tables and classes written to it
sealed class ClassFile
{
    public ClassFile(string path)
    {
        Path = path;

        var previous = File.Exists(path) ? KVDocumentExtensions.ParseKV3(path).Root : new KVObject();
        PreviousDefinitions = (previous.GetArray("classes") ?? []).ToDictionary(static data => data.GetStringProperty("id", data.GetStringProperty("name")), StringComparer.Ordinal);
        PreviousTables = (previous.GetArray("games") ?? []).ToDictionary(static table => table.GetInt32Property("app_id"));
    }

    public string Path { get; }

    public Dictionary<string, KVObject> PreviousDefinitions { get; }

    public Dictionary<int, KVObject> PreviousTables { get; }

    // Every version of each class by its name, with the games that declare it so, under a key of its own until
    // every game is read and the ids it is written under are known
    public Dictionary<string, List<(string Text, string Key, KVObject Data, SortedSet<int> AppIds)>> Definitions { get; } = new(StringComparer.Ordinal);

    public List<KVObject> GameTables { get; } = [];
}
