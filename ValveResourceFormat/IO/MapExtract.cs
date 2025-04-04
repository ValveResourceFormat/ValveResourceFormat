using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using ValveResourceFormat.Blocks;
using ValveResourceFormat.IO.ContentFormats.DmxModel;
using ValveResourceFormat.IO.ContentFormats.ValveMap;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.Serialization;
using ValveResourceFormat.Serialization.KeyValues;
using static ValveResourceFormat.ResourceTypes.EntityLump;

namespace ValveResourceFormat.IO;

public sealed class MapExtract
{
    public string LumpFolder { get; private set; }

    private IReadOnlyCollection<string> EntityLumpNames { get; set; }
    private IReadOnlyCollection<string> WorldNodeNames { get; set; }
    private string WorldPhysicsName { get; set; }

    private List<string> AssetReferences { get; } = [];
    private List<string> ModelsToExtract { get; } = [];
    private HashSet<(string Name, string SurfaceProperty)> ProceduralPhysMaterialsToExtract { get; } = [];
    private List<ContentFile> PreExportedFragments { get; } = [];
    private List<ContentFile> EntityModels { get; } = [];
    private Dictionary<string, string> ModelEntityAssociations { get; } = [];
    private List<string> SceneObjectsToExtract { get; } = [];
    private List<string> FolderExtractFilter { get; } = [];
    private List<string> SnapshotsToExtract { get; } = [];

    // Selection sets (for easy access)
    private CMapSelectionSet S2VSelectionSet;
    private CMapSelectionSet HammerMeshesSelectionSet;
    private CMapSelectionSet HammerMesheEntitiesSelectionSet;
    private CMapSelectionSet StaticPropsSelectionSet;
    private CMapSelectionSet PhysicsHullsSelectionSet;
    private CMapSelectionSet HullEntitiesHullsSelectionSet;
    private CMapSelectionSet PhysicsMeshesSelectionSet;
    private CMapSelectionSet MeshEntitiesHullsSelectionSet;
    private CMapSelectionSet OverlaysSelectionSet;
    private CMapSelectionSet EntitiesSelectionSet;

    private List<CMapWorldLayer> WorldLayers { get; set; }
    private Dictionary<int, MapNode> UniqueNodeIds { get; set; }
    private CMapRootElement MapDocument { get; set; }

    private readonly IFileLoader FileLoader;

    public IProgress<string> ProgressReporter { get; set; }
    public PhysicsVertexMatcher PhysVertexMatcher { get; private set; }

    //these all seem to be roughly hammer meshes in cs2
    private static bool SceneObjectShouldConvertToHammerMesh(string modelName)
    {
        return modelName.Contains("_mesh_blocklight", StringComparison.Ordinal)
            || modelName.Contains("_mesh_overlay", StringComparison.Ordinal)
            || modelName.Contains("_c0_", StringComparison.Ordinal);
    }

    /// <summary>
    /// Extract a map from a resource. Accepted types include Map, World. TODO: WorldNode and EntityLump.
    /// </summary>
    public MapExtract(Resource resource, IFileLoader fileLoader)
    {
        FileLoader = fileLoader ?? throw new ArgumentNullException(nameof(fileLoader), "A file loader must be provided to load the map's lumps");
        FileExtract.EnsurePopulatedStringToken(fileLoader);

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

    private static string NormalizePath(string path)
    {
        if (path is null)
        {
            return path;
        }

        return path.Replace('\\', '/').TrimEnd('/');
    }

    private static bool PathIsSubPath(string equalOrSubPath, string path)
        => equalOrSubPath.StartsWith(path, StringComparison.OrdinalIgnoreCase);

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
        return NormalizePath(Path.GetDirectoryName(worldPath));
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

        return NormalizePath(manifest.Resources.First().FirstOrDefault());
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

        foreach (var sceneObjectResourceName in SceneObjectsToExtract)
        {
            var sceneObjectNameCompiled = sceneObjectResourceName + GameFileLoader.CompiledFileSuffix;
            using var sceneObject = FileLoader.LoadFile(sceneObjectNameCompiled);

            if (sceneObject == null)
            {
                continue;
            }

            var sceneObjectExtract = sceneObject.ResourceType switch
            {
                ResourceType.Model => new ModelExtract(sceneObject, FileLoader),
                ResourceType.Mesh => new ModelExtract((Mesh)sceneObject.DataBlock, sceneObjectResourceName),
                _ => throw new InvalidDataException($"Unhandled resource type: {sceneObject.ResourceType} as a scene object"),
            };

            var vmdl = sceneObjectExtract.ToContentFile();
            vmap.AdditionalFiles.Add(vmdl);
            FolderExtractFilter.Add(sceneObjectNameCompiled);
        }

        // Export all gathered vsnap files
        foreach (var snapshotName in SnapshotsToExtract)
        {
            using var snapshot = FileLoader.LoadFileCompiled(snapshotName);
            if (snapshot != null)
            {
                var snapshotExtract = new SnapshotExtract(snapshot);
                var vsnap = snapshotExtract.ToContentFile();
                vsnap.FileName = snapshotName;
                vmap.AdditionalFiles.Add(vsnap);
            }
        }

        foreach (var generatedMaterial in ProceduralPhysMaterialsToExtract)
        {
            var vmat = GeneratePhysicsTagMaterial(generatedMaterial.Name, generatedMaterial.SurfaceProperty);
            vmap.AdditionalFiles.Add(vmat);
        }

        vmap.AdditionalFiles.AddRange(PreExportedFragments);
        vmap.AdditionalFiles.AddRange(EntityModels);

        // Add these files so they can be filtered out in folder extract
        vmap.AdditionalFiles.AddRange(FolderExtractFilter.Select(r => new ContentFile { FileName = r }));

        return vmap;
    }

    public byte[] ToValveMap()
    {
        using var datamodel = new Datamodel.Datamodel("vmap", 29);

        datamodel.PrefixAttributes.Add("map_asset_references", AssetReferences);
        datamodel.Root = MapDocument = [];

        CreateSelectionSets(MapDocument.RootSelectionSet);

        WorldLayers = [];
        UniqueNodeIds = [];

        var phys = LoadWorldPhysics();
        if (phys != null)
        {
            var worldPhysMesh = phys.Parts[0].Shape.Meshes.FirstOrDefault(m => phys.CollisionAttributes[m.CollisionAttributeIndex].GetStringProperty("m_CollisionGroupString") == "Default");
            if (worldPhysMesh != null)
            {
                PhysVertexMatcher = new PhysicsVertexMatcher(worldPhysMesh);
            }

            // TODO: physics spheres and capsules are ignored
        }

        foreach (var worldNodeName in WorldNodeNames)
        {
            var worldNodeCompiled = worldNodeName + ".vwnod_c";
            FolderExtractFilter.Add(worldNodeCompiled);

            using var worldNode = FileLoader.LoadFile(worldNodeCompiled);
            if (worldNode != null)
            {
                HandleWorldNode((WorldNode)worldNode.DataBlock);
            }
        }

        foreach (var entityLumpName in EntityLumpNames)
        {
            var entityLumpCompiled = entityLumpName + GameFileLoader.CompiledFileSuffix;
            FolderExtractFilter.Add(entityLumpCompiled);

            using var entityLumpResource = FileLoader.LoadFile(entityLumpCompiled);
            if (entityLumpResource != null)
            {
                GatherEntitiesFromLump((EntityLump)entityLumpResource.DataBlock);
            }
        }

        //convert phys to hammer meshes
        if (phys != null)
        {
            foreach (var hammermesh in PhysToHammerMeshes(phys))
            {
                MapDocument.World.Children.Add(hammermesh);
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

    private void CreateSelectionSets(CMapSelectionSet root)
    {
        S2VSelectionSet = root.Children.AddReturn(new CMapSelectionSet("Source2 Viewer"));

        HammerMeshesSelectionSet = S2VSelectionSet.Children.AddReturn(new CMapSelectionSet("Hammer Meshes"));
        {
            HammerMesheEntitiesSelectionSet = HammerMeshesSelectionSet.Children.AddReturn(new CMapSelectionSet("Reconstructed Hammer Mesh Entities"));
        }

        StaticPropsSelectionSet = S2VSelectionSet.Children.AddReturn(new CMapSelectionSet("Static Props"));

        PhysicsHullsSelectionSet = S2VSelectionSet.Children.AddReturn(new CMapSelectionSet("Physics Hulls"));
        {
            HullEntitiesHullsSelectionSet = PhysicsHullsSelectionSet.Children.AddReturn(new CMapSelectionSet("Reconstructed Physics Hull Entities"));
        }

        PhysicsMeshesSelectionSet = S2VSelectionSet.Children.AddReturn(new CMapSelectionSet("Physics Meshes"));
        {
            MeshEntitiesHullsSelectionSet = PhysicsMeshesSelectionSet.Children.AddReturn(new CMapSelectionSet("Reconstructed Physics Mesh Entities"));
        }

        OverlaysSelectionSet = S2VSelectionSet.Children.AddReturn(new CMapSelectionSet("Overlays"));
        EntitiesSelectionSet = S2VSelectionSet.Children.AddReturn(new CMapSelectionSet("Entities"));
    }

    internal IEnumerable<CMapMesh> RenderMeshToHammerMesh(Model model, Resource resource, Vector3 offset = new Vector3(), string entityClassname = null)
    {
        var modelExtract = new ModelExtract(resource, FileLoader);
        modelExtract.GrabMaterialInputSignatures(resource);

        var dmxOptions = new ModelExtract.DatamodelRenderMeshExtractOptions
        {
            MaterialInputSignatures = modelExtract.MaterialInputSignatures,
            SplitDrawCallsIntoSeparateSubmeshes = true,
        };

        // TODO: reference meshes
        var hammerMeshEntitySelectionSet = new CMapSelectionSet();
        var drawSelectionSet = new CMapSelectionSet();
        var componentMeshCount = 0;

        foreach (var embedded in model.GetEmbeddedMeshes())
        {
            using var dmxMesh = ModelExtract.ConvertMeshToDatamodelMesh(embedded.Mesh, Path.GetFileNameWithoutExtension(resource.FileName), dmxOptions);

            var mesh = (DmeModel)dmxMesh.Root["model"];

            foreach (var dag in mesh.JointList.Cast<DmeDag>())
            {
                var builder = new HammerMeshBuilder(FileLoader) { PhysicsVertexMatcher = PhysVertexMatcher, ProgressReporter = ProgressReporter };
                var meshShape = dag.Shape;
                builder.AddRenderMesh(meshShape, offset);
                var hammerMesh = new CMapMesh() { MeshData = builder.GenerateMesh() };

                if (!string.IsNullOrEmpty(entityClassname))
                {
                    hammerMeshEntitySelectionSet.SelectionSetData.SelectedObjects.Add(hammerMesh);
                }
                else
                {
                    drawSelectionSet.SelectionSetData.SelectedObjects.Add(hammerMesh);
                }

                var modelmesh = ((Model)resource.DataBlock).GetEmbeddedMeshes().First();
                var sceneObject = modelmesh.Mesh.Data.GetArray("m_sceneObjects").First();
                var drawCalls = sceneObject.GetArray("m_drawCalls");

                var tint = Vector3.One * 255f;
                var alpha = 255f;

                //this is fine because i think the scene objects we exports are never more than one draw
                var fragment = drawCalls[0];

                if (fragment.ContainsKey("m_vTintColor"))
                {
                    tint *= SrgbLinearToGamma(fragment.GetSubCollection("m_vTintColor").ToVector3());
                }

                alpha *= fragment.GetFloatProperty("m_flAlpha");

                hammerMesh.TintColor = ConvertToColor32(new Vector4(tint, alpha));

                componentMeshCount++;
                yield return hammerMesh;
            }

        }

        hammerMeshEntitySelectionSet.SelectionSetName = "hammer mesh entity " + entityClassname + " (reconstructed from " + componentMeshCount + (componentMeshCount > 1 ? " meshes )" : " mesh )");
        drawSelectionSet.SelectionSetName = "hammer mesh (" + componentMeshCount + (componentMeshCount > 1 ? " scene objects) " : " scene object) ") + Path.GetFileNameWithoutExtension(resource.FileName);

        if (!string.IsNullOrEmpty(entityClassname))
        {
            HammerMesheEntitiesSelectionSet.Children.Add(hammerMeshEntitySelectionSet);
        }
        else if (resource.FileName.Contains("_mesh_overlay", StringComparison.Ordinal))
        {
            OverlaysSelectionSet.Children.Add(drawSelectionSet);
        }
        else if (SceneObjectShouldConvertToHammerMesh(resource.FileName))
        {
            HammerMeshesSelectionSet.Children.Add(drawSelectionSet);
        }
    }

    internal IEnumerable<CMapMesh> PhysToHammerMeshes(PhysAggregateData phys, Vector3 positionOffset = new Vector3(), string entityClassname = null)
    {
        var materialOverride = string.IsNullOrEmpty(entityClassname)
            ? null
            : GetToolTextureForEntity(entityClassname);

        for (var i = 0; i < phys.Parts.Length; i++)
        {
            var shape = phys.Parts[i].Shape;

            var hullsSelectionSet = new CMapSelectionSet
            {
                SelectionSetName = "physics shape (" + shape.Hulls.Length + " hulls)"
            };

            var hullsEntitySelectionSet = new CMapSelectionSet
            {
                SelectionSetName = "physics hull entity " + entityClassname + " (reconstructed from " + shape.Hulls.Length + (shape.Hulls.Length > 1 ? " hulls)" : " hull)")
            };

            var meshesSelectionSet = new CMapSelectionSet
            {
                SelectionSetName = "physics shape (" + shape.Meshes.Length + " meshes)"
            };

            var meshesEntitySelectionSet = new CMapSelectionSet
            {
                SelectionSetName = "physics mesh entity " + entityClassname + " (reconstructed from " + shape.Meshes.Length + (shape.Meshes.Length > 1 ? " meshes)" : " mesh)")
            };

            foreach (var hull in shape.Hulls)
            {
                var hammerMeshBuilder = new HammerMeshBuilder(FileLoader);
                hammerMeshBuilder.AddPhysHull(hull, phys, GetAndExportAutoPhysicsMaterialName, positionOffset, materialOverride);
                var hammerMesh = new CMapMesh() { MeshData = hammerMeshBuilder.GenerateMesh() };

                if (string.IsNullOrEmpty(entityClassname))
                {
                    hullsSelectionSet.SelectionSetData.SelectedObjects.Add(hammerMesh);
                }
                else
                {
                    hullsEntitySelectionSet.SelectionSetData.SelectedObjects.Add(hammerMesh);
                }

                yield return hammerMesh;
            }

            foreach (var mesh in shape.Meshes)
            {
                var hammerMeshBuilder = new HammerMeshBuilder(FileLoader);

                HashSet<int> deletedList = [];
                if (PhysVertexMatcher != null)
                {
                    deletedList = mesh == PhysVertexMatcher.PhysicsMesh ? PhysVertexMatcher.DeletedVertexIndices : [];
                }
                hammerMeshBuilder.AddPhysMesh(mesh, phys, GetAndExportAutoPhysicsMaterialName, deletedList, positionOffset, materialOverride);
                var hammerMesh = new CMapMesh() { MeshData = hammerMeshBuilder.GenerateMesh() };

                if (string.IsNullOrEmpty(entityClassname))
                {
                    meshesSelectionSet.SelectionSetData.SelectedObjects.Add(hammerMesh);
                }
                else
                {
                    meshesEntitySelectionSet.SelectionSetData.SelectedObjects.Add(hammerMesh);
                }

                yield return hammerMesh;
            }

            if (shape.Hulls.Length != 0)
            {
                if (string.IsNullOrEmpty(entityClassname))
                {
                    PhysicsHullsSelectionSet.Children.Add(hullsSelectionSet);
                }
                else
                {
                    HullEntitiesHullsSelectionSet.Children.Add(hullsEntitySelectionSet);
                }
            }

            if (shape.Meshes.Length != 0)
            {
                if (string.IsNullOrEmpty(entityClassname))
                {
                    PhysicsMeshesSelectionSet.Children.Add(meshesSelectionSet);
                }
                else
                {
                    MeshEntitiesHullsSelectionSet.Children.Add(meshesEntitySelectionSet);
                }
            }
        }
    }

    Datamodel.Color ConvertToColor32(Vector4 tint)
    {
        var color32 = unchecked(stackalloc byte[] { (byte)tint.X, (byte)tint.Y, (byte)tint.Z, (byte)tint.W });
        return Datamodel.Color.FromBytes(color32);
    }

    private void HandleWorldNode(WorldNode node)
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
            MapDocument.World.Children.Add(layer);
        }

        MapNode GetWorldLayerNode(int layerIndex, List<MapNode> layerNodes)
        {
            if (layerIndex > -1)
            {
                return layerNodes[layerIndex];
            }

            return MapDocument.World;
        }

        void AddChildMaybeGrouped(MapNode node, MapNode child, string selectionSetName)
        {
            node.Children.Add(child);

            if (!string.IsNullOrEmpty(selectionSetName))
            {
                var selectionSet = (CMapSelectionSet)S2VSelectionSet.Children
                    .FirstOrDefault(set => ((CMapSelectionSet)set).SelectionSetName == selectionSetName);

                if (selectionSet is null)
                {
                    selectionSet = new CMapSelectionSet { SelectionSetName = selectionSetName };
                    S2VSelectionSet.Children.Add(selectionSet);
                }

                selectionSet.SelectionSetData.SelectedObjects.Add(child);
            }
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
            properties["disableinlowquality"] = StringBool(objectFlags.HasFlag(ObjectTypeFlags.DisabledInLowQuality));
        }

        void ProcessSceneObject(KVObject sceneObject, int layerIndex, List<MapNode> layerNodes)
        {
            var modelName = sceneObject.GetProperty<string>("m_renderableModel");
            var meshName = sceneObject.GetProperty<string>("m_renderable");

            if (string.IsNullOrEmpty(modelName))
            {
                if (string.IsNullOrEmpty(meshName))
                {
                    return;
                }

                SceneObjectsToExtract.Add(meshName);

                return;
            }

            var objectFlags = sceneObject.GetEnumValue<ObjectTypeFlags>("m_nObjectTypeFlags", normalize: true);

            FolderExtractFilter.Add(modelName ?? meshName);

            if (SceneObjectShouldConvertToHammerMesh(modelName))
            {
                var meshNameCompiled = modelName + GameFileLoader.CompiledFileSuffix;
                using var mesh = FileLoader.LoadFile(meshNameCompiled);
                var model = (Model)mesh.DataBlock;
                foreach (var hammermesh in RenderMeshToHammerMesh(model, mesh))
                {
                    MapDocument.World.Children.Add(hammermesh);
                }
                return;
            }
            else
            {
                SceneObjectsToExtract.Add(modelName);
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
            var convertToHalfEdge = !hasModelFlag;
            List<CMapMesh> halfEdgeMeshes = [];

            var aggregateMeshes = agg.GetArray("m_aggregateMeshes");

            var drawCalls = Array.Empty<KVObject>();
            var drawCenters = Array.Empty<Vector3>();

            var transformIndex = 0;
            var fragmentTransforms = agg.ContainsKey("m_fragmentTransforms")
                ? agg.GetArray("m_fragmentTransforms")
                : [];

            var aggregateHasTransforms = fragmentTransforms.Length > 0;

            FolderExtractFilter.Add(modelName);
            using var modelRes = FileLoader.LoadFileCompiled(modelName);
            var model = (Model)modelRes.DataBlock;

            // TODO: reference meshes
            var mesh = ((Model)modelRes.DataBlock).GetEmbeddedMeshes().First();
            var sceneObject = mesh.Mesh.Data.GetArray("m_sceneObjects").First();
            drawCalls = sceneObject.GetArray("m_drawCalls");

            if (convertToHalfEdge)
            {
                foreach (var hammermesh in RenderMeshToHammerMesh(model, modelRes))
                {
                    halfEdgeMeshes.Add(hammermesh);
                }
            }
            else
            {
                if (!aggregateHasTransforms)
                {
                    drawCenters = (sceneObject.ContainsKey("m_drawBounds") ? sceneObject.GetArray("m_drawBounds") : [])
                        .Select(aabb => (aabb.GetSubCollection("m_vMinBounds").ToVector3() + aabb.GetSubCollection("m_vMaxBounds").ToVector3()) / 2f)
                        .ToArray();
                }

                var modelFiles = ModelExtract.GetContentFiles_DrawCallSplit(modelRes, FileLoader, drawCenters, drawCalls.Length);
                PreExportedFragments.AddRange(modelFiles);
            }

            BaseEntity NewPropStatic(string modelName) => new CMapEntity()
                .WithClassName("prop_static")
                .WithProperty("model", modelName)
                .WithProperty("baketoworld", StringBool(true))
                .WithProperty("disablemerging", StringBool(true))
                .WithProperty("visoccluder", StringBool(true));

            var drawSelectionSet = new CMapSelectionSet();
            if (convertToHalfEdge)
            {
                drawSelectionSet.SelectionSetName = "hammer mesh " + (aggregateHasTransforms ? "(instanced) " : "(" + drawCalls.Length + " split draw meshes) ") + Path.GetFileNameWithoutExtension(modelName);
                HammerMeshesSelectionSet.Children.Add(drawSelectionSet);
            }
            else
            {
                drawSelectionSet.SelectionSetName = "prop_static render mesh " + (aggregateHasTransforms ? "(instanced) " : "(" + drawCalls.Length + " split draw meshes) ") + Path.GetFileNameWithoutExtension(modelName);
                StaticPropsSelectionSet.Children.Add(drawSelectionSet);
            }

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

                if (convertToHalfEdge)
                {
                    if (aggregateHasTransforms)
                    {
                        throw new InvalidOperationException("Unhandled aggregate with instanced transforms exported as hammer mesh!");
                    }

                    var mapMesh = halfEdgeMeshes[i];
                    mapMesh.Name = $"draw_{i} ({modelName})";
                    mapMesh.TintColor = ConvertToColor32(new Vector4(tint, alpha));

                    MapDocument.World.Children.Add(mapMesh);
                    drawSelectionSet.SelectionSetData.SelectedObjects.Add(mapMesh);
                    continue;
                }

                var fragmentModelName = ModelExtract.GetFragmentModelName(modelName, i);
                AssetReferences.Add(fragmentModelName);

                var instance = NewPropStatic(fragmentModelName);

                if (aggregateHasTransforms)
                {
                    var transform = fragmentTransforms[transformIndex++].ToMatrix4x4();
                    Matrix4x4.Decompose(transform, out var scales, out var rotation, out var translation);

                    instance.Origin = translation;
                    var angles = ModelExtract.ToEulerAngles(rotation);
                    instance.Angles = angles;
                    instance.Scales = scales;

                    SetPropertiesFromFlags(instance, fragmentFlags);
                    SetTintAlpha(instance, new Vector4(tint, alpha));

                    // Keep adding the same prop
                    GetWorldLayerNode(layerIndex, layerNodes).Children.Add(instance);
                    drawSelectionSet.SelectionSetData.SelectedObjects.Add(instance);
                    continue;
                }

                if (drawCenters.Length > 0)
                {
                    // fragment recentering based on bounding box
                    // apply positive vector in the vmap, and negative vector in the vmdl
                    instance.Origin = drawCenters[i];
                }

                SetPropertiesFromFlags(instance, fragmentFlags);
                SetTintAlpha(instance, new Vector4(tint, alpha));

                GetWorldLayerNode(layerIndex, layerNodes).Children.Add(instance);
                drawSelectionSet.SelectionSetData.SelectedObjects.Add(instance);
            }
        }

        for (var i = 0; i < node.SceneObjects.Count; i++)
        {
            var sceneObject = node.SceneObjects[i];
            var layerIndex = (int)(node.SceneObjectLayerIndices?[i] ?? -1);
            ProcessSceneObject(sceneObject, layerIndex, layerNodes);
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


    internal static string GetAutoPhysicsMaterialName(string rootFolder, string surfaceProperty)
        => NormalizePath(Path.Combine(rootFolder, "_vrf", "physics_surfaces", surfaceProperty + ".vmat"));

    private string GetAndExportAutoPhysicsMaterialName(string surfaceProperty)
    {
        var materialName = GetAutoPhysicsMaterialName(LumpFolder, surfaceProperty);
        ProceduralPhysMaterialsToExtract.Add((materialName, surfaceProperty));
        return materialName;
    }

    private static ContentFile GeneratePhysicsTagMaterial(string materialName, string surfaceProperty)
    {
        var textureName = Path.ChangeExtension(materialName, ".png");

        var root = new ValveKeyValue.KVObject("Layer0",
        [
            new("shader", "generic.vfx"),
            new("F_TRANSLUCENT", 1),
            new("TextureTranslucency", $"[{0.700000f:N6} {0.700000f:N6} {0.700000f:N6} {0.000000f:N6}]"),
            new("TextureColor", textureName),
            new("Attributes",
            [
                new("mapbuilder.nodraw", 1),
                new("tools.toolsmaterial", 1),
                new("physics.nodefaultsimplification", 1),
            ]),
            new("SystemAttributes",
            [
                new("PhysicsSurfaceProperties", surfaceProperty),
            ]),
        ]);

        using var ms = new MemoryStream();
        ValveKeyValue.KVSerializer.Create(ValveKeyValue.KVSerializationFormat.KeyValues1Text).Serialize(ms, root);

        var vmat = new ContentFile()
        {
            Data = ms.ToArray(),
            FileName = materialName,
        };

        vmat.SubFiles.Add(new SubFile()
        {
            FileName = Path.GetFileName(textureName),
            Extract = () =>
            {
                using var bitmap = MapAutoPhysTextureGenerator.GenerateTexture(surfaceProperty);
                return TextureExtract.ToPngImage(bitmap);
            }
        });

        return vmat;
    }

    #region Entities
    private void GatherEntitiesFromLump(EntityLump entityLump)
    {
        var lumpName = entityLump.Data.GetStringProperty("m_name");

        foreach (var childLumpName in entityLump.GetChildEntityNames())
        {
            using var entityLumpResource = FileLoader.LoadFileCompiled(childLumpName);
            if (entityLumpResource != null)
            {
                GatherEntitiesFromLump((EntityLump)entityLumpResource.DataBlock);
            }
        }

        Dictionary<int, CMapSelectionSet> lineageSelectionSets = [];

        foreach (var compiledEntity in entityLump.GetEntities())
        {
            var className = compiledEntity.GetProperty<string>("classname");

            if (className == "worldspawn")
            {
                AddProperties(className, compiledEntity, MapDocument.World);
                MapDocument.World.EntityProperties["description"] = $"Decompiled with {StringToken.VRF_GENERATOR}";
                var mapType = compiledEntity.GetProperty<string>("mapusagetype");
                if (mapType != null)
                {
                    MapDocument.World.MapUsageType = mapType;
                }
                continue;
            }

            var mapEntity = new CMapEntity();
            var entityLineage = AddProperties(className, compiledEntity, mapEntity);
            if (entityLineage.Length > 1)
            {
                for (var i = 0; i < entityLineage.Length; i++)
                {
                    var lineage = entityLineage[i];

                    CMapSelectionSet selectionSet;

                    if (lineageSelectionSets.TryGetValue(lineage, out var value))
                    {
                        selectionSet = value;
                    }
                    else
                    {
                        selectionSet = new CMapSelectionSet
                        {
                            Name = lineage.ToString(CultureInfo.InvariantCulture),
                            SelectionSetName = lineage.ToString(CultureInfo.InvariantCulture)
                        };
                        lineageSelectionSets.Add(lineage, selectionSet);

                        if (i == 0)
                        {
                            EntitiesSelectionSet.Children.Add(selectionSet);
                        }
                        else
                        {
                            var parentSelectionSet = lineageSelectionSets[entityLineage[i - 1]];
                            parentSelectionSet.Children.Add(selectionSet);
                        }
                    }

                    if (i == entityLineage.Length - 1)
                    {
                        selectionSet.SelectionSetData.SelectedObjects.Add(mapEntity);
                    }
                }
            }

            var modelName = NormalizePath(compiledEntity.GetProperty<string>("model"));
            if (modelName != null && PathIsSubPath(modelName, LumpFolder))
            {
                var firstReference = ModelEntityAssociations.TryAdd(modelName, className);
                Debug.Assert(firstReference, "Model living in lump folder referenced by more than one entity!");

                ExtractEntityModel(mapEntity, compiledEntity, modelName);

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

            var snapshotFile = NormalizePath(compiledEntity.GetProperty<string>("snapshot_file"));
            if (snapshotFile != null && PathIsSubPath(snapshotFile, LumpFolder))
            {
                SnapshotsToExtract.Add(snapshotFile);

                // snapshot_mesh needs to be set to 0 in order for it to use the vsnap file
                mapEntity.WithProperty("snapshot_mesh", "0");
            }

            MapDocument.World.Children.Add(mapEntity);
        }
    }

    private void ExtractEntityModel(CMapEntity mapEntity, Entity compiledEntity, string modelName)
    {
        using var model = FileLoader.LoadFileCompiled(modelName);
        if (model is null)
        {
            return;
        }

        var EntitiesToHammerMesh = true;
        ModelEntityAssociations.TryGetValue(modelName, out var associatedEntityClass);

        var data = (Model)model.DataBlock;

        var hasMeshes = data.GetEmbeddedMeshesAndLoD().Any() || data.GetReferenceMeshNamesAndLoD().Any();
        var hasPhysics = data.GetEmbeddedPhys() != null || data.GetReferencedPhysNames().Any();
        var isJustPhysics = hasPhysics && !hasMeshes;

        if (EntitiesToHammerMesh)
        {
            var offset = EntityTransformHelper.CalculateTransformationMatrix(compiledEntity).Translation;

            if (isJustPhysics)
            {
                var phys = data.GetEmbeddedPhys();
                if (phys != null)
                {
                    foreach (var hammermesh in PhysToHammerMeshes(phys, offset, associatedEntityClass))
                    {
                        mapEntity.Children.Add(hammermesh);
                    }
                }
            }
            else
            {
                foreach (var hammermesh in RenderMeshToHammerMesh(data, model, offset, associatedEntityClass))
                {
                    mapEntity.Children.Add(hammermesh);
                }
            }

            return;
        }

        var toolTexture = GetToolTextureForEntity(associatedEntityClass);
        var modelExtract = new ModelExtract(model, FileLoader)
        {
            Type = isJustPhysics
                ? ModelExtract.ModelExtractType.Map_PhysicsToRenderMesh
                : ModelExtract.ModelExtractType.Default,
            PhysicsToRenderMaterialNameProvider = (_) => toolTexture,
        };

        var vmdl = modelExtract.ToContentFile();
        EntityModels.Add(vmdl);
    }

    private static int[] AddProperties(string className, Entity compiledEntity, BaseEntity mapEntity)
    {
        var entityLineage = Array.Empty<int>();
        foreach (var (key, value) in compiledEntity.Properties)
        {
            var propertyKey = key.ToLowerInvariant();

            if (TryHandleSpecialProperty(propertyKey, compiledEntity, mapEntity, ref entityLineage))
            {
                continue;
            }

            if (RemoveOrMutateCompilerGeneratedProperty(className, ref propertyKey))
            {
                continue;
            }

            var editString = ToEditString(value);
            editString = RemoveTargetnamePrefix(editString);

            mapEntity.EntityProperties.Add(propertyKey, editString);
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

    private static bool TryHandleSpecialProperty(string key, Entity compiledEntity, BaseEntity mapEntity, ref int[] lineage)
    {
        if (key == "origin")
        {
            mapEntity.Origin = compiledEntity.GetVector3Property(key);
            return true;
        }
        else if (key == "angles")
        {
            mapEntity.Angles = compiledEntity.GetVector3Property(key);
            return true;
        }
        else if (key == "scales")
        {
            mapEntity.Scales = compiledEntity.GetVector3Property(key);
            return true;
        }
        else if (key == "hammeruniqueid")
        {
            try
            {
                var hammerUniqueIdString = ToEditString(compiledEntity.GetProperty(key).Value);
                lineage = Array.ConvertAll(hammerUniqueIdString.Split(':'), int.Parse);
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

    // TODO: cubemaptexture may be set by artist, needs to be handled differently (reference: CS2 /ui/ maps)
    private static bool RemoveOrMutateCompilerGeneratedProperty(string className, ref string propertyKey)
    {
        const string prefix = "vrf_stripped_";
        if (className is "env_combined_light_probe_volume" or "env_light_probe_volume" or "env_cubemap_box" or "env_cubemap")
        {
            if (propertyKey is "cubemaptexture" or "lightprobetexture")
            {
                propertyKey = prefix + propertyKey;
            }
        }

        return false;
    }

    static string StringBool(bool value)
        => value ? "1" : "0";

    private static string ToEditString(object data)
    {
        return data switch
        {
            string str => str,
            bool boolean => StringBool(boolean),
            Vector3 vector => $"{vector.X} {vector.Y} {vector.Z}",
            Vector2 vector => $"{vector.X} {vector.Y}",
            KVObject { IsArray: true } kvArray => string.Join(' ', kvArray.Select(p => p.Value.ToString())),
            null => string.Empty,
            _ when data.GetType().IsPrimitive => data.ToString(),
            _ => throw new NotImplementedException()
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
            (vLinearColor.X <= 0.0031308f) ? vLinearSegment.X : vExpSegment.X,
            (vLinearColor.Y <= 0.0031308f) ? vLinearSegment.Y : vExpSegment.Y,
            (vLinearColor.Z <= 0.0031308f) ? vLinearSegment.Z : vExpSegment.Z
        );

        return vGammaColor;
    }
}

public static class ElementArrayExtensions
{
    public static T AddReturn<T>(this Datamodel.ElementArray array, T element) where T : Datamodel.Element
    {
        array.Add(element);
        return element;
    }
}
