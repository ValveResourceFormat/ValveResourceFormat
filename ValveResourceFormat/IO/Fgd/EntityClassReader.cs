using System.IO;
using System.Linq;
using System.Reflection;
using ValveKeyValue;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.IO.Fgd;

/// <summary>
/// A game of an entity class table file: its app id, whether it is a GoldSrc game and the table of the classes it declares.
/// </summary>
internal sealed record EntityClassGame(int AppId, bool IsGoldSrc, EntityClassTable Table);

/// <summary>
/// Reads the entity class tables the libraries embed.
/// </summary>
internal static class EntityClassReader
{
    /// <summary>Reads a table file embedded in an assembly.</summary>
    public static KVObject ReadResource(Assembly assembly, string resourceName)
    {
        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidDataException($"Missing resource '{resourceName}'.");

        try
        {
            return KVDocumentExtensions.ParseKV3(stream).Root;
        }
        catch (Exception e) when (e is KeyValueException or FormatException)
        {
            throw new InvalidDataException($"'{resourceName}' cannot be read: {e.Message}", e);
        }
    }

    /// <summary>Reads the game tables of a table file, in the order of their app ids.</summary>
    public static IEnumerable<EntityClassGame> ReadTables(KVObject root)
    {
        // A class is kept once for each way games declare it: under its name where they all declare it alike, and
        // under an id naming a game that declares it so where they do not
        var definitions = new Dictionary<string, EntityClass>(StringComparer.Ordinal);

        foreach (var data in root.GetArray("classes") ?? [])
        {
            definitions.Add(data.GetStringProperty("id", data.GetStringProperty("name")), ReadClass(data));
        }

        foreach (var game in (root.GetArray("games") ?? []).OrderBy(static game => game.GetInt32Property("app_id")))
        {
            var classes = (game.GetArray("classes") ?? [])
                .Select(id => definitions.GetValueOrDefault((string)id) ?? throw new InvalidDataException($"Class '{id}' is named but not declared."))
                .ToList();

            var appId = game.GetInt32Property("app_id");

            yield return new EntityClassGame(appId, game.GetBooleanProperty("goldsrc"), new EntityClassTable(classes, ReadGridNav(game)));
        }
    }

    /// <summary>Merges the classes of several games. Of versions as many games declare, it is the one of the game with the lowest app id.</summary>
    public static IEnumerable<EntityClass> MergeClasses(List<EntityClass> classes)
        => classes
            .GroupBy(static entityClass => entityClass.Name, StringComparer.OrdinalIgnoreCase)
            .Select(static versions => versions.MaxBy(version => versions.Count(other => other == version))!);

    public static (int EdgeSize, int OffsetX, int OffsetY, int PickHeight)? ReadGridNav(KVObject game)
    {
        if (!game.TryGetValue("gridnav", out var gridNav))
        {
            return null;
        }

        return (gridNav.GetInt32Property("edge_size"), gridNav.GetInt32Property("offset_x"), gridNav.GetInt32Property("offset_y"), gridNav.GetInt32Property("pick_height"));
    }

    // A class as the tables keep it, where one without a kind is a point
    public static EntityClass ReadClass(KVObject data)
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
