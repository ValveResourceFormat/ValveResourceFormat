using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;
using ValveKeyValue;
using ValveResourceFormat.IO;
using ValveResourceFormat.Renderer.Editor.Helpers;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.Renderer.Editor.Entities;

/// <summary>
/// Gives the entity classes a map is shown with, from the tables built into the renderer. Each game whose
/// classes are known has a table of its own, found by the Steam app it is installed as. Any other game is
/// shown with the classes of every known game, each the version most of them declare, and the ones kept by
/// hand for entities none of them declares.
/// </summary>
public static class EntityClassCatalog
{
    private const string ResourcePrefix = "Renderer.Resources.";
    private const string GeneratedFileName = "entity_classes.kv3";
    private const string ExtraFileName = "entity_classes_extra.kv3";

    private static readonly Lazy<(Dictionary<int, EntityClassTable> Games, EntityClassTable Merged)> BuiltIn = new(LoadBuiltIn);
    private static readonly ConditionalWeakTable<IFileLoader, EntityClassTable> Loaded = [];

    /// <summary>
    /// Gets the entity classes of the game a file loader has found. They are found once for a file loader, so
    /// everything that shows its entities shares them.
    /// </summary>
    /// <param name="fileLoader">The file loader of the map.</param>
    /// <param name="logger">Where it is said which classes are used.</param>
    /// <returns>The classes of the game, or the ones of every game where it is not one whose classes are known.</returns>
    public static EntityClassTable Get(IFileLoader fileLoader, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(fileLoader);
        ArgumentNullException.ThrowIfNull(logger);

        return Loaded.GetValue(fileLoader, loader => FindTable(loader, logger));
    }

    /// <summary>Gets the entity classes of a game by the Steam app it is installed as.</summary>
    /// <param name="appId">The app id, or zero where it is not known.</param>
    /// <returns>The classes of the game, or the ones of every game where it is not one whose classes are known.</returns>
    /// <exception cref="InvalidDataException">The built in classes are missing or cannot be read.</exception>
    public static EntityClassTable GetTable(int appId)
    {
        var (games, merged) = BuiltIn.Value;

        return games.GetValueOrDefault(appId) ?? merged;
    }

    private static EntityClassTable FindTable(IFileLoader fileLoader, ILogger logger)
    {
        var appId = fileLoader is GameFileLoader gameFileLoader ? gameFileLoader.InstalledSteamAppId : 0;

        if (BuiltIn.Value.Games.TryGetValue(appId, out var table))
        {
            logger.LogInformation("Entities are shown with the {Count} classes of Steam app {AppId}", table.Classes.Count, appId);
            return table;
        }

        logger.LogInformation("The entity classes of the game are not known, entities are shown with the classes of every game");
        return BuiltIn.Value.Merged;
    }

    private static (Dictionary<int, EntityClassTable> Games, EntityClassTable Merged) LoadBuiltIn()
    {
        var generated = ReadResource(GeneratedFileName);
        var extra = ReadResource(ExtraFileName);

        // A class is kept once for each way games declare it: under its name where they all declare it alike, and
        // under an id naming a game that declares it so where they do not
        var definitions = new Dictionary<string, KVObject>(StringComparer.Ordinal);

        foreach (var data in generated.GetArray("classes") ?? [])
        {
            definitions.Add(data.GetStringProperty("id", data.GetStringProperty("name")), data);
        }

        var reader = new ClassReader(definitions);
        var games = new Dictionary<int, EntityClassTable>();
        var gameClasses = new List<EntityClass>();
        (int, int, int, int)? mergedGridNav = null;

        foreach (var game in (generated.GetArray("games") ?? []).OrderBy(static game => game.GetInt32Property("app_id")))
        {
            var classes = (game.GetArray("classes") ?? []).Select(id => reader.Read((string)id)).ToList();
            var gridNav = ReadGridNav(game);

            games.Add(game.GetInt32Property("app_id"), new EntityClassTable(classes, gridNav));
            gameClasses.AddRange(classes);
            mergedGridNav ??= gridNav;
        }

        // Of versions as many games declare, it is the one of the game with the lowest app id
        var mergedClasses = gameClasses
            .GroupBy(static entityClass => entityClass.Name, StringComparer.OrdinalIgnoreCase)
            .Select(static versions => versions.MaxBy(version => versions.Count(other => other == version))!);

        var extraClasses = (extra.GetArray("classes") ?? []).Select(reader.ReadClass);

        return (games, new EntityClassTable(mergedClasses.Concat(extraClasses), mergedGridNav));
    }

    private static KVObject ReadResource(string fileName)
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(ResourcePrefix + fileName)
            ?? throw new InvalidDataException($"Missing resource '{fileName}'.");

        try
        {
            return KVDocumentExtensions.ParseKV3(stream).Root;
        }
        catch (Exception e) when (e is KeyValueException or FormatException)
        {
            throw new InvalidDataException($"'{fileName}' cannot be read: {e.Message}", e);
        }
    }

    private static (int EdgeSize, int OffsetX, int OffsetY, int PickHeight)? ReadGridNav(KVObject game)
    {
        if (!game.TryGetValue("gridnav", out var gridNav))
        {
            return null;
        }

        return (gridNav.GetInt32Property("edge_size"), gridNav.GetInt32Property("offset_x"), gridNav.GetInt32Property("offset_y"), gridNav.GetInt32Property("pick_height"));
    }

    /// <summary>
    /// Reads classes as the tables keep them: a class names the ones it is based on, and has only the keys it
    /// adds or changes of what they give it, and the names of the ones it drops. A key it adds goes last, and one
    /// it changes stays where the classes it is based on put it, the last of them where several have it.
    /// </summary>
    private sealed class ClassReader(Dictionary<string, KVObject> definitions)
    {
        private readonly Dictionary<string, EntityClass> classes = new(StringComparer.Ordinal);
        private readonly Dictionary<string, List<EntityClassKey>> keys = new(StringComparer.Ordinal);
        private readonly Dictionary<string, HashSet<string>> baseClasses = new(StringComparer.Ordinal);

        public EntityClass Read(string id)
        {
            if (!classes.TryGetValue(id, out var entityClass))
            {
                entityClass = CreateClass(Find(id), GetKeys(id), GetBaseClasses(id));
                classes.Add(id, entityClass);
            }

            return entityClass;
        }

        public EntityClass ReadClass(KVObject data) => CreateClass(data, ResolveKeys(data), ResolveBaseClasses(data));

        private static EntityClass CreateClass(KVObject data, List<EntityClassKey> keys, HashSet<string> baseClasses)
        {
            var name = data.GetStringProperty("name");
            var color = data.GetIntegerArray("color");
            var mins = data.GetFloatArray("mins");
            var maxs = data.GetFloatArray("maxs");

            return new EntityClass
            {
                Name = name,
                Kind = data.GetStringProperty("kind") switch
                {
                    "point" => EntityClassKind.Point,
                    "solid" => EntityClassKind.Solid,
                    "path" => EntityClassKind.Path,
                    "path_node" => EntityClassKind.PathNode,
                    "cable" => EntityClassKind.Cable,
                    var kind => throw new InvalidDataException($"Class '{name}' is of unknown kind '{kind}'."),
                },
                Color = color is [var r, var g, var b] ? new Color32((byte)r, (byte)g, (byte)b) : null,
                Size = mins is [var minX, var minY, var minZ] && maxs is [var maxX, var maxY, var maxZ]
                    ? (new Vector3(minX, minY, minZ), new Vector3(maxX, maxY, maxZ))
                    : null,
                Helpers = [.. (data.GetArray("helpers") ?? []).Select(static helper => new HelperDeclaration
                {
                    Name = helper.GetStringProperty("name"),
                    Arguments = [.. (helper.GetArray("arguments") ?? []).Select(static argument => (string)argument)],
                    Metadata = helper.TryGetValue("metadata", out var metadata) ? metadata : null,
                })],
                Keys = keys,
                BaseClasses = baseClasses,
                RequiredParentClass = data.GetStringProperty("required_parent_class"),
            };
        }

        private KVObject Find(string id)
            => definitions.TryGetValue(id, out var data) ? data : throw new InvalidDataException($"Class '{id}' is named but not declared.");

        private static IEnumerable<string> GetBaseIds(KVObject data) => (data.GetArray("bases") ?? []).Select(static id => (string)id);

        private List<EntityClassKey> GetKeys(string id)
        {
            if (!keys.TryGetValue(id, out var resolved))
            {
                resolved = ResolveKeys(Find(id));
                keys.Add(id, resolved);
            }

            return resolved;
        }

        private List<EntityClassKey> ResolveKeys(KVObject data)
        {
            var resolved = new List<EntityClassKey>();

            foreach (var baseId in GetBaseIds(data))
            {
                foreach (var key in GetKeys(baseId))
                {
                    SetKey(resolved, key);
                }
            }

            if (data.GetArray("removed_keys") is { } removed)
            {
                resolved.RemoveAll(key => removed.Any(name => key.Name.Equals((string)name, StringComparison.OrdinalIgnoreCase)));
            }

            if (data.TryGetValue("keys", out var own))
            {
                foreach (var (name, key) in own)
                {
                    SetKey(resolved, new EntityClassKey
                    {
                        Name = name,
                        Default = key.GetStringProperty("default"),
                        Metadata = key.TryGetValue("metadata", out var metadata) ? metadata : null,
                    });
                }
            }

            return resolved;
        }

        private static void SetKey(List<EntityClassKey> keys, EntityClassKey key)
        {
            var index = keys.FindIndex(other => other.Name.Equals(key.Name, StringComparison.OrdinalIgnoreCase));

            if (index < 0)
            {
                keys.Add(key);
            }
            else
            {
                keys[index] = key;
            }
        }

        private HashSet<string> GetBaseClasses(string id)
        {
            if (!baseClasses.TryGetValue(id, out var resolved))
            {
                resolved = ResolveBaseClasses(Find(id));
                baseClasses.Add(id, resolved);
            }

            return resolved;
        }

        private HashSet<string> ResolveBaseClasses(KVObject data)
        {
            var resolved = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var baseId in GetBaseIds(data))
            {
                resolved.Add(Find(baseId).GetStringProperty("name"));
                resolved.UnionWith(GetBaseClasses(baseId));
            }

            return resolved;
        }
    }
}
