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
        var definitions = new Dictionary<string, EntityClass>(StringComparer.Ordinal);

        foreach (var data in generated.GetArray("classes") ?? [])
        {
            definitions.Add(data.GetStringProperty("id", data.GetStringProperty("name")), ReadClass(data));
        }

        var games = new Dictionary<int, EntityClassTable>();
        var gameClasses = new List<EntityClass>();
        (int, int, int, int)? mergedGridNav = null;

        foreach (var game in (generated.GetArray("games") ?? []).OrderBy(static game => game.GetInt32Property("app_id")))
        {
            var classes = (game.GetArray("classes") ?? [])
                .Select(id => definitions.GetValueOrDefault((string)id) ?? throw new InvalidDataException($"Class '{id}' is named but not declared."))
                .ToList();
            var gridNav = ReadGridNav(game);

            games.Add(game.GetInt32Property("app_id"), new EntityClassTable(classes, gridNav));
            gameClasses.AddRange(classes);
            mergedGridNav ??= gridNav;
        }

        // Of versions as many games declare, it is the one of the game with the lowest app id
        var mergedClasses = gameClasses
            .GroupBy(static entityClass => entityClass.Name, StringComparer.OrdinalIgnoreCase)
            .Select(static versions => versions.MaxBy(version => versions.Count(other => other == version))!);

        var extraClasses = (extra.GetArray("classes") ?? []).Select(ReadClass);

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

    // A class as the tables keep it, where one without a kind is a point
    private static EntityClass ReadClass(KVObject data)
    {
        var name = data.GetStringProperty("name");
        var mins = data.GetFloatArray("mins");
        var maxs = data.GetFloatArray("maxs");

        return new EntityClass
        {
            Name = name,
            Kind = data.GetStringProperty("kind", "point") switch
            {
                "point" => EntityClassKind.Point,
                "solid" => EntityClassKind.Solid,
                "path" => EntityClassKind.Path,
                "path_node" => EntityClassKind.PathNode,
                "cable" => EntityClassKind.Cable,
                var kind => throw new InvalidDataException($"Class '{name}' is of unknown kind '{kind}'."),
            },
            Size = mins is [var minX, var minY, var minZ] && maxs is [var maxX, var maxY, var maxZ]
                ? (new Vector3(minX, minY, minZ), new Vector3(maxX, maxY, maxZ))
                : null,
            Helpers = [.. (data.GetArray("helpers") ?? []).Select(static helper => new HelperDeclaration
            {
                Name = helper.GetStringProperty("name"),
                Arguments = [.. (helper.GetArray("arguments") ?? []).Select(static argument => (string)argument)],
                Metadata = helper.TryGetValue("metadata", out var metadata) ? metadata : null,
            })],
            PreviewAnimationKeys = [.. (data.GetArray("preview_animation_keys") ?? []).Select(static key => (string)key)],
            BaseClasses = (data.GetArray("base_classes") ?? []).Select(static baseClass => (string)baseClass).ToHashSet(StringComparer.OrdinalIgnoreCase),
            RequiredParentClass = data.GetStringProperty("required_parent_class"),
            AutoApplyMaterial = data.GetStringProperty("auto_apply_material"),
        };
    }
}
