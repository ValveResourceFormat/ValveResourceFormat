using System.Globalization;
using System.IO;
using System.Linq;
using ValveResourceFormat.IO;
using ValveResourceFormat.Renderer.Editor.Gizmos;
using ValveResourceFormat.Renderer.Editor.Map;
using ValveResourceFormat.Renderer.SceneEnvironment;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.Renderer.Editor.Helpers;

/// <summary>
/// What an entity gets baked for it when the map is built: the light probe of a light probe volume, or the
/// cubemap of a cubemap, which the scene is lit with. A key the entity leaves empty names what the editor
/// last baked for it in the bake cache of the map.
/// </summary>
public sealed class BakeResourceHelper : EntityHelper
{
    /// <inheritdoc/>
    public override GizmoTransformModes SupportedTransformModes => GizmoTransformModes.Translate | GizmoTransformModes.Rotate;

    /// <inheritdoc/>
    public override bool IsVisual => false;

    private const string LightProbeVolume = "ToolObjects/LightProbeVolume";
    private const string CubeMap = "ToolObjects/CubeMap";
    private const int BakedLightingVersion = 8;

    /// <summary>A key the entity names a baked resource by, with what the resource is called when the editor bakes it.</summary>
    /// <param name="Key">The key.</param>
    /// <param name="Extension">The extension of the resource.</param>
    /// <param name="Prefix">What the name of the resource starts with.</param>
    private readonly record struct BakedResource(string Key, string Extension, string Prefix);

    /// <summary>Gets the keys the entity names its baked resources by, none where the declaration lists unequal numbers of them.</summary>
    private IReadOnlyList<BakedResource> Resources { get; }

    /// <summary>Gets what is baked, such as <c>ToolObjects/LightProbeVolume</c>.</summary>
    private string? ToolObject => GetArgument(3);

    internal BakeResourceHelper(IHelperOwner owner, HelperDeclaration declaration) : base(owner, declaration)
    {
        static string[] Split(string? list) => list?.Split(',', StringSplitOptions.TrimEntries) ?? [];

        var keys = Split(GetArgument(0));
        var extensions = Split(GetArgument(1));
        var prefixes = Split(GetArgument(2));
        var resources = new List<BakedResource>();

        if (keys.Length == extensions.Length && keys.Length == prefixes.Length)
        {
            for (var i = 0; i < keys.Length; i++)
            {
                resources.Add(new BakedResource(keys[i], extensions[i], prefixes[i]));
            }
        }

        Resources = resources;
    }

    /// <inheritdoc/>
    public override void AddToScene()
    {
        var isLightProbeVolume = LightProbeVolume.Equals(ToolObject, StringComparison.OrdinalIgnoreCase);

        if (!isLightProbeVolume && !CubeMap.Equals(ToolObject, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var scene = Owner.Scene;
        var fileLoader = scene.RendererContext.FileLoader;
        var keyValues = GetBakedKeyValues(fileLoader);

        // What the editor bakes is what the game's shaders read, and the game only reads baked lighting of this
        // version, at the minor version it builds. A map with nothing baked, or a game that does not say which
        // version it builds, keeps the lighting it has without
        if (scene.LightingInfo.LightmapVersionNumber == 0 && HasBakedResource(keyValues)
            && fileLoader.GetGameInfoInt("ResourceCompiler/BakedLighting/Version", -1) is >= 0 and var gameVersion)
        {
            scene.LightingInfo.SetLightmapVersion(BakedLightingVersion, gameVersion);
        }

        SceneNode? node;

        if (isLightProbeVolume)
        {
            node = SceneLightingVolume.AddLightProbe(scene, keyValues, Owner.RigidTransform, Owner.LayerName, isSphere: false);
        }
        else
        {
            // Only env_cubemap has a sphere of influence, the other classes have a box
            var isSphere = Owner.Classname.Equals("env_cubemap", StringComparison.OrdinalIgnoreCase);

            node = SceneLightingVolume.AddEnvironmentMap(scene, keyValues, Owner.RigidTransform, Owner.LayerName, isSphere);
        }

        // The nodes belong to the entity, not to the keys they were read from
        if (node != null)
        {
            node.EntityData = Owner.KeyValues;
        }
    }

    // The keys of the entity, with the ones it leaves empty naming what the editor last baked for it, where that exists
    private EntityLump.Entity GetBakedKeyValues(GameFileLoader fileLoader)
    {
        var keyValues = Owner.KeyValues;

        if (Owner is not MapEntity mapEntity || GetBakeCacheFolder(mapEntity.PlacedNode) is not { } folder)
        {
            return keyValues;
        }

        EntityLump.Entity? baked = null;
        var placement = GetPlacementName(mapEntity.PlacedNode);

        foreach (var resource in Resources)
        {
            if (GetStringKey(resource.Key).Length > 0)
            {
                continue;
            }

            var fileName = placement.Length > 0 ? $"{resource.Prefix}_{placement}.{resource.Extension}" : $"{resource.Prefix}.{resource.Extension}";
            var path = $"{folder}/{fileName}";

            // A volume added since the last bake, or a resource the game does not bake, has nothing in the cache
            if (fileLoader.FindFile(path + GameFileLoader.CompiledFileSuffix, logNotFound: false) is { PathOnDisk: null, PackageEntry: null })
            {
                continue;
            }

            if (baked == null)
            {
                baked = new EntityLump.Entity { ParentLump = keyValues.ParentLump };

                foreach (var (name, value) in keyValues.Children)
                {
                    baked.Add(name, value);
                }
            }

            baked[resource.Key.ToLowerInvariant()] = path;
        }

        return baked ?? keyValues;
    }

    // Whether the keys name anything baked for the entity, by the entity itself or in the bake cache
    private bool HasBakedResource(EntityLump.Entity keyValues)
    {
        foreach (var resource in Resources)
        {
            if (keyValues.GetStringProperty(resource.Key.ToLowerInvariant()) is { Length: > 0 })
            {
                return true;
            }
        }

        return false;
    }

    // Baked resources are cached in a folder named after the map being edited, relative to its content root
    private static string? GetBakeCacheFolder(PlacedMapNode placedNode)
    {
        if (placedNode.RootDocument is not { FileName: { } fileName } document || document.FindContentRoot() is not { } contentRoot)
        {
            return null;
        }

        var mapPath = Path.ChangeExtension(Path.GetRelativePath(contentRoot, fileName), null).Replace('\\', '/');

        return $"_bakeresourcecache/{mapPath}_baked";
    }

    private static string GetPlacementName(PlacedMapNode placedNode)
        => string.Join('_', placedNode.NodeIdPath.Select(static id => id.ToString(CultureInfo.InvariantCulture)));
}
