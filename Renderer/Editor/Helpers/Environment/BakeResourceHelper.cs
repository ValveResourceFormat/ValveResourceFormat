using System.Globalization;
using System.IO;
using ValveResourceFormat.IO;
using ValveResourceFormat.IO.Fgd;
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
    public override bool IsVisual => false;

    private const string LightProbeVolume = "ToolObjects/LightProbeVolume";
    private const string CubeMap = "ToolObjects/CubeMap";

    // Baked lighting only has minor versions within this major version
    private const int BakedLightingMajorVersion = 8;

    /// <summary>A key the entity names a baked resource by, with what the resource is called when the editor bakes it.</summary>
    /// <param name="Key">The key.</param>
    /// <param name="Extension">The extension of the resource.</param>
    /// <param name="Prefix">What the name of the resource starts with.</param>
    private readonly record struct BakedResource(string Key, string Extension, string Prefix);

    /// <summary>Gets the keys the entity names its baked resources by, none where the declaration lists unequal numbers of them.</summary>
    private List<BakedResource> Resources { get; } = [];

    /// <summary>Gets what is baked, such as <c>ToolObjects/LightProbeVolume</c>.</summary>
    private string? ToolObject => GetArgument(3);

    internal BakeResourceHelper(IHelperOwner owner, HelperDeclaration declaration) : base(owner, declaration)
    {
        var keys = SplitArgument(0);
        var extensions = SplitArgument(1);
        var prefixes = SplitArgument(2);

        if (keys.Length != extensions.Length || keys.Length != prefixes.Length)
        {
            return;
        }

        for (var i = 0; i < keys.Length; i++)
        {
            Resources.Add(new BakedResource(keys[i], extensions[i], prefixes[i]));
        }
    }

    private string[] SplitArgument(int index) => GetArgument(index)?.Split(',', StringSplitOptions.TrimEntries) ?? [];

    /// <inheritdoc/>
    public override void AddToScene()
    {
        var isLightProbeVolume = LightProbeVolume.Equals(ToolObject, StringComparison.OrdinalIgnoreCase);

        if (!isLightProbeVolume && !CubeMap.Equals(ToolObject, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var scene = Owner.Scene;
        var keyValues = GetBakedKeyValues(scene.RendererContext.FileLoader);

        // A map does not say which version its baked lighting is, so the first light probe tells by the textures it
        // was baked with
        if (isLightProbeVolume && scene.LightingInfo.LightmapVersionNumber == 0 && GetBakedVersion(keyValues) is { } gameVersion)
        {
            scene.LightingInfo.LightmapVersionNumber = BakedLightingMajorVersion;
            scene.LightingInfo.LightmapGameVersionNumber = gameVersion;
            scene.RenderAttributes["S_LIGHTMAP_VERSION_MINOR"] = (byte)gameVersion;
        }

        SceneNode? node;

        if (isLightProbeVolume)
        {
            // Only a combined volume, which bakes a cubemap too, fades out over its edges
            var fadesAtEdges = Owner.Classname.Equals("env_combined_light_probe_volume", StringComparison.OrdinalIgnoreCase);

            node = SceneLightingVolume.AddLightProbe(scene, keyValues, Owner.RigidTransform, Owner.LayerName, isSphere: false, fadesAtEdges);
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

    // The first version whose texture set a light probe has, see WorldLightingInfo.AddProbe: direct light shadows
    // from version 2, light indices and scalars in version 1. Irradiance alone is only read as RGBM, which the editor
    // does not bake, so it tells nothing.
    private static int? GetBakedVersion(EntityLump.Entity keyValues)
    {
        if (HasKey("lightprobetexture_dlshd"))
        {
            return 2;
        }

        if (HasKey("lightprobetexture_dli") && HasKey("lightprobetexture_dls"))
        {
            return 1;
        }

        return null;

        bool HasKey(string key) => keyValues.GetStringProperty(key) is { Length: > 0 };
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

    // Names one placement of the entity, the way the editor names what it bakes for it
    private static string GetPlacementName(PlacedMapNode placedNode)
    {
        var ids = placedNode.NodeIdPath;
        var parts = new string[ids.Count];

        for (var i = 0; i < ids.Count; i++)
        {
            parts[i] = ids[i].ToString(CultureInfo.InvariantCulture);
        }

        return string.Join('_', parts);
    }
}
