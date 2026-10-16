using System.Globalization;
using System.IO;
using ValveResourceFormat.IO;
using ValveResourceFormat.IO.Fgd;
using ValveResourceFormat.Renderer.Editor.Map;
using ValveResourceFormat.Renderer.SceneEnvironment;
using ValveResourceFormat.ResourceTypes;
using MapNode = ValveResourceFormat.IO.ContentFormats.ValveMap.MapNode;

namespace ValveResourceFormat.Renderer.Editor.Helpers;

/// <summary>
/// What an entity gets baked for it when the map is built: the light probe of a light probe volume, or the
/// cubemap of a cubemap, which the scene is lit with. A key the entity leaves empty names what the editor
/// last baked for it in the bake cache of the map.
/// </summary>
public sealed class BakeResourceHelper : EntityHelper
{
    /// <inheritdoc/>
    public override bool IsVisual => false;

    private const string LightProbeVolume = "ToolObjects/LightProbeVolume";
    private const string CubeMap = "ToolObjects/CubeMap";

    /// <summary>A key the entity names a baked resource by, with what the resource is called when the editor bakes it.</summary>
    /// <param name="Key">The key.</param>
    /// <param name="Extension">The extension of the resource.</param>
    /// <param name="Prefix">What the name of the resource starts with.</param>
    public readonly record struct BakedResource(string Key, string Extension, string Prefix);

    /// <summary>Gets the keys the entity names its baked resources by, none where the declaration lists unequal numbers of them.</summary>
    public IReadOnlyList<BakedResource> Resources { get; }

    /// <summary>Gets what is baked, such as <c>ToolObjects/LightProbeVolume</c>.</summary>
    public string? ToolObject => GetArgument(3);

    /// <summary>Initializes a baked resource of an entity.</summary>
    /// <param name="owner">The entity.</param>
    /// <param name="declaration">The helper as the class of the entity declares it.</param>
    public BakeResourceHelper(IHelperOwner owner, FgdHelper declaration) : base(owner, declaration)
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
        var keyValues = GetBakedKeyValues();
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

    // The keys of the entity, with the ones it leaves empty naming what the editor bakes for it
    private EntityLump.Entity GetBakedKeyValues()
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

            if (baked == null)
            {
                baked = new EntityLump.Entity { ParentLump = keyValues.ParentLump };

                foreach (var (name, value) in keyValues.Children)
                {
                    baked.Add(name, value);
                }
            }

            var fileName = placement.Length > 0 ? $"{resource.Prefix}_{placement}.{resource.Extension}" : $"{resource.Prefix}.{resource.Extension}";
            baked[resource.Key.ToLowerInvariant()] = $"{folder}/{fileName}";
        }

        return baked ?? keyValues;
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

    // The node IDs of the instances and prefabs the entity was placed through, then of the entity itself
    private static string GetPlacementName(PlacedMapNode placedNode)
    {
        var nodeIds = new List<int>(placedNode.InstancePath);

        if (placedNode.Node is MapNode node)
        {
            nodeIds.Add(node.NodeID);
        }

        return string.Join('_', nodeIds.ConvertAll(static id => id.ToString(CultureInfo.InvariantCulture)));
    }
}
