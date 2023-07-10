using System.IO;
using System.Collections.Generic;
using ValveResourceFormat.IO.ContentFormats.ValveMap;
using ValveResourceFormat.Utils;
using ValveResourceFormat.ResourceTypes;
using System.Numerics;
using System;
using System.Text;
using ValveResourceFormat.Serialization;
using ValveResourceFormat.Blocks;
using System.Globalization;
using System.Linq;
using System.Diagnostics;

namespace ValveResourceFormat.IO;

public sealed class MapExtract
{
    public string LumpFolder { get; private set; }

    private IReadOnlyCollection<string> EntityLumpNames { get; set; }
    private IReadOnlyCollection<string> WorldNodeNames { get; set; }
    private string WorldPhysicsName { get; set; }
    private static (string Original, string Editable) WorldPhysicsNamesToExtract(string worldPhysicsName)
    {
        var original = Path.ChangeExtension(worldPhysicsName, ".vmdl");
        var editable = Path.GetDirectoryName(worldPhysicsName).Replace('\\', '/')
            + "/"
            + Path.GetFileNameWithoutExtension(worldPhysicsName)
            + "_edit.vmdl";

        return (original, editable);
    }

    private List<string> AssetReferences { get; } = new();
    private List<string> ModelsToExtract { get; } = new();
    private List<string> MeshesToExtract { get; } = new();
    private List<string> FolderExtractFilter { get; } = new();

    private List<CMapWorldLayer> WorldLayers { get; set; }
    private Dictionary<int, MapNode> UniqueNodeIds { get; set; }
    private CMapRootElement MapDocument { get; set; }

    private readonly IFileLoader FileLoader;

    public IProgress<string> ProgressReporter { get; set; }

    private readonly Dictionary<uint, string> HashTable = StringToken.InvertedTable;

    internal static class CommonHashes
    {
        public static readonly uint ClassName = StringToken.Get("classname");
        public static readonly uint Origin = StringToken.Get("origin");
        public static readonly uint Angles = StringToken.Get("angles");
        public static readonly uint Scales = StringToken.Get("scales");
        public static readonly uint HammerUniqueId = StringToken.Get("hammeruniqueid");
        public static readonly uint Model = StringToken.Get("model");
    }

    /// <summary>
    /// Extract a map from a resource. Accepted types include Map, World, WorldNode and EntityLump.
    /// </summary>
    public MapExtract(Resource resource, IFileLoader fileLoader)
    {
        FileLoader = fileLoader ?? throw new ArgumentNullException(nameof(fileLoader), "A file loader must be provided to load the map's lumps");

        switch (resource.ResourceType)
        {
            case ResourceType.Map:
                InitMapExtract(resource);
                break;
            case ResourceType.World:
                InitWorldExtract(resource);
                break;
            default:
                throw new InvalidDataException($"Resource type {resource.ResourceType} is not supported for map extraction.");
        }
    }

    /// <summary>
    /// Extract a map by name and a vpk-based file loader.
    /// </summary>
    /// <param name="mapNameFull"> Full name of map, including the 'maps' root. The lump folder. E.g. 'maps/prefabs/ui/ui_background'. </param>
    public MapExtract(string mapNameFull, IFileLoader fileLoader)
    {
        ArgumentNullException.ThrowIfNull(fileLoader, nameof(fileLoader));
        FileLoader = fileLoader;

        // Clean up any trailing slashes, or vmap_c extension
        var mapName = Path.GetFileNameWithoutExtension(mapNameFull);
        var mapRoot = Path.GetDirectoryName(mapNameFull);

        LumpFolder = mapRoot + "/" + mapName;

        var vmapPath = LumpFolder + ".vmap_c";
        var vmapResource = FileLoader.LoadFile(vmapPath);
        if (vmapResource is null)
        {
            throw new FileNotFoundException($"Failed to find vmap_c resource at {vmapPath}");
        }

        InitMapExtract(vmapResource);
    }

    private static bool PathIsSubPath(string equalOrSubPath, string path)
    {
        equalOrSubPath = equalOrSubPath.Replace('\\', '/').TrimEnd('/');
        path = path.Replace('\\', '/').TrimEnd('/');

        return equalOrSubPath.StartsWith(path, StringComparison.OrdinalIgnoreCase);
    }

    private void InitMapExtract(Resource vmapResource)
    {
        LumpFolder = GetLumpFolderFromVmapRERL(vmapResource.ExternalReferences);

        var worldPath = Path.Combine(LumpFolder, "world.vwrld_c");
        FolderExtractFilter.Add(worldPath);
        using var worldResource = FileLoader.LoadFile(worldPath);

        if (worldResource == null)
        {
            throw new FileNotFoundException($"Failed to find world resource, which is required for vmap_c extract, at {worldPath}");
        }

        InitWorldExtract(worldResource);
    }

    private static string GetLumpFolderFromVmapRERL(ResourceExtRefList rerl)
    {
        foreach (var info in rerl.ResourceRefInfoList)
        {
            if (info.Name.EndsWith("world.vrman", StringComparison.OrdinalIgnoreCase))
            {
                return GetLumpFolderFromWorldPath(info.Name);
            }
        }

        throw new InvalidDataException("Could not find world.vrman in vmap_c RERL.");
    }

    private static string GetLumpFolderFromWorldPath(string worldPath)
    {
        return Path.GetDirectoryName(worldPath);
    }

    private void InitWorldExtract(Resource vworld)
    {
        LumpFolder ??= GetLumpFolderFromWorldPath(vworld.FileName);

        var world = (World)vworld.DataBlock;
        EntityLumpNames = world.GetEntityLumpNames();
        WorldNodeNames = world.GetWorldNodeNames();

        WorldPhysicsName = GetWorldPhysicsName();
    }

    private string GetWorldPhysicsName()
    {
        var manifestFileName = Path.Combine(LumpFolder, "world_physics.vrman_c");
        var manifestResource = FileLoader.LoadFile(manifestFileName);

        var manifest = (ResourceManifest)manifestResource?.DataBlock;
        if (manifest == null || manifest.Resources.Count < 1)
        {
            return default;
        }

        return manifest.Resources.First().FirstOrDefault();
    }

    public PhysAggregateData LoadWorldPhysics()
    {
        if (WorldPhysicsName == null)
        {
            return default;
        }

        using var physicsResource = FileLoader.LoadFile(WorldPhysicsName + "_c");
        if (physicsResource == null)
        {
            return default;
        }

        return physicsResource.ResourceType switch
        {
            ResourceType.Model => ((Model)physicsResource.DataBlock).GetEmbeddedPhys(),
            ResourceType.PhysicsCollisionMesh => (PhysAggregateData)physicsResource.DataBlock,
            _ => throw new InvalidDataException($"Unexpected resource type {physicsResource.ResourceType} for world physics"),
        };
    }

    // TODO: Scan materials/tools folder
    public static readonly Dictionary<string, HashSet<string>> ToolTextureMultiTags = new()
    {
        ["clip"] = new HashSet<string> { "npcclip", "playerclip" },
        ["invisibleladder"] = new HashSet<string> { "ladder", "passbullets" },
    };

    public static string GetToolTextureNameFromCollisionTags(ModelExtract.SurfaceTagCombo combo)
    {
        var texture = ToolTextureMultiTags.FirstOrDefault(x => x.Value.SetEquals(combo.InteractAsStrings)).Key;
        var tag = combo.InteractAsStrings.FirstOrDefault();
        texture ??= tag switch
        {
            "playerclip" or "npcclip" => tag,
            "sky" => "skybox",
            "csgo_grenadeclip" => "grenadeclip",
            _ => "nodraw",
        };

        return $"materials/tools/tools{texture}.vmat";
    }

    public ContentFile ToContentFile()
    {
        var vmap = new ContentFile
        {
            Data = Encoding.UTF8.GetBytes(ToValveMap()),
            FileName = LumpFolder + ".vmap",
        };

        var physData = LoadWorldPhysics();
        if (physData != null)
        {
            FolderExtractFilter.Add(WorldPhysicsName + "_c"); // TODO: put vphys on vmdl.AdditionalFiles
            var physModelNames = WorldPhysicsNamesToExtract(WorldPhysicsName);

            var original = new ModelExtract(physData, physModelNames.Original).ToContentFile();
            var editable = new ModelExtract(physData, physModelNames.Editable)
            {
                Type = ModelExtract.ModelExtractType.Map_PhysicsToRenderMesh,
                PhysicsToRenderMaterialNameProvider = GetToolTextureNameFromCollisionTags,
            }
            .ToContentFile();
            vmap.AdditionalFiles.Add(original);
            vmap.AdditionalFiles.Add(editable);
        }

        foreach (var meshName in MeshesToExtract)
        {
            var meshNameCompiled = meshName + "_c";
            using var mesh = FileLoader.LoadFile(meshNameCompiled);
            if (mesh is not null)
            {
                var vmdl = new ModelExtract((Mesh)mesh.DataBlock, meshName).ToContentFile();
                FolderExtractFilter.Add(meshNameCompiled); // TODO: put vmesh on vmdl.AdditionalFiles
                vmap.AdditionalFiles.Add(vmdl);
            }
        }

        foreach (var modelName in ModelsToExtract)
        {
            using var model = FileLoader.LoadFile(modelName + "_c");
            if (model is not null)
            {
                var data = (Model)model.DataBlock;

                var hasMeshes = data.GetEmbeddedMeshesAndLoD().Any() || data.GetReferenceMeshNamesAndLoD().Any();
                var hasPhysics = data.GetEmbeddedPhys() is not null || data.GetReferencedPhysNames().Any();
                var isJustPhysics = hasPhysics && !hasMeshes;

                var modelExtract = new ModelExtract(data, FileLoader)
                {
                    Type = isJustPhysics
                        ? ModelExtract.ModelExtractType.Map_PhysicsToRenderMesh
                        : ModelExtract.ModelExtractType.Default,
                    PhysicsToRenderMaterialNameProvider = (_) => "materials/tools/toolstrigger.vmat",
                };

                var vmdl = modelExtract.ToContentFile();
                vmap.AdditionalFiles.Add(vmdl);
            }
        }

        // Add these files so they can be filtered out in folder extract
        vmap.AdditionalFiles.AddRange(FolderExtractFilter.Select(r => new ContentFile { FileName = r }));

        return vmap;
    }

    public string ToValveMap()
    {
        using var datamodel = new Datamodel.Datamodel("vmap", 29);

        datamodel.PrefixAttributes.Add("map_asset_references", AssetReferences);
        datamodel.Root = MapDocument = new();

        WorldLayers = new();
        UniqueNodeIds = new();

        if (!string.IsNullOrEmpty(WorldPhysicsName))
        {
            var physModelNames = WorldPhysicsNamesToExtract(WorldPhysicsName);

            MapDocument.World.Children.Add(new CMapEntity() { Name = "World Physics", EditorOnly = true }
                .WithClassName("prop_static")
                .WithProperty("model", physModelNames.Original)
            );

            MapDocument.World.Children.Add(new CMapEntity() { Name = "Editable World Physics" }
                .WithClassName("prop_static")
                .WithProperty("model", physModelNames.Editable)
            );
        }


        foreach (var worldNodeName in WorldNodeNames)
        {
            var worldNodeCompiled = worldNodeName + ".vwnod_c";
            FolderExtractFilter.Add(worldNodeCompiled);

            using var worldNode = FileLoader.LoadFile(worldNodeCompiled);
            if (worldNode is not null)
            {
                AddWorldNodesAsStaticProps((WorldNode)worldNode.DataBlock);
            }
        }

        foreach (var entityLumpName in EntityLumpNames)
        {
            var entityLumpCompiled = entityLumpName + "_c";
            FolderExtractFilter.Add(entityLumpCompiled);

            using var entityLumpResource = FileLoader.LoadFile(entityLumpCompiled);
            if (entityLumpResource is not null)
            {
                GatherEntitiesFromLump((EntityLump)entityLumpResource.DataBlock);
            }
        }

        using var stream = new MemoryStream();
        datamodel.Save(stream, "keyvalues2", 4);

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private void AddWorldNodesAsStaticProps(WorldNode node)
    {
        var layerNodes = new List<MapNode>(node.LayerNames.Count);
        foreach (var layerName in node.LayerNames)
        {
            if (layerName == "world_layer_base")
            {
                layerNodes.Add(MapDocument.World);
                continue;
            }

            var layer = new CMapWorldLayer { WorldLayerName = layerName };
            layerNodes.Add(layer);
            WorldLayers.Add(layer);
        }

        // Add any non-base world layer to the document
        foreach (var layerNode in layerNodes)
        {
            if (layerNode != MapDocument.World)
            {
                MapDocument.World.Children.Add(layerNode);
            }
        }

        static void AddChildMaybeGrouped(MapNode node, MapNode child, string groupName)
        {
            // Only group if there is a group name
            if (!string.IsNullOrEmpty(groupName))
            {
                if (!node.TryGetValue(groupName, out var group))
                {
                    group = new CMapGroup { Name = groupName };
                    node.Children.Add((CMapGroup)group);
                    // An easy way to keep track of these groups is adding them to the kv
                    // for later retrieval. Hammer ignores these properties.
                    // Otherwise we would have to search the node each time.
                    node[groupName] = group;
                }
                node = (CMapGroup)group;
            }
            node.Children.Add(child);
        }

        void StaticPropFinalize(BaseEntity propStatic, int layerIndex, List<MapNode> layerNodes, bool isBakedToWorld)
        {
            MapNode destNode = MapDocument.World;
            if (layerIndex > -1)
            {
                destNode = layerNodes[layerIndex];
            }

            var bakedGroup = isBakedToWorld && destNode == MapDocument.World
                ? "Baked World Models"
                : null;

            AddChildMaybeGrouped(destNode, propStatic, bakedGroup);
        }

        void SceneObjectToStaticProp(IKeyValueCollection sceneObject, int layerIndex, List<MapNode> layerNodes)
        {
            var modelName = sceneObject.GetProperty<string>("m_renderableModel");
            var meshName = sceneObject.GetProperty<string>("m_renderable");

            var objectFlags = sceneObject.GetEnumValue<ObjectTypeFlags>("m_nObjectTypeFlags", normalize: true);

            if (modelName is null)
            {
                Debug.Assert(!objectFlags.HasFlag(ObjectTypeFlags.Model));
                if (meshName is null)
                {
                    return;
                }

                MeshesToExtract.Add(meshName);
            }

            AssetReferences.Add(modelName);

            var propStatic = new CMapEntity()
                .WithClassName("prop_static")
                .WithProperty("model", modelName);

            var fadeStartDistance = sceneObject.GetProperty<double>("m_flFadeStartDistance");
            var fadeEndDistance = sceneObject.GetProperty<double>("m_flFadeEndDistance");
            //propStatic.EntityProperties["fademindist"] = fadeStartDistance.ToString(CultureInfo.InvariantCulture);
            //propStatic.EntityProperties["fademaxdist"] = fadeEndDistance.ToString(CultureInfo.InvariantCulture);

            var tintColor = sceneObject.GetSubCollection("m_vTintColor").ToVector4();
            //propStatic.EntityProperties["rendercolor"] = $"{tintColor.X} {tintColor.Y} {tintColor.Z}";
            //propStatic.EntityProperties["renderamt"] = tintColor.W.ToString(CultureInfo.InvariantCulture);

            var skin = sceneObject.GetProperty<string>("m_skin");
            if (!string.IsNullOrEmpty(skin))
            {
                propStatic.EntityProperties["skin"] = skin;
            }

            propStatic.EntityProperties["rendertocubemaps"] = StringBool(objectFlags.HasFlag(ObjectTypeFlags.RenderToCubemaps));
            propStatic.EntityProperties["disableshadows"] = StringBool(objectFlags.HasFlag(ObjectTypeFlags.NoShadows));

            var isEmbeddedModel = false;
            if (!objectFlags.HasFlag(ObjectTypeFlags.Model))
            {
                isEmbeddedModel = true;
                propStatic.EntityProperties["baketoworld"] = StringBool(true);
                ModelsToExtract.Add(modelName);
            }

            if (Path.GetFileName(modelName).Contains("nomerge", StringComparison.Ordinal))
            {
                propStatic.EntityProperties["disablemeshmerging"] = StringBool(true);
            }

            StaticPropFinalize(propStatic, layerIndex, layerNodes, isEmbeddedModel);
        }

        void AggregateToStaticProps(IKeyValueCollection agg, int layerIndex, List<MapNode> layerNodes)
        {
            var modelName = agg.GetProperty<string>("m_renderableModel");
            var anyFlags = agg.GetEnumValue<ObjectTypeFlags>("m_anyFlags", normalize: true);
            var allFlags = agg.GetEnumValue<ObjectTypeFlags>("m_allFlags", normalize: true);

            var propStatic = new CMapEntity()
                .WithClassName("prop_static")
                .WithProperty("model", modelName)
                .WithProperty("baketoworld", StringBool(true));

            AssetReferences.Add(modelName);
            ModelsToExtract.Add(modelName);

            // TODO: Split aggregate into fragment models

            StaticPropFinalize(propStatic, layerIndex, layerNodes, true);
        }

        for (var i = 0; i < node.SceneObjects.Count; i++)
        {
            var sceneObject = node.SceneObjects[i];
            var layerIndex = (int)(node.SceneObjectLayerIndices?[i] ?? -1);
            SceneObjectToStaticProp(sceneObject, layerIndex, layerNodes);
        }

        foreach (var aggregateSceneObject in node.AggregateSceneObjects)
        {
            var layerIndex = (int)aggregateSceneObject.GetIntegerProperty("m_nLayer");
            AggregateToStaticProps(aggregateSceneObject, layerIndex, layerNodes);
        }

        foreach (var clutterSceneObject in node.ClutterSceneObjects)
        {
            // TODO: Clutter
        }
    }

    #region Entities
    private void GatherEntitiesFromLump(EntityLump entityLump)
    {
        var lumpName = entityLump.Data.GetStringProperty("m_name");

        MapNode destNode = (string.IsNullOrEmpty(lumpName) || lumpName == "default_ents")
            ? MapDocument.World
            : WorldLayers.Find(l => l.WorldLayerName == lumpName) is CMapWorldLayer worldLayer
                ? worldLayer
                : new CMapGroup { Name = lumpName };

        // If destination is a group, add it to the document
        if (destNode is CMapGroup)
        {
            MapDocument.World.Children.Add(destNode);
        }

        foreach (var childLumpName in entityLump.GetChildEntityNames())
        {
            using var entityLumpResource = FileLoader.LoadFile(childLumpName + "_c");
            if (entityLumpResource is not null)
            {
                GatherEntitiesFromLump((EntityLump)entityLumpResource.DataBlock);
            }
        }

        foreach (var compiledEntity in entityLump.GetEntities())
        {
            FixUpEntityKeyValues(compiledEntity);

            if (compiledEntity.GetProperty<string>(CommonHashes.ClassName) == "worldspawn")
            {
                AddProperties(compiledEntity, MapDocument.World);
                MapDocument.World.EntityProperties["description"] = $"Decompiled with VRF 0.3.2 - https://vrf.steamdb.info/";
                continue;
            }

            var mapEntity = new CMapEntity();
            var entityLineage = AddProperties(compiledEntity, mapEntity);
            if (entityLineage.Length > 1)
            {
                foreach (var parentId in entityLineage[..^1])
                {
                    if (UniqueNodeIds.TryGetValue(parentId, out var existingNode))
                    {
                        destNode = existingNode;
                        continue;
                    }

                    var newDestNode = new CMapGroup { NodeID = parentId, Name = parentId.ToString(CultureInfo.InvariantCulture) };
                    UniqueNodeIds.Add(parentId, newDestNode);
                    destNode.Children.Add(newDestNode);
                    destNode = newDestNode;
                }
            }

            destNode.Children.Add(mapEntity);
        }
    }

    private void FixUpEntityKeyValues(EntityLump.Entity entity)
    {
        foreach (var (hash, property) in entity.Properties)
        {
            property.Name ??= HashTable.GetValueOrDefault(hash, $"{hash}");
        }
    }

    private int[] AddProperties(EntityLump.Entity compiledEntity, BaseEntity mapEntity)
    {
        var entityLineage = Array.Empty<int>();
        foreach (var (hash, property) in compiledEntity.Properties.Reverse())
        {
            if (property.Name is null)
            {
                continue;
            }

            if (TryHandleSpecialHash(hash, property, mapEntity, ref entityLineage))
            {
                continue;
            }

            var value = PropertyToEditString(property);

            if (hash == CommonHashes.Model && PathIsSubPath(value, LumpFolder))
            {
                ModelsToExtract.Add(value);
            }

            mapEntity.EntityProperties.Add(property.Name, value);
        }

        if (compiledEntity.Connections != null)
        {
            foreach (var connection in compiledEntity.Connections)
            {
                var dmeConnection = new DmeConnectionData
                {
                    OutputName = connection.GetProperty<string>("m_outputName"),
                    TargetType = connection.GetInt32Property("m_targetType"),
                    TargetName = connection.GetProperty<string>("m_targetName"),
                    InputName = connection.GetProperty<string>("m_inputName"),
                    OverrideParam = connection.GetProperty<string>("m_overrideParam"),
                    Delay = connection.GetFloatProperty("m_flDelay"),
                    TimesToFire = connection.GetInt32Property("m_nTimesToFire"),
                };

                mapEntity.ConnectionsData.Add(dmeConnection);
            }
        }

        return entityLineage;
    }

    private static bool TryHandleSpecialHash(uint hash, EntityLump.EntityProperty property, BaseEntity mapEntity, ref int[] lineage)
    {
        if (hash == CommonHashes.Origin)
        {
            mapEntity.Origin = GetVector3Property(property);
            return true;
        }
        else if (hash == CommonHashes.Angles)
        {
            mapEntity.Angles = GetVector3Property(property);
            return true;
        }
        else if (hash == CommonHashes.Scales)
        {
            mapEntity.Scales = GetVector3Property(property);
            return true;
        }
        else if (hash == CommonHashes.HammerUniqueId)
        {
            try
            {
                lineage = Array.ConvertAll(PropertyToEditString(property).Split(':'), int.Parse);
            }
            catch (FormatException)
            {
                // not essential, ignore
            }

            if (lineage.Length > 0)
            {
                mapEntity.NodeID = lineage[^1];
            }

            return true;
        }

        return false;
    }

    private static Vector3 GetVector3Property(EntityLump.EntityProperty property)
    {
        return property.Type switch
        {
            EntityFieldType.CString or EntityFieldType.String => EntityTransformHelper.ParseVector((string)property.Data),
            EntityFieldType.Vector or EntityFieldType.QAngle => (Vector3)property.Data,
            _ => throw new InvalidDataException("Unhandled Entity Vector Data Type!"),
        };
    }

    static string StringBool(bool value)
        => value ? "1" : "0";

    private static string PropertyToEditString(EntityLump.EntityProperty property)
    {
        var type = property.Type;
        var data = property.Data;
        return data switch
        {
            string str => str,
            bool boolean => StringBool(boolean),
            Vector3 vector => $"{vector.X} {vector.Y} {vector.Z}",
            byte[] color => $"{color[0]} {color[1]} {color[2]} {color[3]}",
            _ => data.ToString()
        };
    }
    #endregion Entities
}
