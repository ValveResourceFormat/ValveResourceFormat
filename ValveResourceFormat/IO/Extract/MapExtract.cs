using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using ValveKeyValue;
using ValveResourceFormat.Blocks;
using ValveResourceFormat.IO.ContentFormats.DmxModel;
using ValveResourceFormat.IO.ContentFormats.HalfEdgeMesh;
using ValveResourceFormat.IO.ContentFormats.ValveMap;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.Serialization.KeyValues;
using static ValveResourceFormat.ResourceTypes.EntityLump;

namespace ValveResourceFormat.IO;

/// <summary>
/// Extracts map data from Source 2 resources into editable formats.
/// </summary>
public sealed class MapExtract
{
    /// <summary>Gets the folder containing map lumps.</summary>
    public string LumpFolder { get; private set; } = string.Empty;

    private IReadOnlyCollection<string> EntityLumpNames { get; set; } = [];
    private IReadOnlyCollection<string> WorldNodeNames { get; set; } = [];
    private string? WorldPhysicsName { get; set; } = string.Empty;

    private List<string> AssetReferences { get; } = [];
    private List<string> ModelsToExtract { get; } = [];
    private HashSet<(string Name, string SurfaceProperty)> ProceduralPhysMaterialsToExtract { get; } = [];
    private List<ContentFile> PreExportedFragments { get; } = [];
    private List<ContentFile> EntityModels { get; } = [];
    private Dictionary<string, string> ModelEntityAssociations { get; } = [];
    private List<string> SceneObjectsToExtract { get; } = [];
    private List<string> FolderExtractFilter { get; } = [];
    private List<string> SnapshotsToExtract { get; } = [];

    // render mesh vertices closer than this are the same Hammer vertex
    private const float HammerMeshWeldDistance = 1f / 64f;

    private readonly Dictionary<string, Vector2?> MaterialTextureSizes = [];

    /// <summary>
    /// what to group overlay geometry by when reconstructing, tthe projected geometry of the overlays that share a material, render order,
    /// tint and flags, which is everything a rebuilt overlay carries besides its shape.
    /// </summary>
    private readonly record struct OverlayGroup(string Material, int RenderOrder, Vector4 Tint, ObjectTypeFlags Flags);

    private const ObjectTypeFlags OverlayFlags = ObjectTypeFlags.DisabledInLowQuality | ObjectTypeFlags.RenderToCubemaps | ObjectTypeFlags.RenderWithDynamic | ObjectTypeFlags.NoShadows;

    // the projected geometry of every overlay of the map, one piece per compiled overlay mesh, welded together and
    // split into the overlays at the end
    private readonly Dictionary<OverlayGroup, List<(Vector3[] Positions, Vector2[] TexCoords, int[] Triangles)>> WorldOverlayGeometry = [];

    // the overlays rebuilt linked to the geometry they were projected onto, used to find the meshes they were projected on
    private readonly List<(CMapStaticOverlay Overlay, PolygonMesh Geometry)> OverlayReceivers = [];

    // aggregate models whose fragments are instanced with transforms of their own; the fragments of every other
    // aggregate keep the aggregate's world space geometry, recentred on their prop
    private readonly HashSet<string> InstancedAggregateModels = [];

    // units the generated physics surface materials span, declared in their vmat so Hammer projects them the same:
    // their 128 texel texture at the density of the tool textures (64 texels over 8 units)
    private const int AutoPhysicsMaterialWorldMapping = 16;

    /// <summary>
    /// The texture size Hammer projects a material with, for faces that have no texture coordinates, in texels at
    /// <see cref="PolygonMesh.DefaultTextureScale"/>: the units its WorldMappingWidth / WorldMappingHeight attributes
    /// say the texture spans, else the size of its representative texture. Null when the material declares neither,
    /// projecting it at the builder's default size.
    /// </summary>
    private Vector2? GetMaterialTextureSize(string materialName)
    {
        if (MaterialTextureSizes.TryGetValue(materialName, out var cached))
        {
            return cached;
        }

        var size = LoadMaterialTextureSize(materialName);
        MaterialTextureSizes[materialName] = size;
        return size;
    }

    private Vector2? LoadMaterialTextureSize(string materialName)
    {
        if (ProceduralPhysMaterialsToExtract.Any(m => m.Name == materialName))
        {
            return new Vector2(AutoPhysicsMaterialWorldMapping) / PolygonMesh.DefaultTextureScale;
        }

        using var materialResource = FileLoader.LoadFileCompiled(materialName);
        if (materialResource?.DataBlock is not Material material)
        {
            return null;
        }

        var intAttributes = material.Data.GetArray("m_intAttributes") ?? [];

        int GetIntAttribute(string name)
        {
            var attribute = intAttributes.FirstOrDefault(a => a.GetStringProperty("m_name") == name);
            return attribute?.GetInt32Property("m_nValue") ?? 0;
        }

        var worldMappingWidth = GetIntAttribute("WorldMappingWidth");
        var worldMappingHeight = GetIntAttribute("WorldMappingHeight");

        if (worldMappingWidth > 0 && worldMappingHeight > 0)
        {
            return new Vector2(worldMappingWidth, worldMappingHeight) / PolygonMesh.DefaultTextureScale;
        }

        var representativeWidth = GetIntAttribute("RepresentativeTextureWidth");
        var representativeHeight = GetIntAttribute("RepresentativeTextureHeight");

        if (representativeWidth > 0 && representativeHeight > 0)
        {
            return new Vector2(representativeWidth, representativeHeight);
        }

        return null;
    }

    /// <summary>
    /// What one Hammer mesh builder collects: the draw calls of one material with one tint. Geometry only welds
    /// within a material, and a Hammer mesh has a single tint.
    /// </summary>
    private readonly record struct HammerMeshGroup(string Material, Vector4 Tint);

    // Builders for the hammer geometry of every world node. Filled while the world nodes are walked, welded and
    // split into meshes once all of them are in, so geometry connects across the aggregates the compiler split it into.
    private readonly Dictionary<HammerMeshGroup, HammerMeshBuilder> WorldHammerMeshBuilders = [];
    private int WorldHammerMeshDrawCalls;

    // Selection sets (for easy access)
    private CMapSelectionSet? S2VSelectionSet;
    private CMapSelectionSet? HammerMeshesSelectionSet;
    private CMapSelectionSet? HammerMesheEntitiesSelectionSet;
    private CMapSelectionSet? StaticPropsSelectionSet;
    private CMapSelectionSet? PhysicsHullsSelectionSet;
    private CMapSelectionSet? HullEntitiesHullsSelectionSet;
    private CMapSelectionSet? PhysicsMeshesSelectionSet;
    private CMapSelectionSet? MeshEntitiesHullsSelectionSet;
    private CMapSelectionSet? OverlaysSelectionSet;
    private CMapSelectionSet? EntitiesSelectionSet;

    private List<CMapWorldLayer> WorldLayers { get; set; } = [];
    private Dictionary<int, MapNode> UniqueNodeIds { get; set; } = [];
    private CMapRootElement MapDocument { get; set; } = [];
    private List<CMapRootElement> AdditionalMapDocuments { get; set; } = [];

    private readonly IFileLoader FileLoader;

    /// <summary>Gets or sets the progress reporter.</summary>
    public IProgress<string>? ProgressReporter { get; set; }
    /// <summary>Gets the physics vertex matcher used for physics mesh processing.</summary>
    public PhysicsTriangleMatcher? PhysTriangleMatcher { get; private set; }

    //these all seem to be roughly hammer meshes in cs2
    private static bool SceneObjectShouldConvertToHammerMesh(string? modelName)
    {
        if (string.IsNullOrEmpty(modelName))
        {
            return false;
        }

        return modelName.Contains("_mesh_blocklight", StringComparison.Ordinal)
            || modelName.Contains("_mesh_overlay", StringComparison.Ordinal)
            || modelName.Contains("_c0_", StringComparison.Ordinal);
    }

    /// <summary>
    /// Extract a map from a resource. Accepted types include <see cref="ResourceType.Map"/>, <see cref="ResourceType.World"/>. TODO: <see cref="ResourceType.WorldNode"/> and <see cref="ResourceType.EntityLump"/>.
    /// </summary>
    public MapExtract(Resource resource, IFileLoader? fileLoader)
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
        return path.Replace('\\', '/').TrimEnd('/');
    }

    private static bool PathIsSubPath(string? equalOrSubPath, string path)
    {
        if (string.IsNullOrEmpty(equalOrSubPath))
        {
            return false;
        }

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

    /// <summary>
    /// Extracts the lump folder path from a vmap's external resource list.
    /// </summary>
    public static string GetLumpFolderFromVmapRERL(ResourceExtRefList? rerl)
    {
        if (rerl is null)
        {
            throw new InvalidDataException("Failed to get map lump folder.");
        }

        foreach (var info in rerl.ResourceRefInfoList)
        {
            if (info.Name.EndsWith("world.vrman", StringComparison.OrdinalIgnoreCase))
            {
                return GetLumpFolderFromWorldPath(info.Name);
            }
        }

        throw new InvalidDataException("Could not find world.vrman in vmap_c RERL.");
    }

    private static string GetLumpFolderFromWorldPath(string? worldPath)
    {
        var pathDirName = Path.GetDirectoryName(worldPath);

        if (string.IsNullOrEmpty(pathDirName))
        {
            throw new InvalidDataException("Failed to get lump folder directory name");
        }

        return NormalizePath(pathDirName);
    }

    private void InitWorldExtract(Resource vworld)
    {
        var lumpFolder = GetLumpFolderFromWorldPath(vworld.FileName);

        if (lumpFolder == null && vworld.FileName != null)
        {
            LumpFolder = vworld.FileName;
        }
        else if (lumpFolder != null)
        {
            LumpFolder = lumpFolder;
        }

        if (vworld.DataBlock is not World world)
        {
            throw new InvalidOperationException("Failed to get vworld");
        }

        EntityLumpNames = world.GetEntityLumpNames();
        WorldNodeNames = world.GetWorldNodeNames();

        WorldPhysicsName = GetWorldPhysicsName();
    }

    private string? GetWorldPhysicsName()
    {
        var manifestFileName = Path.Combine(LumpFolder, "world_physics.vrman_c");
        var manifestResource = FileLoader.LoadFile(manifestFileName);

        var manifest = (ResourceManifest?)manifestResource?.DataBlock;

        if (manifest == null || manifest.Resources.Count < 1)
        {
            return default;
        }

        var path = manifest.Resources.First().FirstOrDefault();

        if (string.IsNullOrEmpty(path))
        {
            return default;
        }

        return NormalizePath(path);
    }

    /// <summary>
    /// Loads the world physics collision data.
    /// </summary>
    public PhysAggregateData? LoadWorldPhysics()
    {
        if (WorldPhysicsName == null)
        {
            return default;
        }

        using var physicsResource = FileLoader.LoadFileCompiled(WorldPhysicsName);
        if (physicsResource == null || physicsResource.DataBlock == null)
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

    /// <summary>
    /// Maps tool texture names to their associated collision tag sets.
    /// </summary>
    // TODO: we should be parsing fgds and collision_*.txt files from game to remain correct.
    public static readonly Dictionary<string, HashSet<string>> ToolTextureMultiTags = new()
    {
        ["clip"] = ["npcclip", "playerclip"],
        ["invisibleladder"] = ["ladder", "passbullets"],
    };

    /// <summary>
    /// Gets the tool texture material path for a given surface tag combination.
    /// </summary>
    public static string GetToolTextureNameForCollisionTags(ModelExtract.SurfaceTagCombo combo)
    {
        var shortenedToolTextureName = GetToolTextureShortenedName_ForInteractStrings(combo.InteractAsStrings);

        return $"materials/tools/tools{shortenedToolTextureName}.vmat";
    }

    /// <summary>
    /// Gets the shortened tool texture name for a set of interact-as strings.
    /// </summary>
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

    /// <summary>
    /// Gets the auto-applied tool texture material for an entity class name.
    /// </summary>
    // These appear in FGD as "auto_apply_material"
    public static string? GetToolTextureForEntity(string? entityClassName)
    {
        if (string.IsNullOrEmpty(entityClassName))
        {
            return default;
        }

        return entityClassName switch
        {
            "env_cs_place" => "materials/tools/tools_cs_place.vmat",
            "func_nav_blocker" => "materials/tools/toolsnavattribute.vmat",
            "func_nav_markup" => "materials/tools/toolsnavattribute.vmat",
            "func_precipitation" => "materials/tools/toolsprecipitation.vmat",
            "post_processing_volume" => "materials/tools_postprocess_volume.vmat",
            "trigger_no_wards" => "materials/tools/tools_no_wards.vmat",
            _ => "materials/tools/toolstrigger.vmat",
        };
    }

    /// <summary>
    /// Converts the map extract to a content file with all dependencies.
    /// </summary>
    public ContentFile ToContentFile()
    {
        var vmap = new ContentFile
        {
            Data = ToValveMap(),
            FileName = GetMapOutputName(),
        };

        var part = 2;
        foreach (var additionalMap in AdditionalMapDocuments)
        {
            using var additionalDatamodel = new Datamodel.Datamodel("vmap", 29)
            {
                Root = additionalMap,
            };

            var ms = new MemoryStream();
            additionalDatamodel.Save(ms, "binary", 9);

            vmap.SubFiles.Add(new SubFile
            {
                Extract = ms.ToArray,
                FileName = GetMapOutputName(part++),
            });
        }

        foreach (var sceneObjectResourceName in SceneObjectsToExtract)
        {
            var sceneObjectNameCompiled = sceneObjectResourceName + GameFileLoader.CompiledFileSuffix;
            using var sceneObject = FileLoader.LoadFile(sceneObjectNameCompiled);

            if (sceneObject == null || sceneObject.DataBlock == null)
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

    private string GetMapOutputName(int part = 1)
    {
        if (part > 1)
        {
            return $"{LumpFolder}_d_autosplit_part{part}.vmap";
        }

        return $"{LumpFolder}_d.vmap";
    }

    /// <summary>
    /// Converts the map to a Valve map format as a byte array.
    /// </summary>
    public byte[] ToValveMap()
    {
        using var datamodel = new Datamodel.Datamodel("vmap", 29);

        datamodel.PrefixAttributes.Add("map_asset_references", AssetReferences);
        datamodel.Root = MapDocument = [];

        CreateSelectionSets(MapDocument.RootSelectionSet);

        var phys = LoadWorldPhysics();
        if (phys != null)
        {
            var collisionAttributes = phys.CollisionAttributes;
            var worldPhysMeshes = phys.Parts[0].Shape.Meshes.Where(m => collisionAttributes[m.CollisionAttributeIndex].GetStringProperty("m_CollisionGroupString") == "Default");

            PhysTriangleMatcher = new PhysicsTriangleMatcher(worldPhysMeshes.ToArray());

            // TODO: physics spheres and capsules are ignored
        }

        foreach (var worldNodeName in WorldNodeNames)
        {
            var worldNodeCompiled = worldNodeName + ".vwnod_c";
            FolderExtractFilter.Add(worldNodeCompiled);

            using var worldNode = FileLoader.LoadFile(worldNodeCompiled);
            if (worldNode != null && worldNode.DataBlock != null)
            {
                HandleWorldNode((WorldNode)worldNode.DataBlock);
            }
        }

        GenerateOverlays();

        // the hammer geometry of all world nodes is welded together here, then split back into objects
        var worldHammerMeshes = GenerateHammerMeshes(WorldHammerMeshBuilders);
        if (worldHammerMeshes.Count > 0)
        {
            var selectionSet = new CMapSelectionSet
            {
                SelectionSetName = $"hammer meshes ({WorldHammerMeshDrawCalls} welded draw calls, {worldHammerMeshes.Count} meshes)",
            };
            HammerMeshesSelectionSet?.Children.Add(selectionSet);

            foreach (var hammerMesh in worldHammerMeshes)
            {
                MapDocument.World.Children.Add(hammerMesh);
                selectionSet.SelectionSetData.SelectedObjects.Add(hammerMesh);
            }
        }

        AdditionalMapDocuments = SplitLargeMapDocument();

        var i = 2;
        foreach (var additionalMap in AdditionalMapDocuments)
        {
            MapDocument.World.Children.Add(new CMapPrefab
            {
                TargetMapPath = GetMapOutputName(i++),
            });
        }

        foreach (var entityLumpName in EntityLumpNames)
        {
            var entityLumpCompiled = entityLumpName + GameFileLoader.CompiledFileSuffix;
            FolderExtractFilter.Add(entityLumpCompiled);

            using var entityLumpResource = FileLoader.LoadFile(entityLumpCompiled);
            if (entityLumpResource != null && entityLumpResource.DataBlock != null)
            {
                GatherEntitiesFromLump((EntityLump)entityLumpResource.DataBlock);
            }
        }

        if (phys != null)
        {
            foreach (var hammermesh in PhysToHammerMeshes(phys))
            {
                MapDocument.World.Children.Add(hammermesh);
            }
        }

        ResolveOverlayProjectionTargets();

        using var stream = new MemoryStream();

        // datamodel.Save(stream, "keyvalues2", 4)
        datamodel.Save(stream, "binary", 9);

        return stream.ToArray();
    }

    private List<CMapRootElement> SplitLargeMapDocument()
    {
        const int OneGiB = 1024 * 1024 * 1024;
        var accumulatedMapMeshSize = 0;

        List<CMapRootElement> additionalMaps = [];

        var removedMeshes = new HashSet<CMapMesh>();
        foreach (var mesh in MapDocument.World.Children.OfType<CMapMesh>())
        {
            accumulatedMapMeshSize += TotalMapMeshSize(mesh);

            var thresholdCrossedTimes = accumulatedMapMeshSize / OneGiB;

            // if the threshold is crossed, we need to create a new vmap, and move the upcoming meshes to it.

            if (thresholdCrossedTimes > 0)
            {
                if (additionalMaps.Count < thresholdCrossedTimes)
                {
                    additionalMaps.Add([]);
                    ProgressReporter?.Report("Creating additional map document due to large editable mesh size.");
                }

                additionalMaps[^1].World.Children.Add(mesh);
                removedMeshes.Add(mesh);
            }
        }

        static bool RemoveSelectionSetRecursive(CMapSelectionSet? selectionSet, MapNode node)
        {
            if (selectionSet is null)
            {
                return false;
            }

            var removed = selectionSet.SelectionSetData.SelectedObjects.Remove(node);

            foreach (var child in selectionSet.Children.OfType<CMapSelectionSet>())
            {
                removed = removed || RemoveSelectionSetRecursive(child, node);
            }

            return removed;
        }

        foreach (var mesh in removedMeshes)
        {
            var removed = MapDocument.World.Children.Remove(mesh);

            // remove from any selection set as well
            removed = RemoveSelectionSetRecursive(S2VSelectionSet, mesh);
        }

        return additionalMaps;

        #region Mesh Size Calculation

        static int GetArraySize<T>(Datamodel.Array<T> array)
        {
            return array.Count * Unsafe.SizeOf<T>();
        }

        static void GetTotalDataStreamSizes(Datamodel.ElementArray streams, ref int accumulatedMapMeshSize)
        {
            CountSpecialType<int>(streams, ref accumulatedMapMeshSize);
            CountSpecialType<float>(streams, ref accumulatedMapMeshSize);
            CountSpecialType<Vector2>(streams, ref accumulatedMapMeshSize);
            CountSpecialType<Vector3>(streams, ref accumulatedMapMeshSize);
            CountSpecialType<Vector4>(streams, ref accumulatedMapMeshSize);

            static void CountSpecialType<T>(Datamodel.ElementArray streams, ref int accumulatedMapMeshSize)
            {
                foreach (var dataStream in streams.OfType<CDmePolygonMeshDataStream<T>>())
                {
                    accumulatedMapMeshSize += GetArraySize(dataStream.Data);
                }
            }
        }

        static int TotalMapMeshSize(CMapMesh mesh)
        {
            var meshSize = 0;
            // face-vertices
            GetTotalDataStreamSizes(mesh.MeshData.FaceVertexData.Streams, ref meshSize);

            // vertices
            meshSize += GetArraySize(mesh.MeshData.VertexEdgeIndices);
            meshSize += GetArraySize(mesh.MeshData.VertexDataIndices);
            GetTotalDataStreamSizes(mesh.MeshData.VertexData.Streams, ref meshSize);

            // edges
            meshSize += GetArraySize(mesh.MeshData.EdgeVertexIndices)
                + GetArraySize(mesh.MeshData.EdgeDataIndices)
                + GetArraySize(mesh.MeshData.EdgeOppositeIndices)
                + GetArraySize(mesh.MeshData.EdgeNextIndices)
                + GetArraySize(mesh.MeshData.EdgeFaceIndices)
                + GetArraySize(mesh.MeshData.EdgeDataIndices)
                + GetArraySize(mesh.MeshData.EdgeVertexDataIndices);
            GetTotalDataStreamSizes(mesh.MeshData.EdgeData.Streams, ref meshSize);

            // faces
            meshSize += GetArraySize(mesh.MeshData.FaceEdgeIndices);
            meshSize += GetArraySize(mesh.MeshData.FaceDataIndices);
            meshSize += GetArraySize(mesh.MeshData.Materials);
            GetTotalDataStreamSizes(mesh.MeshData.FaceData.Streams, ref meshSize);
            return meshSize;
        }

        #endregion Mesh Size Calculation
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

    /// <summary>
    /// Adds the render meshes of a model to builders, one per material and tint.
    /// </summary>
    /// <param name="model">Model whose embedded meshes are added.</param>
    /// <param name="resource">Resource the model came from.</param>
    /// <param name="transform">Transform applied to the geometry.</param>
    /// <param name="drawCallTint">Tint (gamma space, 0-255, alpha in W) per draw call index, defaults to the draw call's own tint and alpha.</param>
    /// <param name="builders">Builders to add to, created per material and tint as needed.</param>
    /// <returns>Number of draw calls added.</returns>
    private int AddRenderMeshToBuilders(Model model, Resource resource, Matrix4x4 transform, Func<int, Vector4>? drawCallTint, Dictionary<HammerMeshGroup, HammerMeshBuilder> builders)
    {
        var modelExtract = new ModelExtract(resource, FileLoader);
        modelExtract.GrabMaterialInputSignatures(resource);

        var drawCallCount = 0;

        // TODO: reference meshes
        foreach (var embedded in model.GetEmbeddedMeshes())
        {
            var submeshDrawCalls = new List<(DmeDag Dag, KVObject DrawCall)>();
            var dmxOptions = new ModelExtract.DatamodelRenderMeshExtractOptions
            {
                MaterialInputSignatures = modelExtract.MaterialInputSignatures,
                SplitDrawCallsIntoSeparateSubmeshes = true,
                SubmeshDrawCalls = submeshDrawCalls,
            };

            using var dmxMesh = ModelExtract.ConvertMeshToDatamodelMesh(embedded.Mesh, Path.GetFileNameWithoutExtension(resource.FileName ?? "mesh"), dmxOptions);

            for (var drawCallIndex = 0; drawCallIndex < submeshDrawCalls.Count; drawCallIndex++)
            {
                var (dag, drawCall) = submeshDrawCalls[drawCallIndex];
                drawCallCount++;

                var tint = drawCallTint?.Invoke(drawCallIndex) ?? GetDrawCallTint(drawCall);
                var material = Mesh.GetMaterialName(drawCall) ?? string.Empty;
                var group = new HammerMeshGroup(material, tint);

                if (!builders.TryGetValue(group, out var builder))
                {
                    builder = new HammerMeshBuilder()
                    {
                        PhysicsTriangleMatcher = PhysTriangleMatcher,
                        ProgressReporter = ProgressReporter,
                        Untriangulate = true,
                        TextureSizeProvider = GetMaterialTextureSize,
                    };
                    builders.Add(group, builder);
                }

                if (dag.Shape is DmeMesh meshShape)
                {
                    builder.AddRenderMesh(meshShape, transform);
                }
            }
        }

        return drawCallCount;
    }

    /// <summary>
    /// Welds the geometry of each builder and splits it into one Hammer mesh per connected island, named after
    /// the material.
    /// </summary>
    private static List<CMapMesh> GenerateHammerMeshes(Dictionary<HammerMeshGroup, HammerMeshBuilder> builders)
    {
        List<CMapMesh> hammerMeshes = [];
        var meshIndexPerMaterial = new Dictionary<string, int>();

        foreach (var (group, builder) in builders)
        {
            var materialName = Path.GetFileNameWithoutExtension(group.Material);
            var meshIndex = meshIndexPerMaterial.GetValueOrDefault(materialName);

            // the draw calls welded back together, one Hammer mesh per connected part, so separate objects the
            // compiler batched together come apart again
            foreach (var meshData in builder.GenerateMeshes(HammerMeshWeldDistance))
            {
                hammerMeshes.Add(new CMapMesh()
                {
                    Name = $"{materialName}_{meshIndex++}",
                    MeshData = meshData,
                    TintColor = ConvertToColor32(group.Tint),
                });
            }

            meshIndexPerMaterial[materialName] = meshIndex;
        }

        return hammerMeshes;
    }

    /// <summary>
    /// Converts the render meshes of one model into Hammer meshes, welded within the model and split into islands.
    /// </summary>
    /// <param name="model">Model whose embedded meshes are converted.</param>
    /// <param name="resource">Resource the model came from.</param>
    /// <param name="entityClassname">Class of the entity the meshes belong to, null for world geometry.</param>
    /// <param name="transform">Transform applied to the geometry.</param>
    internal List<CMapMesh> RenderMeshToHammerMesh(Model model, Resource resource, string? entityClassname = null, Matrix4x4? transform = null)
    {
        if (resource is null)
        {
            return [];
        }

        var builders = new Dictionary<HammerMeshGroup, HammerMeshBuilder>();
        var drawCallCount = AddRenderMeshToBuilders(model, resource, transform ?? Matrix4x4.Identity, null, builders);
        var hammerMeshesToReturn = GenerateHammerMeshes(builders);

        var hammerMeshEntitySelectionSet = new CMapSelectionSet();
        var drawSelectionSet = new CMapSelectionSet();

        foreach (var hammerMesh in hammerMeshesToReturn)
        {
            if (!string.IsNullOrEmpty(entityClassname))
            {
                hammerMeshEntitySelectionSet.SelectionSetData.SelectedObjects.Add(hammerMesh);
            }
            else
            {
                drawSelectionSet.SelectionSetData.SelectedObjects.Add(hammerMesh);
            }
        }

        hammerMeshEntitySelectionSet.SelectionSetName = "hammer mesh entity " + entityClassname + " (reconstructed from " + drawCallCount + (drawCallCount > 1 ? " draw calls )" : " draw call )");
        drawSelectionSet.SelectionSetName = "hammer mesh (" + drawCallCount + (drawCallCount > 1 ? " draw calls) " : " draw call) ") + Path.GetFileNameWithoutExtension(resource.FileName);

        if (!string.IsNullOrEmpty(entityClassname))
        {
            HammerMesheEntitiesSelectionSet?.Children.Add(hammerMeshEntitySelectionSet);
        }
        else if (resource.FileName!.Contains("_mesh_overlay", StringComparison.Ordinal))
        {
            OverlaysSelectionSet?.Children.Add(drawSelectionSet);
        }
        else if (SceneObjectShouldConvertToHammerMesh(resource.FileName))
        {
            HammerMeshesSelectionSet?.Children.Add(drawSelectionSet);
        }

        return hammerMeshesToReturn;
    }

    internal void AddOverlayGeometry(Model model, Resource resource, Matrix4x4 transform, KVObject sceneObject, ObjectTypeFlags objectFlags)
    {
        var modelExtract = new ModelExtract(resource, FileLoader);
        modelExtract.GrabMaterialInputSignatures(resource);

        foreach (var embedded in model.GetEmbeddedMeshes())
        {
            var submeshDrawCalls = new List<(DmeDag Dag, KVObject DrawCall)>();
            var dmxOptions = new ModelExtract.DatamodelRenderMeshExtractOptions
            {
                MaterialInputSignatures = modelExtract.MaterialInputSignatures,
                SplitDrawCallsIntoSeparateSubmeshes = true,
                SubmeshDrawCalls = submeshDrawCalls,
            };

            using var dmxMesh = ModelExtract.ConvertMeshToDatamodelMesh(embedded.Mesh, Path.GetFileNameWithoutExtension(resource.FileName ?? "overlay"), dmxOptions);

            foreach (var (dag, drawCall) in submeshDrawCalls)
            {
                if (dag.Shape is not DmeMesh shape)
                {
                    continue;
                }

                var material = Mesh.GetMaterialName(drawCall) ?? string.Empty;

                var vertexData = (DmeVertexData)shape.BaseStates[0];
                var positions = HammerMeshBuilder.GetElementArraySafe<Vector3>(vertexData, "position$0");
                var texCoords = HammerMeshBuilder.GetElementArraySafe<Vector2>(vertexData, "texcoord$0");

                if (positions is null || texCoords is null || positions.Count == 0 || texCoords.Count != positions.Count)
                {
                    continue;
                }

                var worldPositions = new Vector3[positions.Count];
                for (var i = 0; i < worldPositions.Length; i++)
                {
                    worldPositions[i] = transform.IsIdentity ? positions[i] : Vector3.Transform(positions[i], transform);
                }

                // the triangles of every face set, faces fanned
                var triangles = new List<int>();
                var faceIndices = new List<int>(4);

                foreach (var faceSet in shape.FaceSets.Cast<DmeFaceSet>())
                {
                    foreach (var index in faceSet.Faces)
                    {
                        if (index != -1)
                        {
                            faceIndices.Add(index);
                            continue;
                        }

                        for (var i = 1; i + 1 < faceIndices.Count; i++)
                        {
                            triangles.Add(faceIndices[0]);
                            triangles.Add(faceIndices[i]);
                            triangles.Add(faceIndices[i + 1]);
                        }

                        faceIndices.Clear();
                    }
                }

                var tintColor = sceneObject.GetSubCollection("m_vTintColor").ToVector4();
                var renderOrder = (int)sceneObject.GetIntegerProperty("m_nOverlayRenderOrder");
                var group = new OverlayGroup(material, renderOrder, tintColor, objectFlags & OverlayFlags);

                if (!WorldOverlayGeometry.TryGetValue(group, out var pieces))
                {
                    pieces = [];
                    WorldOverlayGeometry.Add(group, pieces);
                }

                // the projected triangles with the decal's texture coordinates on their corners,
                // one piece per compiled mesh: within one, whatever touches belongs together
                pieces.Add((worldPositions, [.. texCoords], [.. triangles]));
            }
        }
    }

    /// <summary>
    /// Rebuilds the map's overlays from the projected geometry collected by <see cref="AddOverlayGeometry"/>.
    /// </summary>
    private void GenerateOverlays()
    {
        var indices = new int[3];
        var corners = new HammerMeshBuilder.Corner[3];

        foreach (var (group, pieces) in WorldOverlayGeometry)
        {
            // a surface stacked behind another receives its own copy of the decal, drop those hidden copies over the
            // whole group at once or they come back as duplicated faces stacked on the rebuilt overlays, the copies
            // can sit in different compiled meshes when the surfaces fall into different cells
            var vertexCount = 0;
            var indexCount = 0;

            foreach (var piece in pieces)
            {
                vertexCount += piece.Positions.Length;
                indexCount += piece.Triangles.Length;
            }

            var positions = new Vector3[vertexCount];
            var texCoords = new Vector2[vertexCount];
            var triangles = new int[indexCount];
            var vertexOffset = 0;
            var indexOffset = 0;

            foreach (var piece in pieces)
            {
                piece.Positions.CopyTo(positions, vertexOffset);
                piece.TexCoords.CopyTo(texCoords, vertexOffset);

                for (var i = 0; i < piece.Triangles.Length; i++)
                {
                    triangles[indexOffset + i] = vertexOffset + piece.Triangles[i];
                }

                vertexOffset += piece.Positions.Length;
                indexOffset += piece.Triangles.Length;
            }

            var dropped = HammerOverlayBuilder.RemoveStackedDuplicates(positions, texCoords, triangles);

            // within one compiled mesh whatever touches belongs together, a multi face overlay included; across the
            // compiled meshes only seams where the texture mapping continues join, that is where one overlay was cut
            // by the compiler, while separate overlays of the same material that abut, or touch at a point, stay apart
            var combined = new PolygonMesh();
            var triangleOrdinal = 0;

            foreach (var piece in pieces)
            {
                var builder = new HammerMeshBuilder { ProgressReporter = ProgressReporter };
                var baseVertex = builder.AddVertices(piece.Positions);

                for (var t = 0; t + 2 < piece.Triangles.Length; t += 3, triangleOrdinal++)
                {
                    if (dropped[triangleOrdinal])
                    {
                        continue;
                    }

                    for (var i = 0; i < 3; i++)
                    {
                        indices[i] = baseVertex + piece.Triangles[t + i];
                        corners[i] = new HammerMeshBuilder.Corner(TexCoord: piece.TexCoords[piece.Triangles[t + i]]);
                    }

                    builder.AddFace(indices, group.Material, corners);
                }

                foreach (var welded in builder.Mesh.RemergeDrawCalls(HammerMeshWeldDistance))
                {
                    combined.MergeMesh(welded, out _, out _, out _);
                }
            }

            foreach (var geometry in combined.RemergeDrawCalls(HammerMeshWeldDistance, (hEdge, hPartner) => combined.TextureCoordinatesContinueAcross(hEdge, hPartner), mergeVertices: false))
            {
                var overlay = HammerOverlayBuilder.FromProjectedMesh(geometry, group.Material, GetMaterialTextureSize);
                if (overlay is null)
                {
                    continue;
                }

                // the geometry tells which meshes the overlay projects onto, once those exist
                OverlayReceivers.Add((overlay, geometry));

                overlay.RenderOrder = group.RenderOrder;

                if (group.Tint != Vector4.Zero)
                {
                    overlay.TintColor = ConvertToColor32(group.Tint * 255f);
                }

                overlay.DisabledInLowQuality = group.Flags.HasFlag(ObjectTypeFlags.DisabledInLowQuality);
                overlay.RenderToCubemaps = group.Flags.HasFlag(ObjectTypeFlags.RenderToCubemaps);
                overlay.RenderWithDynamic = group.Flags.HasFlag(ObjectTypeFlags.RenderWithDynamic);
                overlay.DisableShadows = group.Flags.HasFlag(ObjectTypeFlags.NoShadows);

                MapDocument.World.Children.Add(overlay);
                OverlaysSelectionSet?.SelectionSetData.SelectedObjects.Add(overlay);
            }
        }
    }

    /// <summary>
    /// Points each rebuilt overlay at the meshes it was compiled onto, in practice we need to mathc overlapping triangles similarly to
    /// PhysicsTriangleMatcher.
    /// </summary>
    private void ResolveOverlayProjectionTargets()
    {
        if (OverlayReceivers.Count == 0)
        {
            return;
        }

        // what an overlay can land on: rendered geometry and props, not physics and not other overlays
        var candidates = new List<CMapMesh>();
        var props = new List<CMapEntity>();
        var maxNodeId = 0;

        void Collect(MapNode node)
        {
            maxNodeId = Math.Max(maxNodeId, node.NodeID);

            if (node is CMapMesh mesh && node is not CMapStaticOverlay && !IsPhysicsMesh(mesh))
            {
                candidates.Add(mesh);
            }
            else if (node is CMapEntity entity
                && entity.EntityProperties.ContainsKey("classname") && entity.EntityProperties["classname"] as string == "prop_static"
                && entity.EntityProperties.ContainsKey("model"))
            {
                props.Add(entity);
            }

            foreach (var child in node.Children.OfType<MapNode>())
            {
                Collect(child);
            }
        }

        Collect(MapDocument.World);

        foreach (var mesh in candidates)
        {
            if (mesh.NodeID == 0)
            {
                mesh.NodeID = ++maxNodeId;
            }
        }

        foreach (var prop in props)
        {
            if (prop.NodeID == 0)
            {
                prop.NodeID = ++maxNodeId;
            }
        }

        // every candidate face in world space, in a grid for lookup by position
        const float PlaneDistance = 0.25f;
        const float CellSize = 64f;

        // what a matched face targets: a mesh targets itself, a face of an aggregate targets every fragment prop of
        // the aggregate instance, a face cannot tell which fragment it belongs to
        var targetSets = new List<int[]>();

        var faces = new List<(int TargetSet, Vector3[] Corners, Vector3 Normal, float Offset)>();
        var grid = new Dictionary<(int X, int Y, int Z), List<int>>();

        static (int X, int Y, int Z) Cell(Vector3 position)
            => ((int)MathF.Floor(position.X / CellSize), (int)MathF.Floor(position.Y / CellSize), (int)MathF.Floor(position.Z / CellSize));

        void InsertFace(int targetSet, Vector3[] corners)
        {
            // Newell normal and bounds
            var normal = Vector3.Zero;
            var min = new Vector3(float.MaxValue);
            var max = new Vector3(float.MinValue);

            for (var i = 0; i < corners.Length; i++)
            {
                var current = corners[i];
                var next = corners[(i + 1) % corners.Length];
                normal += new Vector3((current.Y - next.Y) * (current.Z + next.Z), (current.Z - next.Z) * (current.X + next.X), (current.X - next.X) * (current.Y + next.Y));
                min = Vector3.Min(min, current);
                max = Vector3.Max(max, current);
            }

            if (normal.LengthSquared() < 1e-10f)
            {
                return;
            }

            normal = Vector3.Normalize(normal);
            faces.Add((targetSet, corners, normal, Vector3.Dot(normal, corners[0])));

            var (minX, minY, minZ) = Cell(min - new Vector3(PlaneDistance));
            var (maxX, maxY, maxZ) = Cell(max + new Vector3(PlaneDistance));

            for (var x = minX; x <= maxX; x++)
            {
                for (var y = minY; y <= maxY; y++)
                {
                    for (var z = minZ; z <= maxZ; z++)
                    {
                        if (!grid.TryGetValue((x, y, z), out var cellFaces))
                        {
                            cellFaces = [];
                            grid.Add((x, y, z), cellFaces);
                        }

                        cellFaces.Add(faces.Count - 1);
                    }
                }
            }
        }

        foreach (var mesh in candidates)
        {
            var positions = mesh.MeshData.VertexData.Streams.OfType<CDmePolygonMeshDataStream<Vector3>>().FirstOrDefault(s => s.Name == "position:0")?.Data;
            if (positions is null)
            {
                continue;
            }

            var meshTargetSet = targetSets.Count;
            targetSets.Add([mesh.NodeID]);

            var angles = mesh.Angles;
            var transform = Matrix4x4.CreateScale(mesh.Scales)
                * Matrix4x4.CreateFromQuaternion(EntityTransformHelper.EulerAnglesToQuaternion(new Vector3(angles.Pitch, angles.Yaw, angles.Roll)))
                * Matrix4x4.CreateTranslation(mesh.Origin);

            var meshData = mesh.MeshData;
            var corners = new List<Vector3>();

            for (var faceIndex = 0; faceIndex < meshData.FaceEdgeIndices.Count; faceIndex++)
            {
                corners.Clear();

                var firstEdge = meshData.FaceEdgeIndices[faceIndex];
                var edge = firstEdge;
                do
                {
                    corners.Add(Vector3.Transform(positions[meshData.VertexDataIndices[meshData.EdgeVertexIndices[edge]]], transform));
                    edge = meshData.EdgeNextIndices[edge];
                }
                while (edge != firstEdge && corners.Count < 1024);

                if (corners.Count < 3)
                {
                    continue;
                }

                InsertFace(meshTargetSet, [.. corners]);
            }
        }

        // only the props near some overlay's receivers are worth loading and matching
        const float CoarseCellSize = 1024f;

        static (int X, int Y, int Z) CoarseCell(Vector3 position)
            => ((int)MathF.Floor(position.X / CoarseCellSize), (int)MathF.Floor(position.Y / CoarseCellSize), (int)MathF.Floor(position.Z / CoarseCellSize));

        var coarse = new HashSet<(int X, int Y, int Z)>();

        foreach (var (_, geometry) in OverlayReceivers)
        {
            foreach (var hVertex in geometry.VertexHandles)
            {
                coarse.Add(CoarseCell(geometry.Positions[hVertex]));
            }
        }

        bool NearOverlays(Vector3 position)
        {
            var (cx, cy, cz) = CoarseCell(position);

            for (var x = cx - 1; x <= cx + 1; x++)
            {
                for (var y = cy - 1; y <= cy + 1; y++)
                {
                    for (var z = cz - 1; z <= cz + 1; z++)
                    {
                        if (coarse.Contains((x, y, z)))
                        {
                            return true;
                        }
                    }
                }
            }

            return false;
        }

        // the fragment props of one aggregate reference the per draw call models this extract generates, which don't
        // exist in the compiled map: their geometry is one draw call of the aggregate's own model, its name is theirs
        // without the draw suffix and the suffix is the draw call's index. Every fragment carries a transform of its
        // own, so each prop places just its draw call's triangles; an ordinary prop places its whole model
        static string StripDrawSuffix(string modelName, out int drawCall)
        {
            drawCall = -1;

            if (!modelName.Contains("_agg_", StringComparison.Ordinal))
            {
                return modelName;
            }

            var suffix = modelName.LastIndexOf("_draw", StringComparison.Ordinal);

            if (suffix < 0 || !modelName.EndsWith(".vmdl", StringComparison.Ordinal)
                || !int.TryParse(modelName.AsSpan(suffix + "_draw".Length, modelName.Length - suffix - "_draw".Length - ".vmdl".Length), out drawCall))
            {
                drawCall = -1;
                return modelName;
            }

            return string.Concat(modelName.AsSpan(0, suffix), ".vmdl");
        }

        // the render triangles of each prop model, in model space with the range of each draw call, loaded once per model
        var modelTriangles = new Dictionary<string, (Vector3[] Positions, int[] Triangles, List<(int Start, int Length)> DrawCalls)?>();

        foreach (var prop in props)
        {
            if (!NearOverlays(prop.Origin))
            {
                continue;
            }

            var loadName = StripDrawSuffix((string)prop.EntityProperties["model"]!, out var drawCall);

            if (!modelTriangles.TryGetValue(loadName, out var data))
            {
                data = LoadModelTriangles(loadName);
                modelTriangles[loadName] = data;
            }

            if (data is not { } propModel)
            {
                continue;
            }

            var (start, length) = drawCall >= 0 && drawCall < propModel.DrawCalls.Count
                ? propModel.DrawCalls[drawCall]
                : (0, propModel.Triangles.Length);

            var propAngles = prop.Angles;
            var propTransform = Matrix4x4.CreateScale(prop.Scales)
                * Matrix4x4.CreateFromQuaternion(EntityTransformHelper.EulerAnglesToQuaternion(new Vector3(propAngles.Pitch, propAngles.Yaw, propAngles.Roll)))
                * Matrix4x4.CreateTranslation(prop.Origin);

            // a recentred fragment's geometry is already in world space in the aggregate, its origin is only the
            // recentring, while an instanced fragment's geometry is local to its transform
            if (drawCall >= 0 && !InstancedAggregateModels.Contains(loadName))
            {
                propTransform = Matrix4x4.Identity;
            }

            var propTargetSet = targetSets.Count;
            targetSets.Add([prop.NodeID]);

            for (var t = start; t + 2 < start + length; t += 3)
            {
                var a = Vector3.Transform(propModel.Positions[propModel.Triangles[t]], propTransform);

                if (!NearOverlays(a))
                {
                    continue;
                }

                var b = Vector3.Transform(propModel.Positions[propModel.Triangles[t + 1]], propTransform);
                var c = Vector3.Transform(propModel.Positions[propModel.Triangles[t + 2]], propTransform);
                InsertFace(propTargetSet, [a, b, c]);
            }
        }

        foreach (var (overlay, geometry) in OverlayReceivers)
        {
            var targets = new SortedSet<int>();

            foreach (var hFace in geometry.FaceHandles)
            {
                // the projected faces are triangles
                var hEdge = hFace.Edge;
                var a = geometry.Positions[hEdge.Vertex];
                hEdge = hEdge.NextEdge;
                var b = geometry.Positions[hEdge.Vertex];
                hEdge = hEdge.NextEdge;
                var c = geometry.Positions[hEdge.Vertex];
                var centre = (a + b + c) / 3f;
                var triangleNormal = Vector3.Cross(b - a, c - a);

                if (triangleNormal.LengthSquared() < 1e-10f || !grid.TryGetValue(Cell(centre), out var cellFaces))
                {
                    continue;
                }

                triangleNormal = Vector3.Normalize(triangleNormal);

                foreach (var faceIndex in cellFaces)
                {
                    var (targetSet, corners, normal, offset) = faces[faceIndex];

                    if (targets.Contains(targetSets[targetSet][0])
                     || Vector3.Dot(normal, triangleNormal) < 0.9f
                     || MathF.Abs(Vector3.Dot(normal, centre) - offset) > PlaneDistance
                     || !PointInFace(centre, corners, normal, PlaneDistance))
                    {
                        continue;
                    }

                    foreach (var nodeId in targetSets[targetSet])
                    {
                        targets.Add(nodeId);
                    }
                }
            }

            // the targets only count in the target objects projection mode, everything else stays at project on all
            if (targets.Count > 0)
            {
                overlay.ProjectionTargets.AddRange(targets);
                overlay.ProjectionMode = 3;
            }
        }

        // the render triangles of a prop model at its first lod, in model space
        (Vector3[] Positions, int[] Triangles, List<(int Start, int Length)> DrawCalls)? LoadModelTriangles(string modelName)
        {
            using var modelResource = FileLoader.LoadFileCompiled(modelName);
            if (modelResource?.DataBlock is not Model propModel)
            {
                return null;
            }

            var positions = new List<Vector3>();
            var triangles = new List<int>();
            var drawCalls = new List<(int Start, int Length)>();

            // straight out of the vertex and index buffers, converting these models properly is far too slow for
            // what is only a triangle lookup
            void AddMesh(Mesh propMesh)
            {
                var vbib = propMesh.VBIB;
                var bufferStarts = new Dictionary<int, int>();

                foreach (var sceneObject in propMesh.Data.GetArray("m_sceneObjects"))
                {
                    foreach (var drawCall in sceneObject.GetArray("m_drawCalls"))
                    {
                        // every draw call gets a range, even an empty one, its index is how fragment props find theirs
                        var rangeStart = triangles.Count;
                        drawCalls.Add((rangeStart, 0));

                        var vertexBufferIndex = drawCall.GetArray("m_vertexBuffers")[0].GetInt32Property("m_hBuffer");

                        if (!bufferStarts.TryGetValue(vertexBufferIndex, out var bufferStart))
                        {
                            var vertexBuffer = vbib.VertexBuffers[vertexBufferIndex];
                            var positionAttribute = vertexBuffer.InputLayoutFields.FirstOrDefault(a => a.SemanticName == "POSITION");

                            if (positionAttribute.SemanticName != "POSITION")
                            {
                                bufferStarts[vertexBufferIndex] = -1;
                                continue;
                            }

                            bufferStart = positions.Count;
                            bufferStarts[vertexBufferIndex] = bufferStart;
                            positions.AddRange(VBIB.GetVector3AttributeArray(vertexBuffer, positionAttribute));
                        }

                        if (bufferStart < 0)
                        {
                            continue;
                        }

                        var indexBuffer = vbib.IndexBuffers[drawCall.GetSubCollection("m_indexBuffer").GetInt32Property("m_hBuffer")];
                        var indices = GltfModelExporter.ReadIndices(indexBuffer, drawCall.GetInt32Property("m_nStartIndex"), drawCall.GetInt32Property("m_nIndexCount"), drawCall.GetInt32Property("m_nBaseVertex"));

                        foreach (var index in indices)
                        {
                            triangles.Add(bufferStart + index);
                        }

                        drawCalls[^1] = (rangeStart, triangles.Count - rangeStart);
                    }
                }
            }

            foreach (var embedded in propModel.GetEmbeddedMeshesAndLoD())
            {
                if ((embedded.LoDMask & 1) != 0)
                {
                    AddMesh(embedded.Mesh);
                }
            }

            foreach (var reference in propModel.GetReferenceMeshNamesAndLoD())
            {
                if ((reference.LoDMask & 1) == 0)
                {
                    continue;
                }

                using var meshResource = FileLoader.LoadFileCompiled(reference.MeshName);
                if (meshResource?.DataBlock is not Mesh referenceMesh)
                {
                    continue;
                }

                propModel.SetExternalMeshData(referenceMesh);
                AddMesh(referenceMesh);
            }

            return positions.Count > 0 ? (positions.ToArray(), triangles.ToArray(), drawCalls) : null;
        }

        static bool IsPhysicsMesh(CMapMesh mesh)
            => mesh.MeshData.Materials.Count > 0 && mesh.MeshData.Materials.All(m =>
                m.Contains("/_vrf/physics_surfaces/", StringComparison.Ordinal) || m.StartsWith("materials/tools/", StringComparison.Ordinal));

        // inside the polygon in the plane, or within the tolerance of one of its edges
        static bool PointInFace(Vector3 point, Vector3[] corners, Vector3 normal, float tolerance)
        {
            var absNormal = Vector3.Abs(normal);
            var (u, v) = absNormal.X >= absNormal.Y && absNormal.X >= absNormal.Z ? (1, 2) : absNormal.Y >= absNormal.Z ? (0, 2) : (0, 1);

            var inside = false;
            var pu = point[u];
            var pv = point[v];

            for (int i = 0, j = corners.Length - 1; i < corners.Length; j = i++)
            {
                var ci = corners[i];
                var cj = corners[j];

                if ((ci[v] > pv) != (cj[v] > pv) && pu < (cj[u] - ci[u]) * (pv - ci[v]) / (cj[v] - ci[v]) + ci[u])
                {
                    inside = !inside;
                }

                // distance to the edge
                var edge = cj - ci;
                var along = Math.Clamp(Vector3.Dot(point - ci, edge) / MathF.Max(edge.LengthSquared(), 1e-12f), 0f, 1f);

                if (Vector3.Distance(point, ci + edge * along) <= tolerance)
                {
                    return true;
                }
            }

            return inside;
        }
    }

    /// <summary>
    /// Tint of a draw call in gamma space, 0-255, with its alpha in W.
    /// </summary>
    private static Vector4 GetDrawCallTint(KVObject drawCall)
    {
        var tint = Vector3.One * 255f;

        if (drawCall.ContainsKey("m_vTintColor"))
        {
            tint *= ColorSpace.SrgbLinearToGamma(drawCall.GetSubCollection("m_vTintColor").ToVector3());
        }

        var alpha = 255f * drawCall.GetFloatProperty("m_flAlpha", 1f);

        return new Vector4(tint, alpha);
    }

    internal List<CMapMesh> PhysToHammerMeshes(PhysAggregateData phys, Vector3 positionOffset = new Vector3(), string? entityClassname = null)
    {
        var cMapMeshesToReturn = new List<CMapMesh>();

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
                SelectionSetName = "physics shape (" + shape.Meshes.Length + " original meshes)"
            };

            var meshesEntitySelectionSet = new CMapSelectionSet
            {
                SelectionSetName = "physics mesh entity " + entityClassname + " (reconstructed from " + shape.Meshes.Length + (shape.Meshes.Length > 1 ? " meshes)" : " mesh)")
            };

            foreach (var hull in shape.Hulls)
            {
                var hammerMeshBuilder = new HammerMeshBuilder { Untriangulate = true, TextureSizeProvider = GetMaterialTextureSize };
                hammerMeshBuilder.AddPhysHull(hull, phys, GetAndExportAutoPhysicsMaterialName, positionOffset, materialOverride);
                var meshData = hammerMeshBuilder.GenerateMesh();

                if (meshData.FaceEdgeIndices.Count == 0)
                {
                    continue;
                }

                var hammerMesh = new CMapMesh() { MeshData = meshData };

                if (string.IsNullOrEmpty(entityClassname))
                {
                    hullsSelectionSet.SelectionSetData.SelectedObjects.Add(hammerMesh);
                }
                else
                {
                    hullsEntitySelectionSet.SelectionSetData.SelectedObjects.Add(hammerMesh);
                }

                cMapMeshesToReturn.Add(hammerMesh);
            }

            // physics meshes are welded together and split by connectivity like the render geometry is, however
            // material doesnt matter and neither does tint
            var physicsMeshBuilder = new HammerMeshBuilder { Untriangulate = true, TextureSizeProvider = GetMaterialTextureSize };

            foreach (var mesh in shape.Meshes)
            {
                var deletedTriangles = PhysTriangleMatcher?.PhysicsMeshes.FirstOrDefault(physicsMesh => physicsMesh.Mesh == mesh)?.DeletedTriangles;
                physicsMeshBuilder.AddPhysMesh(mesh, phys, GetAndExportAutoPhysicsMaterialName, deletedTriangles, positionOffset, materialOverride);
            }

            if (shape.Meshes.Length > 0)
            {
                foreach (var meshData in physicsMeshBuilder.GenerateMeshes(HammerMeshWeldDistance))
                {
                    if (meshData.FaceEdgeIndices.Count == 0)
                    {
                        continue;
                    }

                    var hammerMesh = new CMapMesh() { MeshData = meshData };

                    var selectionSet = string.IsNullOrEmpty(entityClassname) ? meshesSelectionSet : meshesEntitySelectionSet;
                    selectionSet.SelectionSetData.SelectedObjects.Add(hammerMesh);

                    cMapMeshesToReturn.Add(hammerMesh);
                }
            }

            if (shape.Hulls.Length != 0)
            {
                if (string.IsNullOrEmpty(entityClassname))
                {
                    PhysicsHullsSelectionSet?.Children.Add(hullsSelectionSet);
                }
                else
                {
                    HullEntitiesHullsSelectionSet?.Children.Add(hullsEntitySelectionSet);
                }
            }

            if (shape.Meshes.Length != 0)
            {
                if (string.IsNullOrEmpty(entityClassname))
                {
                    PhysicsMeshesSelectionSet?.Children.Add(meshesSelectionSet);
                }
                else
                {
                    MeshEntitiesHullsSelectionSet?.Children.Add(meshesEntitySelectionSet);
                }
            }
        }

        return cMapMeshesToReturn;
    }

    static Datamodel.Color ConvertToColor32(Vector4 tint)
    {
        var color32 = unchecked(stackalloc byte[] { (byte)tint.X, (byte)tint.Y, (byte)tint.Z, (byte)tint.W });
        return Datamodel.Color.FromBytes(color32);
    }

    private void HandleWorldNode(WorldNode node)
    {
        var layerNames = node.LayerNames;
        var layerNodes = new List<MapNode>(layerNames.Count);
        foreach (var layerName in layerNames)
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

        void AddChildMaybeGrouped(MapNode node, MapNode child, string? selectionSetName)
        {
            node.Children.Add(child);

            if (!string.IsNullOrEmpty(selectionSetName))
            {
                if (S2VSelectionSet is not null)
                {
                    var selectionSet = (CMapSelectionSet?)S2VSelectionSet.Children
                    .FirstOrDefault(set => ((CMapSelectionSet)set).SelectionSetName == selectionSetName);

                    if (selectionSet is null)
                    {
                        selectionSet = new CMapSelectionSet { SelectionSetName = selectionSetName };
                        S2VSelectionSet.Children.Add(selectionSet);
                    }

                    selectionSet.SelectionSetData.SelectedObjects.Add(child);
                }
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
            var modelName = sceneObject.GetStringProperty("m_renderableModel");
            var meshName = sceneObject.GetStringProperty("m_renderable");

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

            var objectTransform = sceneObject.GetArray("m_vTransform").ToMatrix4x4();

            if (SceneObjectShouldConvertToHammerMesh(modelName))
            {
                var meshNameCompiled = modelName + GameFileLoader.CompiledFileSuffix;
                using var mesh = FileLoader.LoadFile(meshNameCompiled);

                if (mesh is null || mesh.DataBlock is null)
                {
                    return;
                }

                var model = (Model)mesh.DataBlock;

                // overlays are not normal hammer geo, their projected geometry is collected and reconstructed at the end
                if (modelName!.Contains("_mesh_overlay", StringComparison.Ordinal))
                {
                    AddOverlayGeometry(model, mesh, objectTransform, sceneObject, objectFlags);
                    return;
                }

                // Source 2 bakes a mesh's scale into its vertices, so bake it here and keep only origin/angles on the node.
                var meshOrigin = Vector3.Zero;
                var meshAngles = new Datamodel.QAngle();
                var scaleTransform = Matrix4x4.Identity;
                if (!objectTransform.IsIdentity)
                {
                    if (!Matrix4x4.Decompose(objectTransform, out var scales, out var rotation, out var translation))
                    {
                        throw new InvalidOperationException("Matrix decompose failed");
                    }

                    meshOrigin = translation;
                    meshAngles = EntityTransformHelper.ToEulerAngles(rotation);
                    scaleTransform = Matrix4x4.CreateScale(scales);
                }

                foreach (var hammermesh in RenderMeshToHammerMesh(model, mesh, transform: scaleTransform))
                {
                    hammermesh.Origin = meshOrigin;
                    hammermesh.Angles = meshAngles;
                    MapDocument.World.Children.Add(hammermesh);
                }
                return;
            }
            else
            {
                SceneObjectsToExtract.Add(modelName!);
            }

            AssetReferences.Add(modelName!);

            var propStatic = new CMapEntity()
                .WithClassName("prop_static")
                .WithProperty("model", modelName!);

            if (!objectTransform.IsIdentity)
            {
                if (!Matrix4x4.Decompose(objectTransform, out var scales, out var rotation, out var translation))
                {
                    throw new InvalidOperationException("Matrix decompose failed");
                }

                propStatic.Origin = translation;
                propStatic.Angles = EntityTransformHelper.ToEulerAngles(rotation);
                propStatic.Scales = scales;
            }

            var fadeStartDistance = sceneObject.GetDoubleProperty("m_flFadeStartDistance");
            var fadeEndDistance = sceneObject.GetDoubleProperty("m_flFadeEndDistance");
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

            var skin = sceneObject.GetStringProperty("m_skin");
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
                ModelsToExtract.Add(modelName!);
            }

            if (Path.GetFileName(modelName!).Contains("nomerge", StringComparison.Ordinal))
            {
                propStatic.EntityProperties["disablemeshmerging"] = StringBool(true);
            }

            StaticPropFinalize(propStatic, layerIndex, layerNodes, isEmbeddedModel);
        }

        void ProcessAggregate(KVObject agg, int layerIndex, List<MapNode> layerNodes)
        {
            var modelName = agg.GetStringProperty("m_renderableModel");
            var anyFlags = agg.GetEnumValue<ObjectTypeFlags>("m_anyFlags", normalize: true);
            var allFlags = agg.GetEnumValue<ObjectTypeFlags>("m_allFlags", normalize: true);

            var hasModelFlag = allFlags.HasFlag(ObjectTypeFlags.Model);
            var convertToHalfEdge = !hasModelFlag;

            var aggregateMeshes = agg.GetArray("m_aggregateMeshes");

            IReadOnlyList<KVObject> drawCalls = [];
            var drawCenters = Array.Empty<Vector3>();

            var transformIndex = 0;
            var fragmentTransforms = agg.ContainsKey("m_fragmentTransforms")
                ? agg.GetArray("m_fragmentTransforms")
                : [];

            var aggregateHasTransforms = fragmentTransforms.Count > 0;

            if (aggregateHasTransforms)
            {
                InstancedAggregateModels.Add(modelName);
            }

            FolderExtractFilter.Add(modelName);
            using var modelRes = FileLoader.LoadFileCompiled(modelName);

            if (modelRes is null || modelRes.DataBlock is null)
            {
                return;
            }

            var model = (Model)modelRes.DataBlock;

            // TODO: reference meshes
            var mesh = ((Model)modelRes.DataBlock).GetEmbeddedMeshes().First();
            var sceneObject = mesh.Mesh.Data.GetArray("m_sceneObjects")[0];
            drawCalls = sceneObject.GetArray("m_drawCalls");

            if (!convertToHalfEdge)
            {
                if (!aggregateHasTransforms)
                {
                    drawCenters = (sceneObject.ContainsKey("m_drawBounds") ? sceneObject.GetArray("m_drawBounds") : [])
                        .Select(aabb => (aabb.GetSubCollection("m_vMinBounds").ToVector3() + aabb.GetSubCollection("m_vMaxBounds").ToVector3()) / 2f)
                        .ToArray();
                }

                var modelFiles = ModelExtract.GetContentFiles_DrawCallSplit(modelRes, FileLoader, drawCenters, drawCalls.Count);
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
                if (aggregateHasTransforms)
                {
                    throw new InvalidOperationException("Unhandled aggregate with instanced transforms exported as hammer mesh!");
                }

                // the fragment tint multiplies the draw call tint, one fragment per draw call is expected here
                var fragmentTints = new Dictionary<int, Vector3>();
                foreach (var fragment in aggregateMeshes)
                {
                    if (fragment.ContainsKey("m_vTintColor"))
                    {
                        fragmentTints.TryAdd(fragment.GetInt32Property("m_nDrawCallIndex"), fragment.GetSubCollection("m_vTintColor").ToVector3());
                    }
                }

                Vector4 HammerMeshTint(int drawCallIndex)
                {
                    var tint = GetDrawCallTint(drawCalls[drawCallIndex]);

                    if (fragmentTints.TryGetValue(drawCallIndex, out var fragmentTint))
                    {
                        tint = new Vector4(fragmentTint * new Vector3(tint.X, tint.Y, tint.Z) / 255f, tint.W);
                    }

                    return tint;
                }

                // world geometry is welded across all aggregates, the meshes are made once every world node is in
                WorldHammerMeshDrawCalls += AddRenderMeshToBuilders(model, modelRes, Matrix4x4.Identity, HammerMeshTint, WorldHammerMeshBuilders);

                return;
            }

            drawSelectionSet.SelectionSetName = "prop_static render mesh " + (aggregateHasTransforms ? "(instanced) " : "(" + drawCalls.Count + " split draw meshes) ") + Path.GetFileNameWithoutExtension(modelName);
            StaticPropsSelectionSet?.Children.Add(drawSelectionSet);

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
                tint *= ColorSpace.SrgbLinearToGamma(drawCallTint);
                alpha *= drawCall.GetFloatProperty("m_flAlpha");

                var fragmentModelName = ModelExtract.GetFragmentModelName(modelName, i);
                AssetReferences.Add(fragmentModelName);

                var instance = NewPropStatic(fragmentModelName);

                if (aggregateHasTransforms)
                {
                    var transform = fragmentTransforms[transformIndex++].ToMatrix4x4();
                    if (!Matrix4x4.Decompose(transform, out var scales, out var rotation, out var translation))
                    {
                        throw new InvalidOperationException("Matrix decompose failed");
                    }

                    instance.Origin = translation;
                    var angles = EntityTransformHelper.ToEulerAngles(rotation);
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

        var sceneObjects = node.SceneObjects;
        var sceneObjectLayerIndices = node.SceneObjectLayerIndices;
        for (var i = 0; i < sceneObjects.Count; i++)
        {
            var sceneObject = sceneObjects[i];
            var layerIndex = (int)(sceneObjectLayerIndices?[i] ?? -1);
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
        => NormalizePath(Path.Combine(rootFolder, "_vrf", "physics_surfaces", surfaceProperty + ".vmat"))!;

    private string GetAndExportAutoPhysicsMaterialName(string surfaceProperty)
    {
        var materialName = GetAutoPhysicsMaterialName(LumpFolder, surfaceProperty);
        ProceduralPhysMaterialsToExtract.Add((materialName, surfaceProperty));
        return materialName;
    }

    private static ContentFile GeneratePhysicsTagMaterial(string materialName, string surfaceProperty)
    {
        var textureName = Path.ChangeExtension(materialName, ".png");

        var root = ValveKeyValue.KVObject.ListCollection();
        root.Add("shader", "generic.vfx");
        root.Add("F_TRANSLUCENT", 1);
        root.Add("TextureTranslucency", "[0.700000 0.700000 0.700000 0.000000]");
        root.Add("TextureColor", textureName);

        var attributes = ValveKeyValue.KVObject.ListCollection();
        attributes.Add("mapbuilder.nodraw", 1);
        attributes.Add("tools.toolsmaterial", 1);
        attributes.Add("physics.nodefaultsimplification", 1);
        root.Add("Attributes", attributes);

        var systemAttributes = ValveKeyValue.KVObject.ListCollection();
        systemAttributes.Add("PhysicsSurfaceProperties", surfaceProperty);
        systemAttributes.Add("WorldMappingWidth", AutoPhysicsMaterialWorldMapping);
        systemAttributes.Add("WorldMappingHeight", AutoPhysicsMaterialWorldMapping);
        root.Add("SystemAttributes", systemAttributes);

        using var ms = new MemoryStream();
        var doc = new ValveKeyValue.KVDocument(new(), "Layer0", root);
        ValveKeyValue.KVSerializer.Create(ValveKeyValue.KVSerializationFormat.KeyValues1Text).Serialize(ms, doc);

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
    // Child lumps discovered while walking, keyed by name so a point_template can claim its own lump by
    // entitylumpname (see GatherEntitiesFromLump below). Whatever is left once the root walk finishes was
    // not referenced by any template, i.e. orphans to emit at their stored positions.
    // The renderer and glTF exporter share EntityLumpTraversal for the same walk; we keep a separate copy
    // here because extraction additionally needs orphan emission and parent-transform threading.
    private readonly Dictionary<string, EntityLump> ChildEntityLumps = [];

    private void GatherEntitiesFromLump(EntityLump entityLump)
    {
        GatherEntitiesFromLump(entityLump, null);

        while (ChildEntityLumps.Count > 0)
        {
            var (childLumpName, childEntityLump) = ChildEntityLumps.First();
            ChildEntityLumps.Remove(childLumpName);

            if (childEntityLump.GetEntities().Count > 0)
            {
                ProgressReporter?.Report($"Entity lump {childLumpName} is not referenced by any point_template, emitting its entities at stored positions.");
            }

            GatherEntitiesFromLump(childEntityLump, null);
        }
    }

    private void GatherEntitiesFromLump(EntityLump entityLump, Matrix4x4? parentTransform)
    {
        foreach (var childEntityName in entityLump.GetChildEntityNames())
        {
            using var entityLumpResource = FileLoader.LoadFileCompiled(childEntityName);
            if (entityLumpResource?.DataBlock is EntityLump childEntityLump)
            {
                ChildEntityLumps.TryAdd(childEntityLump.Name, childEntityLump);
            }
        }

        Dictionary<int, CMapSelectionSet> lineageSelectionSets = [];

        foreach (var compiledEntity in entityLump.GetEntities())
        {
            var className = compiledEntity.GetStringProperty("classname");

            if (className == null)
            {
                continue;
            }

            if (className == "worldspawn")
            {
                AddProperties(className, compiledEntity, MapDocument.World);
                MapDocument.World.EntityProperties["description"] = $"Decompiled with {StringToken.VRF_GENERATOR}";
                var mapType = compiledEntity.GetStringProperty("mapusagetype");
                if (mapType != null)
                {
                    MapDocument.World.MapUsageType = mapType;
                }
                continue;
            }

            var mapEntity = new CMapEntity();
            var entityLineage = AddProperties(className, compiledEntity, mapEntity);
            var localTransform = EntityTransformHelper.ToTransformationMatrix(compiledEntity);
            var worldTransform = parentTransform is { } parent ? localTransform * parent : localTransform;
            if (parentTransform is not null)
            {
                // parent transform is rigid (rotation and translation only), so worldTransform is affine and
                // decomposes cleanly unless the child itself shears (non-uniform scale + rotation). Where it
                // does shear, keep the entity's own placement rather than silently writing out an identity
                // rotation the decompose left behind.
                if (Matrix4x4.Decompose(worldTransform, out var scales, out var rotation, out var translation))
                {
                    mapEntity.Origin = translation;
                    mapEntity.Angles = EntityTransformHelper.ToEulerAngles(rotation);
                    mapEntity.Scales = scales;
                }
                else
                {
                    ProgressReporter?.Report($"Failed to decompose transform for entity '{className}', its placement may be wrong.");
                }

                if (TryDeduplicateTemplateChild(compiledEntity))
                {
                    continue;
                }
            }

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
                            EntitiesSelectionSet?.Children.Add(selectionSet);
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

            if (className == "point_template")
            {
                // empty when the template has no compiled children
                var entityLumpName = compiledEntity.GetStringProperty("entitylumpname");
                if (!string.IsNullOrEmpty(entityLumpName))
                {
                    if (ChildEntityLumps.Remove(entityLumpName, out var childEntityLump))
                    {
                        var childLumpTransform = EntityTransformHelper.ToRigidTransformationMatrix(compiledEntity) * (parentTransform ?? Matrix4x4.Identity);
                        GatherEntitiesFromLump(childEntityLump, childLumpTransform);
                    }
                    else
                    {
                        ProgressReporter?.Report($"Failed to find child entity lump with name {entityLumpName}.");
                    }
                }
            }

            var rawModelName = compiledEntity.GetStringProperty("model");
            string? modelName = null;
            if (!string.IsNullOrEmpty(rawModelName))
            {
                modelName = NormalizePath(rawModelName);
            }

            if (modelName != null && PathIsSubPath(modelName, LumpFolder))
            {
                var firstReference = ModelEntityAssociations.TryAdd(modelName, className);
                if (!firstReference)
                {
                    var otherClass = ModelEntityAssociations[modelName];
                    Debug.Assert(className == otherClass, "Model living in lump folder referenced by more than one entity type!\n" +
                        $"model = {modelName} {className} != {otherClass}");
                }

                ExtractEntityModel(mapEntity, modelName, worldTransform.Translation);

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

            var rawSnapshotFile = compiledEntity.GetStringProperty("snapshot_file");
            string? snapshotFile = null;
            if (!string.IsNullOrEmpty(rawSnapshotFile))
            {
                snapshotFile = NormalizePath(rawSnapshotFile);
            }
            if (snapshotFile != null && PathIsSubPath(snapshotFile, LumpFolder))
            {
                SnapshotsToExtract.Add(snapshotFile);

                // snapshot_mesh needs to be set to 0 in order for it to use the vsnap file
                mapEntity.WithProperty("snapshot_mesh", "0");
            }

            MapDocument.World.Children.Add(mapEntity);
        }
    }

    private readonly HashSet<string> TemplateChildEntities = [];

    /// <summary>
    /// The compiler clones an entity used by several point_templates into each template's child lump,
    /// but every clone keeps the original hammeruniqueid, so we can fold them back into one entity.
    /// </summary>
    private bool TryDeduplicateTemplateChild(Entity compiledEntity)
    {
        var hammerUniqueId = compiledEntity.GetStringProperty("hammeruniqueid");
        if (string.IsNullOrEmpty(hammerUniqueId))
        {
            return false;
        }

        return !TemplateChildEntities.Add(hammerUniqueId);
    }

    private void ExtractEntityModel(CMapEntity mapEntity, string modelName, Vector3 offset)
    {
        using var model = FileLoader.LoadFileCompiled(modelName);
        if (model is null || model.DataBlock is null)
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
                foreach (var hammermesh in RenderMeshToHammerMesh(data, model, associatedEntityClass, Matrix4x4.CreateTranslation(offset)))
                {
                    mapEntity.Children.Add(hammermesh);
                }
            }

            return;
        }

        var toolTexture = GetToolTextureForEntity(associatedEntityClass);
        Debug.Assert(toolTexture is not null);
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
        foreach (var (key, value) in compiledEntity.Children)
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
                    OutputName = connection.OutputName,
                    TargetType = (int)connection.TargetType,
                    TargetName = RemoveTargetnamePrefix(connection.TargetName),
                    InputName = connection.InputName,
                    OverrideParam = connection.OverrideParam,
                    Delay = connection.Delay,
                    TimesToFire = connection.TimesToFire,
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
                var hammerUniqueIdString = compiledEntity.TryGetValue(key, out var hammerValue) ? ToEditString(hammerValue) : null;
                if (!string.IsNullOrEmpty(hammerUniqueIdString))
                {
                    lineage = Array.ConvertAll(hammerUniqueIdString.Split(':'), int.Parse);
                }
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
            // in newer s2 engine branches having `array_index` present causes all sort of issues and crashes
            if (propertyKey is "cubemaptexture" or "lightprobetexture" or "array_index")
            {
                propertyKey = prefix + propertyKey;
            }
        }

        return false;
    }

    static string StringBool(bool value)
        => value ? "1" : "0";

    private static string? ToEditString(object? data)
    {
        if (data is null)
        {
            return default;
        }

        if (data is KVObject kvObject)
        {
            if (kvObject.IsArray)
            {
                return string.Join(' ', kvObject.Select(p => p.Value.ToString() ?? string.Empty));
            }

            if (kvObject.ValueType is not KVValueType.Collection)
            {
                return kvObject.ValueType switch
                {
                    KVValueType.String => (string)kvObject,
                    KVValueType.Boolean => StringBool((bool)kvObject),
                    KVValueType.Null => string.Empty,
                    _ => kvObject.ToString(),
                };
            }

            using var ms = new MemoryStream();
            KVSerializer.Create(KVSerializationFormat.KeyValues1Text).Serialize(ms, new KVDocument(null, null, kvObject));
            return System.Text.Encoding.UTF8.GetString(ms.ToArray());
        }

        return data switch
        {
            string str => str,
            bool boolean => StringBool(boolean),
            Vector3 vector => string.Create(CultureInfo.InvariantCulture, $"{vector.X} {vector.Y} {vector.Z}"),
            Vector2 vector => string.Create(CultureInfo.InvariantCulture, $"{vector.X} {vector.Y}"),
            null => string.Empty,
            _ when data.GetType().IsPrimitive => Convert.ToString(data, CultureInfo.InvariantCulture),
            _ => throw new NotImplementedException()
        };
    }

    #endregion Entities
}

/// <summary>
/// Extension methods for ElementArray.
/// </summary>
public static class ElementArrayExtensions
{
    /// <summary>
    /// Adds an element to the array and returns the element.
    /// </summary>
    public static T AddReturn<T>(this Datamodel.ElementArray array, T element) where T : Datamodel.Element
    {
        array.Add(element);
        return element;
    }
}
