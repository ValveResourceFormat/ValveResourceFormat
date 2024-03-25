using SkiaSharp;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Reflection;
using ValveResourceFormat.Blocks;
using ValveResourceFormat.IO.ContentFormats.DmxModel;
using ValveResourceFormat.IO.ContentFormats.ValveMap;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.Serialization;
using ValveResourceFormat.Serialization.KeyValues;
using ValveResourceFormat.Utils;
using static System.Net.Mime.MediaTypeNames;
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

    private List<string> AssetReferences { get; } = [];
    private List<string> ModelsToExtract { get; } = [];
    private HashSet<string> ProceduralPhysMaterialsToExtract { get; } = [];
    private List<ContentFile> PreExportedFragments { get; } = [];
    private Dictionary<string, string> ModelEntityAssociations { get; } = [];
    private List<string> MeshesToExtract { get; } = [];
    private List<string> FolderExtractFilter { get; } = [];
    private List<string> SnapshotsToExtract { get; } = [];

    private List<CMapWorldLayer> WorldLayers { get; set; }
    private Dictionary<int, MapNode> UniqueNodeIds { get; set; }
    private CMapRootElement MapDocument { get; set; }

    private readonly IFileLoader FileLoader;

    public IProgress<string> ProgressReporter { get; set; }
    public PhysicsVertexMatcher PhysVertexMatcher { get; private set; }

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
    /// Extract a map from a resource. Accepted types include Map, World. TODO: WorldNode and EntityLump.
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
                throw new InvalidDataException($"Resource type {resource.ResourceType} is not supported in {nameof(MapExtract)}.");
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
        var vmapResource = FileLoader.LoadFile(vmapPath) ?? throw new FileNotFoundException($"Failed to find vmap_c resource at {vmapPath}");
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

        var worldPath = Path.Combine(LumpFolder, "world.vwrld");
        FolderExtractFilter.Add(worldPath);
        using var worldResource = FileLoader.LoadFileCompiled(worldPath) ??
            throw new FileNotFoundException($"Failed to find world resource, which is required for vmap_c extract, at {worldPath}");
        InitWorldExtract(worldResource);
    }

    public static string GetLumpFolderFromVmapRERL(ResourceExtRefList rerl)
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

        using var physicsResource = FileLoader.LoadFileCompiled(WorldPhysicsName);
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

    // TODO: we should be parsing fgds and collision_*.txt files from game to remain correct.
    public static readonly Dictionary<string, HashSet<string>> ToolTextureMultiTags = new()
    {
        ["clip"] = ["npcclip", "playerclip"],
        ["invisibleladder"] = ["ladder", "passbullets"],
    };

    public static string GetToolTextureNameForCollisionTags(ModelExtract.SurfaceTagCombo combo)
    {
        var shortenedToolTextureName = GetToolTextureShortenedName_ForInteractStrings(combo.InteractAsStrings);

        return $"materials/tools/tools{shortenedToolTextureName}.vmat";
    }

    public static string GetToolTextureShortenedName_ForInteractStrings(HashSet<string> interactAsStrings)
    {
        var texture = ToolTextureMultiTags.FirstOrDefault(x => x.Value.SetEquals(interactAsStrings)).Key;
        var tag = interactAsStrings.FirstOrDefault();
        texture ??= tag switch
        {
            "playerclip" or "npcclip" or "blocksound" => tag,
            "sky" => "skybox",
            "csgo_grenadeclip" => "grenadeclip",
            "ladder" => "invisibleladder",
            _ => "nodraw",
        };
        return texture;
    }

    // These appear in FGD as "auto_apply_material"
    public static string GetToolTextureForEntity(string entityClassName)
    {
        return entityClassName switch
        {
            "env_cs_place" => "materials/tools/tools_cs_place.vmat",
            "func_nav_blocker" => "materials/tools/toolsnavattribute.vmat",
            "func_nav_markup" => "materials/tools/toolsnavattribute.vmat",
            "post_processing_volume" => "materials/tools_postprocess_volume.vmat",
            "trigger_no_wards" => "materials/tools/tools_no_wards.vmat",
            _ => "materials/tools/toolstrigger.vmat",
        };
    }

    public ContentFile ToContentFile()
    {
        var vmap = new ContentFile
        {
            Data = ToValveMap(),
            FileName = LumpFolder + "_d.vmap",
        };

        var physData = LoadWorldPhysics();
        if (physData != null)
        {
            FolderExtractFilter.Add(WorldPhysicsName + GameFileLoader.CompiledFileSuffix); // TODO: put vphys on vmdl.AdditionalFiles
            var physModelNames = WorldPhysicsNamesToExtract(WorldPhysicsName);

            //var original = new ModelExtract(physData, physModelNames.Original).ToContentFile();
            var editable = new ModelExtract(physData, physModelNames.Editable)
            {
                Type = ModelExtract.ModelExtractType.Map_PhysicsToRenderMesh,
                PhysicsToRenderMaterialNameProvider = GetToolTextureNameForCollisionTags,
            }
            .ToContentFile();
            //vmap.AdditionalFiles.Add(original);
            vmap.AdditionalFiles.Add(editable);
        }

        foreach (var meshName in MeshesToExtract)
        {
            var meshNameCompiled = meshName + GameFileLoader.CompiledFileSuffix;
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
            using var model = FileLoader.LoadFileCompiled(modelName);
            if (model is not null)
            {
                var data = (Model)model.DataBlock;

                var hasMeshes = data.GetEmbeddedMeshesAndLoD().Any() || data.GetReferenceMeshNamesAndLoD().Any();
                var hasPhysics = data.GetEmbeddedPhys() is not null || data.GetReferencedPhysNames().Any();
                var isJustPhysics = hasPhysics && !hasMeshes;

                ModelEntityAssociations.TryGetValue(modelName, out var associatedEntityClass);
                var toolTexture = GetToolTextureForEntity(associatedEntityClass);

                var modelExtract = new ModelExtract(model, FileLoader)
                {
                    Type = isJustPhysics
                        ? ModelExtract.ModelExtractType.Map_PhysicsToRenderMesh
                        : ModelExtract.ModelExtractType.Default,
                    PhysicsToRenderMaterialNameProvider = (_) => toolTexture,
                };

                var vmdl = modelExtract.ToContentFile();
                vmap.AdditionalFiles.Add(vmdl);
            }
        }

        // Export all gathered vsnap files
        foreach (var snapshotName in SnapshotsToExtract)
        {
            using var snapshot = FileLoader.LoadFileCompiled(snapshotName);
            if (snapshot is not null)
            {
                var snapshotExtract = new SnapshotExtract(snapshot);
                var vsnap = snapshotExtract.ToContentFile();
                vsnap.FileName = snapshotName;
                vmap.AdditionalFiles.Add(vsnap);
            }
        }

        foreach (var proceduralPhysMaterial in ProceduralPhysMaterialsToExtract)
        {
            var material = GeneratePhysicsTagMaterial(proceduralPhysMaterial, FileLoader, AssetReferences);
            vmap.AdditionalFiles.Add(material);
        }

        vmap.AdditionalFiles.AddRange(PreExportedFragments);

        // Add these files so they can be filtered out in folder extract
        vmap.AdditionalFiles.AddRange(FolderExtractFilter.Select(r => new ContentFile { FileName = r }));

        return vmap;
    }

    public byte[] ToValveMap()
    {
        using var datamodel = new Datamodel.Datamodel("vmap", 29);

        datamodel.PrefixAttributes.Add("map_asset_references", AssetReferences);
        datamodel.Root = MapDocument = [];

        WorldLayers = [];
        UniqueNodeIds = [];

        if (!string.IsNullOrEmpty(WorldPhysicsName))
        {
            var physModelNames = WorldPhysicsNamesToExtract(WorldPhysicsName);

            MapDocument.World.Children.Add(new CMapEntity() { Name = "Editable World Physics" }
                .WithClassName("prop_static")
                .WithProperty("model", physModelNames.Editable)
            );
        }

        var phys = LoadWorldPhysics();
        var worldPhysMesh = phys.Parts[0].Shape.Meshes.First(m => phys.CollisionAttributes[m.CollisionAttributeIndex].GetStringProperty("m_CollisionGroupString") == "Default");
        PhysVertexMatcher = new PhysicsVertexMatcher(worldPhysMesh);

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
            var entityLumpCompiled = entityLumpName + GameFileLoader.CompiledFileSuffix;
            FolderExtractFilter.Add(entityLumpCompiled);

            using var entityLumpResource = FileLoader.LoadFile(entityLumpCompiled);
            if (entityLumpResource is not null)
            {
                GatherEntitiesFromLump((EntityLump)entityLumpResource.DataBlock);
            }
        }

        //convert phys to hammer meshes
        if (phys != null)
        {
            foreach (var hammermesh in PhysToHammerMeshes(phys))
            {
                MapDocument.World.Children.Add(new CMapMesh() { MeshData = hammermesh });
            }
        }

        using var stream = new MemoryStream();

#if DEBUG
        datamodel.Save(stream, "keyvalues2", 4);
#else
        datamodel.Save(stream, "binary", 9);
#endif

        return stream.ToArray();
    }

    internal IEnumerable<CDmePolygonMesh> RenderMeshToHammerMesh(Model model, Resource resource, Vector3 offset = new Vector3())
    {
        var modelExtract = new ModelExtract(resource, FileLoader);
        modelExtract.GrabMaterialInputSignatures(resource);

        var dmxOptions = new ModelExtract.DatamodelRenderMeshExtractOptions
        {
            MaterialInputSignatures = modelExtract.MaterialInputSignatures,
            SplitDrawCallsIntoSeparateSubmeshes = true,
        };

        foreach (var embedded in model.GetEmbeddedMeshes())
        {
            using var dmxMesh = ModelExtract.ConvertMeshToDatamodelMesh(embedded.Mesh, Path.GetFileNameWithoutExtension(resource.FileName), dmxOptions);

            var mesh = (DmeModel)dmxMesh.Root["model"];
            foreach (var dag in mesh.JointList.Cast<DmeDag>())
            {
                var builder = new HammerMeshBuilder(FileLoader) { PhysicsVertexMatcher = PhysVertexMatcher };
                var meshShape = dag.Shape;
                builder.AddRenderMesh(mesh, meshShape, offset);
                yield return builder.GenerateMesh();
            }
        }
    }

    internal IEnumerable<CDmePolygonMesh> PhysToHammerMeshes(PhysAggregateData phys, Vector3 positionOffset = new Vector3(), string entityClassname = null)
    {
        var materialOverride = string.IsNullOrEmpty(entityClassname)
            ? null
            : GetToolTextureForEntity(entityClassname);

        for (var i = 0; i < phys.Parts.Length; i++)
        {
            var shape = phys.Parts[i].Shape;

            foreach (var hull in shape.Hulls)
            {
                var hammerMeshBuilder = new HammerMeshBuilder(FileLoader);
                hammerMeshBuilder.AddPhysHull(hull, phys, ProceduralPhysMaterialsToExtract, positionOffset, materialOverride);

                yield return hammerMeshBuilder.GenerateMesh();
            }

            foreach (var mesh in shape.Meshes)
            {
                var hammerMeshBuilder = new HammerMeshBuilder(FileLoader);

                var delete = mesh == PhysVertexMatcher.PhysicsMesh ? PhysVertexMatcher.DeletedVertexIndices : [];
                hammerMeshBuilder.AddPhysMesh(mesh, phys, ProceduralPhysMaterialsToExtract, delete, positionOffset, materialOverride);

                yield return hammerMeshBuilder.GenerateMesh();
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

        // Add any non-default world layer to the document
        foreach (var layerNode in layerNodes)
        {
            if (layerNode != MapDocument.World)
            {
                MapDocument.World.Children.Add(layerNode);
            }
        }

        MapNode GetWorldLayerNode(int layerIndex, List<MapNode> layerNodes)
        {
            if (layerIndex > -1)
            {
                return layerNodes[layerIndex];
            }

            return MapDocument.World;
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

        void StaticPropFinalize(MapNode node, int layerIndex, List<MapNode> layerNodes, bool isBakedToWorld)
        {
            var destNode = GetWorldLayerNode(layerIndex, layerNodes);

            // Only use this group in the base world layer
            var bakedGroup = isBakedToWorld && destNode == MapDocument.World
                ? "Baked World Models"
                : null;

            AddChildMaybeGrouped(destNode, node, bakedGroup);
        }

        Datamodel.Color ConvertToColor32(Vector4 tint)
        {
            var color32 = unchecked(stackalloc byte[] { (byte)tint.X, (byte)tint.Y, (byte)tint.Z, (byte)tint.W });
            return Datamodel.Color.FromBytes(color32);
        }

        void SetTintAlpha(BaseEntity entity, Vector4 tint)
        {
            var color32 = ConvertToColor32(tint);
            if (entity is CMapInstance instance)
            {
                instance.TintColor = color32;
                return;
            }

            entity.EntityProperties["rendercolor"] = $"{color32.R} {color32.G} {color32.B}";
            entity.EntityProperties["renderamt"] = color32.A.ToString(CultureInfo.InvariantCulture);
        }

        void SetPropertiesFromFlags(BaseEntity prop, ObjectTypeFlags objectFlags)
        {
            var properties = prop.EntityProperties;
            properties["renderwithdynamic"] = StringBool(objectFlags.HasFlag(ObjectTypeFlags.RenderWithDynamic));
            properties["rendertocubemaps"] = StringBool(objectFlags.HasFlag(ObjectTypeFlags.RenderToCubemaps));
            //properties["disableshadows"] = StringBool(objectFlags.HasFlag(ObjectTypeFlags.NoShadows));
            properties["disableinlowquality"] = StringBool(objectFlags.HasFlag(ObjectTypeFlags.DisabledInLowQuality));
        }

        void SceneObjectToStaticProp(KVObject sceneObject, int layerIndex, List<MapNode> layerNodes)
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

                modelName = Path.ChangeExtension(meshName, ".vmdl");
            }

            AssetReferences.Add(modelName);

            var propStatic = new CMapEntity()
                .WithClassName("prop_static")
                .WithProperty("model", modelName);

            var objectTransform = sceneObject.GetArray("m_vTransform").ToMatrix4x4();
            if (!objectTransform.IsIdentity)
            {
                Matrix4x4.Decompose(objectTransform, out var scales, out var rotation, out var translation);

                propStatic.Origin = translation;
                propStatic.Angles = ModelExtract.ToEulerAngles(rotation);
                propStatic.Scales = scales;
            }

            var fadeStartDistance = sceneObject.GetProperty<double>("m_flFadeStartDistance");
            var fadeEndDistance = sceneObject.GetProperty<double>("m_flFadeEndDistance");
            if (fadeStartDistance > 0)
            {
                propStatic.EntityProperties["fademindist"] = fadeStartDistance.ToString(CultureInfo.InvariantCulture);
                propStatic.EntityProperties["fademaxdist"] = fadeEndDistance.ToString(CultureInfo.InvariantCulture);
            }

            var tintColor = sceneObject.GetSubCollection("m_vTintColor").ToVector4();
            if (tintColor != Vector4.Zero)
            {
                SetTintAlpha(propStatic, tintColor * 255f);
            }

            /* // TODO: check for values being 0
            if (!sceneObject.ContainsKey("m_nLightProbeVolumePrecomputedHandshake") || !sceneObject.ContainsKey("m_nCubeMapPrecomputedHandshake"))
            {
                propStatic.EntityProperties["precomputelightprobes"] = StringBool(false);
            }*/

            var skin = sceneObject.GetProperty<string>("m_skin");
            if (!string.IsNullOrEmpty(skin))
            {
                propStatic.EntityProperties["skin"] = skin;
            }

            SetPropertiesFromFlags(propStatic, objectFlags);

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

        void ProcessAggregate(KVObject agg, int layerIndex, List<MapNode> layerNodes)
        {
            var modelName = agg.GetProperty<string>("m_renderableModel");
            var anyFlags = agg.GetEnumValue<ObjectTypeFlags>("m_anyFlags", normalize: true);
            var allFlags = agg.GetEnumValue<ObjectTypeFlags>("m_allFlags", normalize: true);

            var hasModelFlag = allFlags.HasFlag(ObjectTypeFlags.Model);

            var aggregateMeshes = agg.GetArray("m_aggregateMeshes");

            //ModelsToExtract.Add(modelName);
            var drawCalls = Array.Empty<KVObject>();
            var drawCenters = Array.Empty<Vector3>();

            var transformIndex = 0;
            var fragmentTransforms = agg.ContainsKey("m_fragmentTransforms")
                ? agg.GetArray("m_fragmentTransforms")
                : [];

            var aggregateHasTransforms = fragmentTransforms.Length > 0;

            // maybe not load and export model here
            Model model;
            Resource modelRes;
            using (modelRes = FileLoader.LoadFileCompiled(modelName))
            {
                // TODO: reference meshes
                model = (Model)modelRes.DataBlock;
                var mesh = ((Model)modelRes.DataBlock).GetEmbeddedMeshes().First();
                var sceneObject = mesh.Mesh.Data.GetArray("m_sceneObjects").First();
                drawCalls = sceneObject.GetArray("m_drawCalls");

                if (!aggregateHasTransforms)
                {
                    drawCenters = (sceneObject.ContainsKey("m_drawBounds") ? sceneObject.GetArray("m_drawBounds") : [])
                        .Select(aabb => (aabb.GetSubCollection("m_vMinBounds").ToVector3() + aabb.GetSubCollection("m_vMaxBounds").ToVector3()) / 2f)
                        .ToArray();
                }

                PreExportedFragments.AddRange(ModelExtract.GetContentFiles_DrawCallSplit(modelRes, FileLoader, drawCenters, drawCalls.Length));
            }

            List<CMapMesh> mapMeshes = [];
            if (!hasModelFlag)
            {
                foreach (var hammermesh in RenderMeshToHammerMesh(model, modelRes))
                {
                    var mapMesh = new CMapMesh() { MeshData = hammermesh };
                    mapMeshes.Add(mapMesh);
                }
            }

            BaseEntity NewPropStatic(string modelName) => new CMapEntity()
                .WithClassName("prop_static")
                .WithProperty("model", modelName)
                .WithProperty("baketoworld", StringBool(true))
                .WithProperty("disablemerging", StringBool(true))
                .WithProperty("visoccluder", StringBool(true));

            var UseHammerInstances = false;
            CMapGroup instanceGroup = null;

            foreach (var fragment in aggregateMeshes)
            {
                var i = fragment.GetInt32Property("m_nDrawCallIndex");
                var fragmentFlags = fragment.GetEnumValue<ObjectTypeFlags>("m_objectFlags", normalize: true);

                var tint = Vector3.One * 255f;
                var alpha = 255f;

                var drawCall = drawCalls[i];

                if (fragment.ContainsKey("m_vTintColor"))
                {
                    tint = fragment.GetSubCollection("m_vTintColor").ToVector3();
                }

                var drawCallTint = drawCall.GetSubCollection("m_vTintColor").ToVector3();
                tint *= SrgbLinearToGamma(drawCallTint);
                alpha *= drawCall.GetFloatProperty("m_flAlpha");

                if (!hasModelFlag)
                {
                    var mapMesh = mapMeshes[i];
                    mapMesh.TintColor = ConvertToColor32(new Vector4(tint, alpha));
                    MapDocument.World.Children.Add(mapMesh);
                    continue;
                }

                var fragmentModelName = ModelExtract.GetFragmentModelName(modelName, i);

                var instance = NewPropStatic(fragmentModelName);
                AssetReferences.Add(fragmentModelName);

                if (aggregateHasTransforms)
                {
                    if (instanceGroup is null)
                    {
                        instanceGroup = new CMapGroup
                        {
                            Name = "[Instances] " + Path.GetFileNameWithoutExtension(modelName),
                        };

                        if (UseHammerInstances)
                        {
                            // One shared prop when using hammer instances
                            instanceGroup.Children.Add(instance);
                        }

                        // Add group to world
                        GetWorldLayerNode(layerIndex, layerNodes).Children.Add(instanceGroup);
                    }

                    if (UseHammerInstances)
                    {
                        instance = new CMapInstance() { Target = instanceGroup };
                        GetWorldLayerNode(layerIndex, layerNodes).Children.Add(instance);
                    }
                    else
                    {
                        // Keep adding new props to the group
                        instanceGroup.Children.Add(instance);
                    }

                    var transform = fragmentTransforms[transformIndex++].ToMatrix4x4();
                    Matrix4x4.Decompose(transform, out var scales, out var rotation, out var translation);

                    instance.Origin = translation;
                    var angles = ModelExtract.ToEulerAngles(rotation);
                    instance.Angles = angles;
                    instance.Scales = scales;

                    SetPropertiesFromFlags(instance, fragmentFlags);
                    SetTintAlpha(instance, new Vector4(tint, alpha));

                    continue;
                }

                if (drawCenters.Length > 0)
                {
                    // fragment recentering
                    // apply positive vector in the vmap, and negative vector in the vmdl
                    instance.Origin = drawCenters[i];
                }

                if (instanceGroup is null)
                {
                    Debug.Assert(aggregateHasTransforms == false, "aggregate also has instanced transforms");

                    instanceGroup = new CMapGroup
                    {
                        Name = "[MultiDraw] " + Path.GetFileNameWithoutExtension(modelName),
                    };

                    GetWorldLayerNode(layerIndex, layerNodes).Children.Add(instanceGroup);
                }

                SetPropertiesFromFlags(instance, fragmentFlags);
                SetTintAlpha(instance, new Vector4(tint, alpha));

                instanceGroup.Children.Add(instance);
            }
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
            ProcessAggregate(aggregateSceneObject, layerIndex, layerNodes);
        }

        foreach (var clutterSceneObject in node.ClutterSceneObjects)
        {
            // TODO: Clutter
        }
    }

    private static ContentFile GeneratePhysicsTagMaterial(string surfaceName, IFileLoader fileLoader, List<string> assetReferences)
    {
        var physicsTagMaterial = new Material()
        {
            ShaderName = "generic.vfx",
        };

        physicsTagMaterial.VectorParams["TextureTranslucency"] = new Vector4(0.500000f, 0.500000f, 0.500000f, 0.000000f);
        physicsTagMaterial.IntParams["F_TRANSLUCENT"] = 1;
        physicsTagMaterial.TextureParams["TextureColor"] = "maps/" + HammerMeshBuilder.proceduralPhysMaterialsPath + "vrf.vtex";
        physicsTagMaterial.IntAttributes["physics.nodefaultsimplification"] = 1;

        physicsTagMaterial.StringAttributes["PhysicsSurfaceProperties"] = surfaceName;

        var materialContentFile = new MaterialExtract(physicsTagMaterial, null, fileLoader).ToContentFile();
        materialContentFile.FileName = HammerMeshBuilder.proceduralPhysMaterialsPath + surfaceName + ".vmat";
        assetReferences.Add(materialContentFile.FileName);

        GeneratePhysicsTagTexture(surfaceName, "D:/S2 tools");

        return materialContentFile;
    }

    private static void GeneratePhysicsTagTexture(string surfaceName, string outputPath)
    {
        var text = "S2V AUTOPHYS " + surfaceName.ToUpper(CultureInfo.CurrentCulture);
        int texSize = 64;
        SKColor backgroundColor = SKColors.DarkBlue;

        using (var bitmap = new SKBitmap(texSize, texSize))
        {
            using (SKCanvas canvas = new SKCanvas(bitmap))
            {
                canvas.Clear(backgroundColor);

                using var textPaint = new SKPaint
                {
                    Color = SKColors.White,
                    TextSize = 10,
                    TextAlign = SKTextAlign.Center,
                    IsAntialias = true,
                    Typeface = SKTypeface.FromFamilyName("Arial"),
                };

                SkiaTextBox.Draw(text, 0, 0, texSize, texSize, canvas, textPaint, false);
            }

            using (var stream = System.IO.File.OpenWrite(Path.Combine(outputPath, surfaceName) + ".png"))
            {
                bitmap.Encode(SKEncodedImageFormat.Png, 100).SaveTo(stream);
            }
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
            using var entityLumpResource = FileLoader.LoadFileCompiled(childLumpName);
            if (entityLumpResource is not null)
            {
                GatherEntitiesFromLump((EntityLump)entityLumpResource.DataBlock);
            }
        }

        foreach (var compiledEntity in entityLump.GetEntities())
        {
            FixUpEntityKeyValues(compiledEntity);

            var className = compiledEntity.GetProperty<string>(CommonHashes.ClassName);

            if (className == "worldspawn")
            {
                AddProperties(className, compiledEntity, MapDocument.World);
                MapDocument.World.EntityProperties["description"] = $"Decompiled with {StringToken.VRF_GENERATOR}";
                continue;
            }

            var mapEntity = new CMapEntity();
            var entityLineage = AddProperties(className, compiledEntity, mapEntity);
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

            var modelName = compiledEntity.GetProperty<string>(CommonHashes.Model);
            if (modelName != null && PathIsSubPath(modelName, LumpFolder))
            {
                modelName = modelName.Replace('\\', '/');

                if (!className.StartsWith("prop_", StringComparison.Ordinal))
                {
                    var resource = FileLoader.LoadFile(modelName + "_c");
                    var model = (Model)resource.DataBlock;
                    var offset = EntityTransformHelper.CalculateTransformationMatrix(compiledEntity).Translation;

                    foreach (var hammermesh in RenderMeshToHammerMesh(model, resource, offset))
                    {
                        mapEntity.Children.Add(new CMapMesh() { MeshData = hammermesh });
                    }

                    // only extract physics if there are no render meshes
                    if (!model.GetEmbeddedMeshes().Any())
                    {
                        var phys = model.GetEmbeddedPhys();
                        if (phys != null)
                        {
                            foreach (var hammermesh in PhysToHammerMeshes(phys, offset, className))
                            {
                                mapEntity.Children.Add(new CMapMesh() { MeshData = hammermesh });
                            }
                        }
                    }
                }
                else
                {
                    ModelsToExtract.Add(modelName);
                    Debug.Assert(ModelEntityAssociations.TryAdd(modelName, className), "Model referenced by more than one entity!");
                }


                ReadOnlySpan<char> entityIdFull = Path.GetFileNameWithoutExtension(modelName);
                var nameCutoff = entityIdFull.Length;
                foreach (var entityId in entityLineage.Reverse())
                {
                    ReadOnlySpan<char> entityIdString = '_' + entityId.ToString(CultureInfo.InvariantCulture);
                    if (entityIdFull[..nameCutoff].EndsWith(entityIdString, StringComparison.Ordinal))
                    {
                        nameCutoff -= entityIdString.Length;
                    }
                }

                var entityName = new string(entityIdFull[..nameCutoff]);
                if (entityName != "unnamed")
                {
                    mapEntity.Name = entityName;
                }
            }

            var snapshotFile = compiledEntity.GetProperty<string>(StringToken.Get("snapshot_file"));
            if (snapshotFile != null && PathIsSubPath(snapshotFile, LumpFolder))
            {
                snapshotFile = snapshotFile.Replace('\\', '/');
                SnapshotsToExtract.Add(snapshotFile);

                // snapshot_mesh needs to be set to 0 in order for it to use the vsnap file
                mapEntity.WithProperty("snapshot_mesh", "0");
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

    private static int[] AddProperties(string className, EntityLump.Entity compiledEntity, BaseEntity mapEntity)
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

            if (RemoveOrMutateCompilerGeneratedProperty(className, property))
            {
                continue;
            }

            var key = property.Name;
            var value = PropertyToEditString(property);
            value = RemoveTargetnamePrefix(value);

            mapEntity.EntityProperties.Add(key, value);
        }

        if (compiledEntity.Connections != null)
        {
            foreach (var connection in compiledEntity.Connections)
            {
                var dmeConnection = new DmeConnectionData
                {
                    OutputName = connection.GetProperty<string>("m_outputName"),
                    TargetType = connection.GetInt32Property("m_targetType"),
                    TargetName = RemoveTargetnamePrefix(connection.GetProperty<string>("m_targetName")),
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

    // TODO: cubemaptexture may be set by artist, needs to be handled differently (check CS2 /ui/ maps)
    private static bool RemoveOrMutateCompilerGeneratedProperty(string className, EntityLump.EntityProperty property)
    {
        const string prefix = "vrf_";
        if (className is "env_combined_light_probe_volume" or "env_light_probe_volume" or "env_cubemap_box" or "env_cubemap")
        {
            var key = property.Name;
            if (key is "cubemaptexture" or "lightprobetexture")
            {
                property.Name = prefix + key;
            }
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
        //var type = property.Type;
        var data = property.Data;
        return ToEditString(data);
    }

    private static string ToEditString(object data)
    {
        return data switch
        {
            string str => str,
            bool boolean => StringBool(boolean),
            Vector3 vector => $"{vector.X} {vector.Y} {vector.Z}",
            Vector2 vector => $"{vector.X} {vector.Y}",
            byte[] color => $"{color[0]} {color[1]} {color[2]} {color[3]}",
            null => string.Empty,
            _ => data.ToString()
        };
    }

    private static string RemoveTargetnamePrefix(string value)
    {
        const string Prefix = "[PR#]";

        if (!value.StartsWith(Prefix, StringComparison.Ordinal))
        {
            return value;
        }

        return value[Prefix.Length..];
    }

    #endregion Entities
    private static Vector3 SrgbLinearToGamma(Vector3 vLinearColor)
    {
        var vLinearSegment = vLinearColor * 12.92f;
        const float power = 1.0f / 2.4f;

        var vExpSegment = new Vector3(
            MathF.Pow(vLinearColor.X, power),
            MathF.Pow(vLinearColor.Y, power),
            MathF.Pow(vLinearColor.Z, power)
        );

        vExpSegment *= 1.055f;
        vExpSegment -= new Vector3(0.055f);

        var vGammaColor = new Vector3(
            (vLinearColor.X <= 0.0031308) ? vLinearSegment.X : vExpSegment.X,
            (vLinearColor.Y <= 0.0031308) ? vLinearSegment.Y : vExpSegment.Y,
            (vLinearColor.Z <= 0.0031308) ? vLinearSegment.Z : vExpSegment.Z
        );

        return vGammaColor;
    }

    /// <summary>
	/// A text box that handles multi-line text wrapping and layout.
	/// </summary>
	public static class SkiaTextBox
    {
        /// <summary>
        /// Draw the specified text in the region defined by x, y, width, height wrapping and breaking lines
        /// to fit in that region
        /// </summary>
        /// <returns>The draw.</returns>
        /// <param name="text">Text.</param>
        /// <param name="x">The x coordinate.</param>
        /// <param name="y">The y coordinate.</param>
        /// <param name="width">Width.</param>
        /// <param name="height">Height.</param>
        /// <param name="canvas">Canvas.</param>
        /// <param name="paint">Paint.</param>
        public static void Draw(string text, double x, double y, double width, double height, SKCanvas canvas, SKPaint paint, bool ellipsize)
        {
            if (text == null)
            {
                return;
            }

            double textY = 0, textX = 0;

            switch (paint.TextAlign)
            {
                case SKTextAlign.Center:
                    textX = x + width / 2;
                    break;
                case SKTextAlign.Left:
                    textX = x;
                    break;
                case SKTextAlign.Right:
                    textX = x + width;
                    break;
            }

            var lines = BreakLines(text, paint, width);

            var metrics = paint.FontMetrics;
            var lineHeight = metrics.Bottom - metrics.Top;

            float textHeight = lines.Count * lineHeight - metrics.Leading;

            if (textHeight > height)
            {
                textY = y - metrics.Top;
            }
            else
            {
                textY = y - metrics.Top + (height - textHeight) / 2;
            }
            if (ellipsize && lines.Count > height / lineHeight)
            {
                var ellipsizedLine = $"{lines.FirstOrDefault()}...";
                canvas.DrawText(ellipsizedLine, (float)textX, (float)textY, paint);
            }
            else
            {
                for (int i = 0; i < lines.Count; i++)
                {
                    canvas.DrawText(lines[i], (float)textX, (float)textY, paint);
                    textY += lineHeight;
                    if (textY + metrics.Descent > y + height)
                    {
                        break;
                    }
                }
            }
        }

        static List<string> BreakLines(string text, SKPaint paint, double width)
        {
            List<string> lines = new List<string>();

            string remainingText = text.Trim();

            do
            {
                int idx = LineBreak(remainingText, paint, width);
                if (idx == 0)
                {
                    break;
                }
                var lastLine = remainingText.Substring(0, idx).Trim();
                lines.Add(lastLine);
                remainingText = remainingText.Substring(idx).Trim();
            } while (!string.IsNullOrEmpty(remainingText));
            return lines;
        }

        static int LineBreak(string text, SKPaint paint, double width)
        {
            int idx = 0, last = 0;
            int lengthBreak = (int)paint.BreakText(text, (float)width);

            while (idx < text.Length)
            {
                int next = text.IndexOfAny([' ', '\n'], idx);
                if (next == -1)
                {
                    if (idx == 0)
                    {
                        // Word is too long, we will have to break it
                        return lengthBreak;
                    }
                    else
                    {
                        // Ellipsize if it's the last line
                        if (lengthBreak == text.Length
                        // || text.IndexOfAny (new char [] { ' ', '\n' }, lengthBreak + 1) == -1
                        )
                        {
                            return lengthBreak;
                        }
                        // Split at the last word;
                        return last;
                    }
                }
                if (text[idx] == '\n')
                {
                    return idx;
                }
                if (next > lengthBreak)
                {
                    return idx;
                }
                last = next;
                idx = next + 1;
            }
            return last;
        }
    }

}
