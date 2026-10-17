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
        Console.WriteLine($"Keeping the classes of {appId} {previousTable.GetStringProperty("name")}, which is not installed with its FGD files");
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

    var classIds = KVObject.Array();
    var ids = new Dictionary<FgdClass, string>();

    foreach (var gameClass in fgd.Classes)
    {
        if (GetKind(gameClass) is not (null or "base"))
        {
            classIds.Add(AddClass(gameClass, game.AppID, ids));
        }
    }

    Console.WriteLine($"Found {classIds.Count} classes in {Path.GetDirectoryName(gameInfo)}");

    var table = new KVObject
    {
        { "app_id", game.AppID },
        { "name", game.AppName },
    };

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
    var ids = new Dictionary<string, string>(StringComparer.Ordinal);
    var table = new KVObject();

    foreach (var (name, value) in previousTable)
    {
        table.Add(name, name == "classes"
            ? KVObject.Array((previousTable.GetArray("classes") ?? []).Select(id => new KVObject(KeepDefinition((string)id, appId, ids))))
            : value);
    }

    return table;
}

// A class as it was written before, under the id it is given among the classes written now
string KeepDefinition(string previousId, int appId, Dictionary<string, string> ids)
{
    if (ids.TryGetValue(previousId, out var id))
    {
        return id;
    }

    var previousData = previousDefinitions.GetValueOrDefault(previousId)
        ?? throw new InvalidDataException($"'{classesPath}' names class '{previousId}', which it does not declare.");

    // It is written as it would be by a game that is installed, which gives it its id last
    var data = new KVObject();

    foreach (var (name, value) in previousData)
    {
        if (name == "id")
        {
            continue;
        }

        data.Add(name, name == "bases"
            ? KVObject.Array((previousData.GetArray("bases") ?? []).Select(baseId => new KVObject(KeepDefinition((string)baseId, appId, ids))))
            : value);
    }

    id = AddDefinition(data.GetStringProperty("name"), data, appId);
    ids.Add(previousId, id);
    return id;
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

// The id of a class and of every class it is based on, added the first time a game names them
string AddClass(FgdClass gameClass, int appId, Dictionary<FgdClass, string> ids)
{
    if (ids.TryGetValue(gameClass, out var id))
    {
        return id;
    }

    var baseIds = gameClass.BaseClasses.Select(baseClass => AddClass(baseClass, appId, ids)).ToList();

    id = AddDefinition(gameClass.Name, ConvertClass(gameClass, baseIds), appId);
    ids.Add(gameClass, id);
    return id;
}

static string? GetKind(FgdClass gameClass) => gameClass.Type switch
{
    FgdClassType.BaseClass => "base",
    FgdClassType.PointClass or FgdClassType.NpcClass or FgdClassType.FilterClass or FgdClassType.MoveClass
        or FgdClassType.KeyFrameClass or FgdClassType.NavLinkClass or FgdClassType.ChoreoAnchorClass => "point",
    FgdClassType.SolidClass => "solid",
    FgdClassType.PathClass => "path",
    FgdClassType.PathNodeClass => "path_node",
    FgdClassType.CableClass => "cable",
    _ => null,
};

// A class as the renderer reads it. Its keys, inputs and outputs are only the ones it changes of what the
// classes it is based on give it, and the renderer takes the rest from those, so each is written once.
static KVObject ConvertClass(FgdClass gameClass, List<string> baseIds)
{
    var kind = GetKind(gameClass)!;
    var data = new KVObject
    {
        { "name", gameClass.Name },
        { "kind", kind },
    };

    if (kind != "base")
    {
        AddDrawnData(gameClass, data);
    }

    if (baseIds.Count > 0)
    {
        data.Add("bases", KVObject.Array(baseIds.Select(static id => new KVObject(id))));
    }

    AddOwnEntries(gameClass.Name, data, "keys",
        InheritEntries(gameClass.BaseClasses.Select(static baseClass => ConvertKeys(baseClass.Variables)), laterBaseWins: true),
        ConvertKeys(gameClass.Variables), asObject: true);

    AddOwnEntries(gameClass.Name, data, "inputs",
        InheritEntries(gameClass.BaseClasses.Select(static baseClass => ConvertInputsOutputs(baseClass.Inputs)), laterBaseWins: false),
        ConvertInputsOutputs(gameClass.Inputs), asObject: false);

    AddOwnEntries(gameClass.Name, data, "outputs",
        InheritEntries(gameClass.BaseClasses.Select(static baseClass => ConvertInputsOutputs(baseClass.Outputs)), laterBaseWins: false),
        ConvertInputsOutputs(gameClass.Outputs), asObject: false);

    return data;
}

// What an entity of the class is drawn with, which is written whole for each class as it is small
static void AddDrawnData(FgdClass gameClass, KVObject data)
{
    if (gameClass.Description.Length > 0)
    {
        data.Add("description", gameClass.Description);
    }

    if (gameClass.HasColor)
    {
        data.Add("color", KVObject.Array([gameClass.Color.R, gameClass.Color.G, gameClass.Color.B]));
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
}

// A key without a name is never read
static List<(string Name, KVObject Data)> ConvertKeys(List<FgdVariable> variables)
    => [.. variables.Where(static variable => variable.Name.Length > 0).Select(static variable => (variable.Name, Data: ConvertKey(variable)))];

static List<(string Name, KVObject Data)> ConvertInputsOutputs(List<FgdInputOutput> inputsOutputs)
    => [.. inputsOutputs.Select(static inputOutput => (inputOutput.Name, Data: ConvertInputOutput(inputOutput)))];

// What a class takes from the classes it is based on, in their order. Of a name several of them have, a key is
// the one of the last, and an input or an output the one of the first.
static List<(string Name, KVObject Data)> InheritEntries(IEnumerable<List<(string Name, KVObject Data)>> bases, bool laterBaseWins)
{
    var entries = new List<(string Name, KVObject Data)>();

    foreach (var baseEntries in bases)
    {
        foreach (var entry in baseEntries)
        {
            var index = entries.FindIndex(other => other.Name.Equals(entry.Name, StringComparison.OrdinalIgnoreCase));

            if (index < 0)
            {
                entries.Add(entry);
            }
            else if (laterBaseWins)
            {
                entries[index] = entry;
            }
        }
    }

    return entries;
}

// The entries of a class that are not what it takes from its bases: the ones it adds or changes, which replace
// an inherited one of their name where there is one, and the names of the inherited ones it drops
static void AddOwnEntries(string className, KVObject data, string name, List<(string Name, KVObject Data)> inherited, List<(string Name, KVObject Data)> entries, bool asObject)
{
    var own = new List<(string Name, KVObject Data)>();

    foreach (var entry in entries)
    {
        var index = inherited.FindIndex(other => other.Name.Equals(entry.Name, StringComparison.OrdinalIgnoreCase));

        if (index < 0 || inherited[index].Name != entry.Name || inherited[index].Data.ToKV3String() != entry.Data.ToKV3String())
        {
            own.Add(entry);
        }
    }

    var removed = inherited
        .Where(other => !entries.Exists(entry => entry.Name.Equals(other.Name, StringComparison.OrdinalIgnoreCase)))
        .Select(static entry => entry.Name)
        .ToList();

    // The renderer has to end up with the entries the class has, in their order
    var resolved = new List<(string Name, KVObject Data)>(inherited);
    resolved.RemoveAll(entry => removed.Contains(entry.Name, StringComparer.OrdinalIgnoreCase));

    foreach (var entry in own)
    {
        var index = resolved.FindIndex(other => other.Name.Equals(entry.Name, StringComparison.OrdinalIgnoreCase));

        if (index < 0)
        {
            resolved.Add(entry);
        }
        else
        {
            resolved[index] = entry;
        }
    }

    if (!resolved.Select(static entry => entry.Name).SequenceEqual(entries.Select(static entry => entry.Name)))
    {
        throw new InvalidOperationException($"The {name} of '{className}' cannot be written as what it changes of its bases.");
    }

    if (own.Count > 0)
    {
        data.Add(name, asObject
            ? KVObject.Collection(own.Select(static entry => KeyValuePair.Create(entry.Name, entry.Data)))
            : KVObject.Array(own.Select(static entry => entry.Data)));
    }

    if (removed.Count > 0)
    {
        data.Add($"removed_{name}", KVObject.Array(removed.Select(static entry => new KVObject(entry))));
    }
}

// Everything the class declares of a key, as its properties are shown in the editor
static KVObject ConvertKey(FgdVariable variable)
{
    var key = new KVObject { { "type", variable.TypeName } };

    if (variable.SubType.Length > 0)
    {
        key.Add("sub_type", variable.SubType);
    }

    if (variable.LongName.Length > 0)
    {
        key.Add("long_name", variable.LongName);
    }

    if (variable.Description.Length > 0 && variable.Description != variable.Name)
    {
        key.Add("description", variable.Description);
    }

    if (variable.HasDefault)
    {
        key.Add("default", variable.DefaultString);
    }

    if (variable.Choices.Count > 0)
    {
        key.Add("choices", KVObject.Array(variable.Choices.Select(choice =>
        {
            var data = new KVObject { { "value", choice.ValueText } };

            if (choice.Caption.Length > 0)
            {
                data.Add("caption", choice.Caption);
            }

            if (choice.Description.Length > 0)
            {
                data.Add("description", choice.Description);
            }

            if (choice.IsDefault)
            {
                data.Add("default", true);
            }

            return data;
        })));
    }

    if (variable.Metadata.Count > 0)
    {
        key.Add("metadata", variable.Metadata);
    }

    return key;
}

// Where an input or output was declared is left out, as it is not the same for every game
static KVObject ConvertInputOutput(FgdInputOutput inputOutput)
{
    var data = new KVObject
    {
        { "name", inputOutput.Name },
        { "type", inputOutput.Type.ToString().ToLowerInvariant() },
    };

    if (inputOutput.IsApi)
    {
        data.Add("api", true);
    }

    if (inputOutput.Description.Length > 0)
    {
        data.Add("description", inputOutput.Description);
    }

    var metadata = new KVObject();

    foreach (var (name, value) in inputOutput.Metadata)
    {
        if (!name.StartsWith("originating_", StringComparison.Ordinal))
        {
            metadata.Add(name, value);
        }
    }

    if (metadata.Count > 0)
    {
        data.Add("metadata", metadata);
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

    KVObject MapIds(KVObject data, string arrayName)
    {
        var mapped = new KVObject();

        foreach (var (name, value) in data)
        {
            mapped.Add(name, name == arrayName ? KVObject.Array(data.GetArray(arrayName).Select(key => new KVObject(ids[(string)key]))) : value);
        }

        return mapped;
    }

    var classes = definitions
        .SelectMany(static versions => versions.Value)
        .Select(version =>
        {
            var data = MapIds(version.Data, "bases");
            var id = ids[version.Key];

            if (id != data.GetStringProperty("name"))
            {
                data.Add("id", id);
            }

            return (Id: id, Data: data);
        })
        .OrderBy(static version => version.Id, StringComparer.Ordinal)
        .Select(static version => version.Data);

    var root = new KVObject
    {
        { "games", KVObject.Array(gameTables.Select(table => MapIds(table, "classes"))) },
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
