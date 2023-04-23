using System.IO;
using System.Collections.Generic;
using ValveResourceFormat.IO.Formats.ValveMap;
using ValveResourceFormat.Utils;
using ValveResourceFormat.ResourceTypes;
using System.Numerics;
using System;
using System.Text;
using ValveResourceFormat.Serialization;
using ValveResourceFormat.Blocks;
using System.Globalization;
using System.Linq;

namespace ValveResourceFormat.IO;

public sealed class MapExtract
{
    public string LumpFolder { get; private set; }

    private IReadOnlyCollection<string> EntityLumpNames { get; set; }
    private IReadOnlyCollection<string> WorldNodeNames { get; set; }
    private PhysAggregateData WorldPhysics { get; set; }

    private List<string> AssetReferences { get; } = new();
    private List<string> FilesForExtract { get; } = new();

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

    private static bool PathIsEqualOrSubPath(string equalOrSubPath, string path)
    {
        equalOrSubPath = equalOrSubPath.Replace('\\', '/').TrimEnd('/');
        path = path.Replace('\\', '/').TrimEnd('/');

        return path.EndsWith(equalOrSubPath, StringComparison.OrdinalIgnoreCase);
    }

    private void InitMapExtract(Resource vmapResource)
    {
        LumpFolder = GetLumpFolderFromVmapRERL(vmapResource.ExternalReferences);

        // TODO: make these progressreporter warnings
        // Not required, but good sanity checks
        var mapName = Path.GetFileNameWithoutExtension(vmapResource.FileName);
        var rootDir = Path.GetDirectoryName(vmapResource.FileName);
        if (!string.IsNullOrEmpty(rootDir) && !PathIsEqualOrSubPath(LumpFolder, rootDir + "/" + mapName))
        {
            throw new InvalidDataException(
                "vmap_c filename does not match or have the RERL-derived lump folder as a subpath. " +
                "Make sure to load the resource from the correct location inside vpk.\n" +
                $"\tLump folder: {LumpFolder}\n\tResource filename: {vmapResource.FileName}"
            );
        }

        var worldPath = Path.Combine(LumpFolder, "world.vwrld_c");
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

        var physicsPath = Path.Combine(LumpFolder, "world_physics.vphys_c");
        using var physResource = FileLoader.LoadFile(physicsPath);
        WorldPhysics = (PhysAggregateData)physResource?.DataBlock;
    }

    public ContentFile ToContentFile()
    {
        var vmap = new ContentFile
        {
            Data = Encoding.UTF8.GetBytes(ToValveMap()),
        };

        vmap.RequiredGameFiles.AddRange(FilesForExtract);

        return vmap;
    }

    public string ToValveMap()
    {
        using var datamodel = new Datamodel.Datamodel("vmap", 29);

        datamodel.PrefixAttributes.Add("map_asset_references", AssetReferences);
        datamodel.Root = MapDocument = new();

        WorldLayers = new();
        UniqueNodeIds = new();

        if (WorldPhysics is not null)
        {
            PhyiscsToMapMesh(WorldPhysics);
        }

        foreach (var worldNodeName in WorldNodeNames)
        {
            using var worldNode = FileLoader.LoadFile(worldNodeName + ".vwnod_c");
            if (worldNode is not null)
            {
                AddWorldNodesAsStaticProps((WorldNode)worldNode.DataBlock);
            }
        }

        foreach (var entityLumpName in EntityLumpNames)
        {
            using var entityLumpResource = FileLoader.LoadFile(entityLumpName + "_c");
            if (entityLumpResource is not null)
            {
                GatherEntitiesFromLump((EntityLump)entityLumpResource.DataBlock);
            }
        }

        using var stream = new MemoryStream();
        datamodel.Save(stream, "keyvalues2", 4);

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private void PhyiscsToMapMesh(PhysAggregateData physData)
    {
        if (physData.Parts.Length == 0)
        {
            return;
        }

        var physMeshes = physData.Parts[0].Shape.Meshes;
        var collisionAttributes = physData.CollisionAttributes;

        foreach (var mesh in physMeshes)
        {
            var attributes = collisionAttributes[mesh.CollisionAttributeIndex];
            var tags = attributes.GetArray<string>("m_InteractAsStrings") ?? attributes.GetArray<string>("m_PhysicsTagStrings");

            if (!tags.Contains("sky"))
            {
                continue;
            }

            var skyboxGroup = new CMapGroup { Name = "toolsskybox mesh" };

            // A skybox mesh
            foreach (var tri in mesh.Shape.Triangles)
            {
                var builder = new HammerMeshBuilder();

                builder.AddFace("materials/tools/toolsskybox.vmat", new Vector3[]
                {
                    mesh.Shape.Vertices[tri.Indices[0]],
                    mesh.Shape.Vertices[tri.Indices[1]],
                    mesh.Shape.Vertices[tri.Indices[2]],
                });

                // One triangle per mesh for now, since we don't have a way to join edges
                var mapMesh = builder.GenerateMesh();
                skyboxGroup.Children.Add(new CMapMesh { MeshData = mapMesh });
            }

            if (skyboxGroup.Children.Count > 0)
            {
                MapDocument.World.Children.Add(skyboxGroup);
            }
        }
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

        void SceneObjectToStaticProp(IKeyValueCollection sceneObject, int layerIndex, List<MapNode> layerNodes, bool isAggregate)
        {
            var modelName = sceneObject.GetProperty<string>("m_renderableModel");
            var meshName = sceneObject.GetProperty<string>("m_renderable");

            if (isAggregate)
            {
                return;
            }

            var objectFlags = ObjectTypeFlags.None;
            try
            {
                objectFlags = (ObjectTypeFlags)sceneObject.GetProperty<int>("m_nObjectTypeFlags");
            }
            catch (InvalidCastException)
            {
                // TODO: Parse from string
            }

            if (modelName is null)
            {
                // TODO: Generate model for mesh
                return;
            }

            var propStatic = new CMapEntity();
            propStatic.EntityProperties["classname"] = "prop_static";
            propStatic.EntityProperties["model"] = modelName;

            if (!isAggregate)
            {
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
            }

            if ((objectFlags & ObjectTypeFlags.RenderToCubemaps) != 0)
            {
                propStatic.EntityProperties["rendertocubemaps"] = "1";
            }

            if ((objectFlags & ObjectTypeFlags.NoShadows) != 0)
            {
                propStatic.EntityProperties["disableshadows"] = "1";
            }

            var isEmbeddedModel = true;
            if ((objectFlags & ObjectTypeFlags.Model) != 0)
            {
                isEmbeddedModel = false;
            }

            if (Path.GetFileName(modelName).Contains("nomerge", StringComparison.Ordinal))
            {
                propStatic.EntityProperties["disablemeshmerging"] = "1";
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

            MapNode destNode = MapDocument.World;
            if (layerIndex > -1)
            {
                destNode = layerNodes[layerIndex];
            }

            // Only create a group on the base layer (don't want non-unique names)
            var bakedGroup = isEmbeddedModel && destNode == MapDocument.World
                ? "Baked World Models"
                : null;

            AddChildMaybeGrouped(destNode, propStatic, bakedGroup);
        }

        for (var i = 0; i < node.SceneObjects.Count; i++)
        {
            var sceneObject = node.SceneObjects[i];
            var layerIndex = (int)(node.SceneObjectLayerIndices?[i] ?? -1);
            SceneObjectToStaticProp(sceneObject, layerIndex, layerNodes, isAggregate: false);
        }

        // TODO: Aggregates should be separated.
        foreach (var aggregateSceneObject in node.AggregateSceneObjects)
        {
            var layerIndex = (int)aggregateSceneObject.GetIntegerProperty("m_nLayer");
            SceneObjectToStaticProp(aggregateSceneObject, layerIndex, layerNodes, isAggregate: true);
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

    private static int[] AddProperties(EntityLump.Entity compiledEntity, BaseEntity mapEntity)
    {
        var entityLineage = Array.Empty<int>();
        foreach (var (hash, property) in compiledEntity.Properties)
        {
            if (property.Name is null)
            {
                continue;
            }

            if (TryHandleSpecialHash(hash, property, mapEntity, ref entityLineage))
            {
                continue;
            }

            mapEntity.EntityProperties.Add(property.Name, PropertyToEditString(property));
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

    private static string PropertyToEditString(EntityLump.EntityProperty property)
    {
        var type = property.Type;
        var data = property.Data;
        return data switch
        {
            string str => str,
            bool boolean => boolean ? "1" : "0",
            Vector3 vector => $"{vector.X} {vector.Y} {vector.Z}",
            byte[] color => $"{color[0]} {color[1]} {color[2]} {color[3]}",
            _ => data.ToString()
        };
    }
    #endregion Entities
}
