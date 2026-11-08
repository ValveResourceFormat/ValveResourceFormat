using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using ValveKeyValue;
using ValveResourceFormat.Blocks;
using ValveResourceFormat.IO.ContentFormats.DmxModel;
using ValveResourceFormat.IO.ContentFormats.ValveMap;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.Serialization.KeyValues;
using static ValveResourceFormat.ResourceTypes.EntityLump;

namespace ValveResourceFormat.IO;

/// <summary>
/// Extracts map data from Source 2 resources into editable formats.
/// </summary>
public sealed partial class MapExtract
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
    private HashSet<string> MaterialsToExtract { get; } = [];
    private HashSet<string> ProvidedResources { get; } = [];

    // render mesh vertices closer than this are the same Hammer vertex
    private const float HammerMeshWeldDistance = 1f / 64f;

    private readonly Dictionary<string, Vector2?> MaterialTextureSizes = [];

    // units the generated physics surface materials span, declared in their vmat so Hammer projects them the same:
    // their 64 texel texture at the density of the tool textures (64 texels over 16 units)
    private const int AutoPhysicsMaterialWorldMapping = 16;

    /// <summary>
    /// The texture size Hammer projects a material with, for faces that have no texture coordinates, in texels at
    /// <see cref="TextureScale"/>: the units its WorldMappingWidth / WorldMappingHeight attributes
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
            return new Vector2(AutoPhysicsMaterialWorldMapping) / TextureScale;
        }

        using var materialResource = FileLoader.LoadFileCompiled(materialName);
        if (materialResource?.DataBlock is not Material material)
        {
            return null;
        }

        int GetIntAttribute(string name) => (int)material.IntAttributes.GetValueOrDefault(name);

        var worldMappingWidth = GetIntAttribute("WorldMappingWidth");
        var worldMappingHeight = GetIntAttribute("WorldMappingHeight");

        if (worldMappingWidth > 0 && worldMappingHeight > 0)
        {
            return new Vector2(worldMappingWidth, worldMappingHeight) / TextureScale;
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
    /// What one Hammer mesh builder collects: the draw calls of one material with one tint and shadow mode. Geometry
    /// only welds within a material, and a Hammer mesh has a single tint and shadow mode.
    /// </summary>
    private readonly record struct HammerMeshGroup(string Material, Vector4 Tint, int DisableShadows);

    /// <summary>
    /// Gets the Hammer <c>disableShadows</c> mode of geometry compiled with <paramref name="flags"/>. No shadows and
    /// only baked shadows compile to the same flags, which read as only baked.
    /// </summary>
    private static int DisableShadowsFromFlags(ObjectTypeFlags flags)
        => flags.HasFlag(ObjectTypeFlags.NoShadows) ? 3 : flags.HasFlag(ObjectTypeFlags.NeedsDynamicShadows) ? 2 : 0;

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

    // Units one texel spans on a face Hammer projects a texture onto, the game's Hammer default
    private readonly float TextureScale;

    /// <summary>Gets or sets the progress reporter.</summary>
    public IProgress<string>? ProgressReporter { get; set; }

    /// <summary>Gets or sets whether the models extracted with the map reconstruct their soft-body (cloth) physics.</summary>
    public bool ReconstructSoftbody { get; set; } = true;

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
    /// Vertex streams a material reads as foliage animation. A Hammer mesh has no stream for them.
    /// </summary>
    private static readonly string[] FoliageAnimationStreams = ["PivotPaint", "FoliageAnimation"];

    /// <summary>
    /// The draw calls of every mesh embedded in a model, each paired with the mesh it belongs to.
    /// </summary>
    private static IEnumerable<(Mesh Mesh, KVObject DrawCall)> EnumerateDrawCalls(Model model)
    {
        foreach (var embedded in model.GetEmbeddedMeshes())
        {
            foreach (var meshSceneObject in embedded.Mesh.Data.GetArray("m_sceneObjects"))
            {
                foreach (var drawCall in meshSceneObject.GetArray("m_drawCalls"))
                {
                    yield return (embedded.Mesh, drawCall);
                }
            }
        }
    }

    /// <summary>
    /// Whether a draw call of the model carries a vertex stream its material reads as foliage animation.
    /// </summary>
    private bool HasFoliageAnimationStreams(Model model)
    {
        var inputSignatures = new Dictionary<string, Material.VsInputSignature>();

        foreach (var (mesh, drawCall) in EnumerateDrawCalls(model))
        {
            var materialName = Mesh.GetMaterialName(drawCall);

            if (materialName is null)
            {
                continue;
            }

            if (!inputSignatures.TryGetValue(materialName, out var inputSignature))
            {
                inputSignature = Material.LoadInputSignature(FileLoader, materialName);
                inputSignatures.Add(materialName, inputSignature);
            }

            foreach (var vertexBuffer in drawCall.GetArray("m_vertexBuffers"))
            {
                foreach (var attribute in mesh.VBIB.VertexBuffers[vertexBuffer.GetInt32Property("m_hBuffer")].InputLayoutFields)
                {
                    var element = Material.FindD3DInputSignatureElement(inputSignature, attribute.SemanticName, attribute.SemanticIndex);

                    if (FoliageAnimationStreams.Contains(element.Semantic))
                    {
                        return true;
                    }
                }
            }
        }

        return false;
    }

    /// <summary>
    /// Builds the Hammer vertex paint of one placed model from the vertex streams its world node binds to it,
    /// or <see langword="null"/> when none of them can be carried over.
    /// </summary>
    private CDmExtraVertexData? GetExtraVertexData(string modelName, WorldNode.ExtraVertexStreamOverride[] streamOverrides, VBIB streams)
    {
        if (streamOverrides.Length == 0)
        {
            return null;
        }

        using var modelResource = FileLoader.LoadFile(modelName + GameFileLoader.CompiledFileSuffix);

        if (modelResource?.DataBlock is not Model model)
        {
            return null;
        }

        var extraVertexData = new CDmExtraVertexData();

        foreach (var streamOverride in streamOverrides)
        {
            if (streamOverride.DrawCallIndex > int.MaxValue || streamOverride.IsVertexLighting)
            {
                continue;
            }

            var stream = streams.VertexBuffers[streamOverride.BufferIndex];
            var drawCall = FindEmbeddedDrawCall(model, streamOverride);

            // Streams painted on an older version of the model no longer match its vertex count
            if (drawCall is null || drawCall.GetInt32Property("m_nVertexCount") != stream.ElementCount
                || Mesh.GetMaterialName(drawCall) is not { } materialName)
            {
                continue;
            }

            if (!ExtraVertexStreamInputSignatures.TryGetValue(materialName, out var inputSignature))
            {
                inputSignature = Material.LoadInputSignature(FileLoader, materialName);
                ExtraVertexStreamInputSignatures.Add(materialName, inputSignature);
            }

            var extraStream = new CDmExtraVertexStream
            {
                MeshIndex = (int)streamOverride.SubSceneObject,
                DrawCallIndex = (int)streamOverride.DrawCallIndex,
            };

            var indices = Enumerable.Range(0, (int)stream.ElementCount).ToArray();

            foreach (var attribute in stream.InputLayoutFields)
            {
                // The material names what it reads from the slot the stream fills
                var streamName = Material.FindD3DInputSignatureElement(inputSignature, attribute.SemanticName, attribute.SemanticIndex).Semantic;

                if (string.IsNullOrEmpty(streamName))
                {
                    continue;
                }

                extraStream.VertexData.AddIndexedStream(streamName, VBIB.GetVector4AttributeArray(stream, attribute), indices);
            }

            if (extraStream.VertexData.VertexFormat.Count > 0)
            {
                extraVertexData.ExtraStreams.Add(extraStream);
            }
        }

        return extraVertexData.ExtraStreams.Count > 0 ? extraVertexData : null;
    }

    private static KVObject? FindEmbeddedDrawCall(Model model, WorldNode.ExtraVertexStreamOverride streamOverride)
    {
        var remaining = (int)streamOverride.DrawCallIndex;

        foreach (var embeddedMesh in model.GetEmbeddedMeshes())
        {
            if (embeddedMesh.MeshIndex != streamOverride.SubSceneObject)
            {
                continue;
            }

            foreach (var meshSceneObject in embeddedMesh.Mesh.Data.GetArray("m_sceneObjects"))
            {
                foreach (var drawCall in meshSceneObject.GetArray("m_drawCalls"))
                {
                    if (remaining == 0)
                    {
                        return drawCall;
                    }

                    remaining--;
                }
            }
        }

        return null;
    }

    private readonly Dictionary<string, Material.VsInputSignature> ExtraVertexStreamInputSignatures = [];

    /// <summary>
    /// The material that replaces every draw call of one placed model, which is all a prop can express in Hammer,
    /// or <see langword="null"/> when its overrides differ or leave some draw calls alone.
    /// </summary>
    private string? GetWholeModelMaterialOverride(string modelName, WorldNode.MaterialOverride[] materialOverrides)
    {
        foreach (var materialOverride in materialOverrides)
        {
            if (materialOverride.IsWholeModel && !string.IsNullOrEmpty(materialOverride.Material))
            {
                return materialOverride.Material;
            }
        }

        if (materialOverrides.Length == 0 || materialOverrides.Any(materialOverride => materialOverride.Material != materialOverrides[0].Material))
        {
            return null;
        }

        using var modelResource = FileLoader.LoadFile(modelName + GameFileLoader.CompiledFileSuffix);

        if (modelResource?.DataBlock is not Model model)
        {
            return null;
        }

        var overriddenDrawCalls = materialOverrides
            .Select(static materialOverride => (materialOverride.SubSceneObject, materialOverride.DrawCallIndex))
            .Distinct()
            .Count();

        if (overriddenDrawCalls != EnumerateDrawCalls(model).Count())
        {
            // smart prop?
            return null;
        }

        return materialOverrides[0].Material;
    }

    /// <summary>
    /// Extract a map from a resource. Accepted types include <see cref="ResourceType.Map"/>, <see cref="ResourceType.World"/>. TODO: <see cref="ResourceType.WorldNode"/> and <see cref="ResourceType.EntityLump"/>.
    /// </summary>
    public MapExtract(Resource resource, IFileLoader? fileLoader)
    {
        FileLoader = fileLoader ?? throw new ArgumentNullException(nameof(fileLoader), "A file loader must be provided to load the map's lumps");
        TextureScale = FileLoader.GameInfo.HammerTextureScale;
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
        using var worldResource = FileLoader.LoadFileCompiled(worldPath) ??
            throw new FileNotFoundException($"Failed to find world resource, which is required for vmap_c extract, at {worldPath}");
        InitWorldExtract(worldResource);
    }

    /// <summary>
    /// Files the map compiler writes next to the map. The map holds their content, or Hammer builds them again,
    /// so a folder export skips them. An entry ending in a slash stands for the whole folder.
    /// </summary>
    private void AddCompilerOwnedFilter()
    {
        FolderExtractFilter.Add($"{LumpFolder}/world.vwrld");
        FolderExtractFilter.Add($"{LumpFolder}/world.vrman");
        FolderExtractFilter.Add($"{LumpFolder}/world_visibility.vvis");
        FolderExtractFilter.Add($"{LumpFolder}/world_physics.vrman");
        FolderExtractFilter.Add($"{LumpFolder}/entities/");
        FolderExtractFilter.Add($"{LumpFolder}/lightmaps/");
        FolderExtractFilter.Add($"{LumpFolder}/cubemaps/");

        if (WorldPhysicsName != null)
        {
            FolderExtractFilter.Add(WorldPhysicsName);
        }
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

        AddCompilerOwnedFilter();
    }

    private string? GetWorldPhysicsName()
    {
        var manifestFileName = Path.Combine(LumpFolder, "world_physics.vrman_c");
        using var manifestResource = FileLoader.LoadFile(manifestFileName);

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
    /// Converts the map extract to a content file with all dependencies.
    /// </summary>
    public ContentFile ToContentFile()
    {
        var vmap = new ContentFile
        {
            Data = ToValveMap(),
            FileName = GetMapOutputName(),
        };

        AddAdditionalMapSubFiles(vmap);
        AddProvidedEntityModels();
        AddSceneObjectFiles(vmap);
        // Export all gathered vsnap files
        AddSnapshotFiles(vmap);
        AddProvidedMaterialFiles(vmap);
        AddProvidedTextureFiles(vmap);
        AddProvidedAssetFiles(vmap);

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

    private IResourceProvider? ResourceProvider => (FileLoader as GameFileLoader)?.ResourceProvider;

    private void AddProvidedEntityModels()
    {
        foreach (var modelName in ProvidedResources)
        {
            if (!modelName.EndsWith(".vmdl", StringComparison.Ordinal) || SceneObjectsToExtract.Contains(modelName))
            {
                continue;
            }

            SceneObjectsToExtract.Add(modelName);
        }
    }

    private void AddAdditionalMapSubFiles(ContentFile vmap)
    {
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
    }

    private void AddSceneObjectFiles(ContentFile vmap)
    {
        foreach (var sceneObjectResourceName in SceneObjectsToExtract)
        {
            var sceneObjectNameCompiled = sceneObjectResourceName + GameFileLoader.CompiledFileSuffix;
            using var loadedSceneObject = FileLoader.LoadFile(sceneObjectNameCompiled);

            if (loadedSceneObject == null)
            {
                continue;
            }

            var sceneObject = ResourceProvider?.GetExportResource(loadedSceneObject) ?? loadedSceneObject;

            if (sceneObject.DataBlock == null)
            {
                continue;
            }

            AddMaterialReferences(sceneObject);

            var sceneObjectExtract = sceneObject.ResourceType switch
            {
                ResourceType.Model => new ModelExtract(sceneObject, FileLoader) { ReconstructSoftbody = ReconstructSoftbody },
                ResourceType.Mesh => new ModelExtract((Mesh)sceneObject.DataBlock, sceneObjectResourceName),
                _ => throw new InvalidDataException($"Unhandled resource type: {sceneObject.ResourceType} as a scene object"),
            };

            var vmdl = sceneObjectExtract.ToContentFile();
            vmap.AdditionalFiles.Add(vmdl);
            FolderExtractFilter.Add(sceneObjectNameCompiled);
        }
    }

    private void AddSnapshotFiles(ContentFile vmap)
    {
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
    }

    private void AddMaterialReferences(Resource? resource)
    {
        if (resource?.ExternalReferences is not { } references)
        {
            return;
        }

        foreach (var reference in references.ResourceRefInfoList)
        {
            if (reference.Name.EndsWith(".vmat", StringComparison.Ordinal))
            {
                MaterialsToExtract.Add(reference.Name);
            }
        }
    }

    // Tools materials of brushes are not drawn by the game, so only the faces of the map name them.
    private void AddMeshMaterials(CDmePolygonMesh mesh)
    {
        foreach (var material in mesh.Materials)
        {
            MaterialsToExtract.Add(material);
        }
    }

    private void AddProvidedTextureFiles(ContentFile vmap)
    {
        foreach (var textureName in ProvidedResources)
        {
            if (!textureName.EndsWith(".vtex", StringComparison.Ordinal))
            {
                continue;
            }

            using var textureResource = FileLoader.LoadFileCompiled(textureName);
            if (textureResource?.DataBlock is not Texture)
            {
                continue;
            }

            var vtex = new TextureExtract(textureResource).ToContentFile();
            vtex.FileName = textureName;
            vmap.AdditionalFiles.Add(vtex);
        }
    }

    private void AddProvidedAssetFiles(ContentFile vmap)
    {
        foreach (var assetName in ProvidedResources)
        {
            if (!assetName.EndsWith(".vpost", StringComparison.Ordinal) && !assetName.EndsWith(".vmap", StringComparison.Ordinal))
            {
                continue;
            }

            using var assetResource = FileLoader.LoadFileCompiled(assetName);
            if (assetResource == null)
            {
                continue;
            }

            var asset = FileExtract.Extract(assetResource, FileLoader);
            asset.FileName = assetName;
            vmap.AdditionalFiles.Add(asset);
        }
    }

    // A material that was not read from a compiled file, such as a converted one, is not in the game files, so the map carries it.
    private void AddProvidedMaterialFiles(ContentFile vmap)
    {
        foreach (var materialName in MaterialsToExtract)
        {
            using var materialResource = FileLoader.LoadFileCompiled(materialName);
            if (materialResource?.DataBlock is null || materialResource.CanReload)
            {
                continue;
            }

            var vmat = new MaterialExtract(materialResource, FileLoader).ToContentFile();
            vmat.FileName = materialName;
            vmap.AdditionalFiles.Add(vmat);
        }
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

        // Small scene maps can have world physics without any parts
        var phys = LoadWorldPhysics();
        if (phys is { Parts.Length: > 0 })
        {
            var collisionAttributes = phys.CollisionAttributes;
            var worldPhysMeshes = phys.Parts[0].Shape.GetAllMeshes().Where(m => collisionAttributes[m.CollisionAttributeIndex].GetStringProperty("m_CollisionGroupString") == "Default");

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
                selectionSet.ObjectSelection.SelectedObjects.Add(hammerMesh);
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
                AddMaterialReferences(entityLumpResource);
                if (ResourceProvider is { } provider)
                {
                    ProvidedResources.UnionWith(provider.GetExportResources(entityLumpResource));
                }

                GatherEntitiesFromLump((EntityLump)entityLumpResource.DataBlock);
            }
        }

        if (phys != null)
        {
            foreach (var hammermesh in PhysToHammerMeshes(phys, Matrix4x4.Identity))
            {
                MapDocument.World.Children.Add(hammermesh);
            }
        }

        using var stream = new MemoryStream();

        // datamodel.Save(stream, "keyvalues2", 4)
        datamodel.Save(stream, "binary", 9);

        return stream.ToArray();
    }

    private List<CMapRootElement> SplitLargeMapDocument()
    {
        const long OneGiB = 1024 * 1024 * 1024;
        var accumulatedMapMeshSize = 0L;

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

            var removed = selectionSet.ObjectSelection.SelectedObjects.Remove(node);

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
            foreach (var dataStream in streams.OfType<CDmePolygonMeshDataStream>())
            {
                accumulatedMapMeshSize += dataStream.Data switch
                {
                    Datamodel.Array<int> array => GetArraySize(array),
                    Datamodel.Array<float> array => GetArraySize(array),
                    Datamodel.Array<Vector2> array => GetArraySize(array),
                    Datamodel.Array<Vector3> array => GetArraySize(array),
                    Datamodel.Array<Vector4> array => GetArraySize(array),
                    _ => 0,
                };
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
    /// <param name="drawCallShadows">Hammer <c>disableShadows</c> mode per draw call index, defaults to 0.</param>
    /// <param name="builders">Builders to add to, created per material, tint and shadow mode as needed.</param>
    /// <returns>Number of draw calls added.</returns>
    private int AddRenderMeshToBuilders(Model model, Resource resource, Matrix4x4 transform, Func<int, Vector4>? drawCallTint, Func<int, int>? drawCallShadows, Dictionary<HammerMeshGroup, HammerMeshBuilder> builders)
    {
        var modelExtract = new ModelExtract(resource, FileLoader);
        modelExtract.GrabMaterialInputSignatures(resource);
        AddMaterialReferences(resource);

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
                var group = new HammerMeshGroup(material, tint, drawCallShadows?.Invoke(drawCallIndex) ?? 0);

                var builder = GetOrCreateHammerMeshBuilder(builders, group);

                if (dag.Shape is DmeMesh meshShape)
                {
                    builder.AddRenderMesh(meshShape, transform);
                }
            }
        }

        return drawCallCount;
    }

    private HammerMeshBuilder GetOrCreateHammerMeshBuilder(Dictionary<HammerMeshGroup, HammerMeshBuilder> builders, HammerMeshGroup group)
    {
        if (!builders.TryGetValue(group, out var builder))
        {
            builder = new HammerMeshBuilder()
            {
                PhysicsTriangleMatcher = PhysTriangleMatcher,
                ProgressReporter = ProgressReporter,
                Untriangulate = true,
                TextureSizeProvider = GetMaterialTextureSize,
                DefaultTextureScale = TextureScale,
            };
            builders.Add(group, builder);
        }

        return builder;
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
                    DisableShadows = group.DisableShadows,
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
        var drawCallCount = AddRenderMeshToBuilders(model, resource, transform ?? Matrix4x4.Identity, null, null, builders);
        var hammerMeshesToReturn = GenerateHammerMeshes(builders);

        var hammerMeshEntitySelectionSet = new CMapSelectionSet();
        var drawSelectionSet = new CMapSelectionSet();

        foreach (var hammerMesh in hammerMeshesToReturn)
        {
            if (!string.IsNullOrEmpty(entityClassname))
            {
                hammerMeshEntitySelectionSet.ObjectSelection.SelectedObjects.Add(hammerMesh);
            }
            else
            {
                drawSelectionSet.ObjectSelection.SelectedObjects.Add(hammerMesh);
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

    /// <summary>
    /// Tint of a draw call in gamma space, 0-255, with its alpha in W. The compiler stores it as (tint / 255) to the
    /// power of 2.2.
    /// </summary>
    private static Vector4 GetDrawCallTint(KVObject drawCall)
    {
        var tint = Vector3.One * 255f;

        if (drawCall.ContainsKey("m_vTintColor"))
        {
            tint *= ColorSpace.LinearToGamma22(drawCall.GetSubCollection("m_vTintColor").ToVector3());
        }

        var alpha = 255f * drawCall.GetFloatProperty("m_flAlpha", 1f);

        return new Vector4(tint, alpha);
    }

    /// <summary>
    /// The tint every draw call of a model shares, or <see langword="null"/> when they differ.
    /// </summary>
    private static Vector4? GetUniformDrawCallTint(Model model)
    {
        Vector4? uniformTint = null;

        foreach (var (_, drawCall) in EnumerateDrawCalls(model))
        {
            var tint = GetDrawCallTint(drawCall);

            if (uniformTint is { } seenTint && seenTint != tint)
            {
                return null;
            }

            uniformTint = tint;
        }

        return uniformTint;
    }

    /// <summary>
    /// Tint of an aggregate fragment in gamma space, 0-255, with its alpha in W. The compiler stores the authored
    /// color as is, and appends the render alpha to it when the fragment is not opaque.
    /// </summary>
    private static Vector4 GetFragmentTint(KVObject fragment)
    {
        if (!fragment.ContainsKey("m_vTintColor"))
        {
            return new Vector4(255f);
        }

        KVObject tint = fragment.GetSubCollection("m_vTintColor");

        return tint.Count > 3 ? tint.ToVector4() : new Vector4(tint.ToVector3(), 255f);
    }

    internal List<CMapMesh> PhysToHammerMeshes(PhysAggregateData phys, Matrix4x4 transform, string? entityClassname = null)
    {
        var cMapMeshesToReturn = new List<CMapMesh>();

        var materialOverride = string.IsNullOrEmpty(entityClassname)
            ? null
            : ToolsMaterials.GetForEntity(entityClassname, FileLoader);

        for (var i = 0; i < phys.Parts.Length; i++)
        {
            var shape = phys.Parts[i].Shape;
            var hulls = shape.GetAllHulls().ToArray();
            var meshes = shape.GetAllMeshes().ToArray();

            var hullsSelectionSet = new CMapSelectionSet
            {
                SelectionSetName = "physics shape (" + hulls.Length + " hulls)"
            };

            var hullsEntitySelectionSet = new CMapSelectionSet
            {
                SelectionSetName = "physics hull entity " + entityClassname + " (reconstructed from " + hulls.Length + (hulls.Length > 1 ? " hulls)" : " hull)")
            };

            var meshesSelectionSet = new CMapSelectionSet
            {
                SelectionSetName = "physics shape (" + meshes.Length + " original meshes)"
            };

            var meshesEntitySelectionSet = new CMapSelectionSet
            {
                SelectionSetName = "physics mesh entity " + entityClassname + " (reconstructed from " + meshes.Length + (meshes.Length > 1 ? " meshes)" : " mesh)")
            };

            foreach (var hull in hulls)
            {
                var hammerMeshBuilder = new HammerMeshBuilder { Untriangulate = true, TextureSizeProvider = GetMaterialTextureSize, DefaultTextureScale = TextureScale };
                hammerMeshBuilder.AddPhysHull(hull, phys, GetAndExportAutoPhysicsMaterialName, transform, materialOverride);
                var meshData = hammerMeshBuilder.GenerateMesh();

                if (meshData.FaceEdgeIndices.Count == 0)
                {
                    continue;
                }

                AddMeshMaterials(meshData);

                var hammerMesh = new CMapMesh() { MeshData = meshData };

                if (string.IsNullOrEmpty(entityClassname))
                {
                    hullsSelectionSet.ObjectSelection.SelectedObjects.Add(hammerMesh);
                }
                else
                {
                    hullsEntitySelectionSet.ObjectSelection.SelectedObjects.Add(hammerMesh);
                }

                cMapMeshesToReturn.Add(hammerMesh);
            }

            // physics meshes are welded together and split by connectivity like the render geometry is, however
            // material doesnt matter and neither does tint
            var physicsMeshBuilder = new HammerMeshBuilder { Untriangulate = true, TextureSizeProvider = GetMaterialTextureSize, DefaultTextureScale = TextureScale };

            foreach (var mesh in meshes)
            {
                var deletedTriangles = PhysTriangleMatcher?.GetDeletedTriangles(mesh);
                physicsMeshBuilder.AddPhysMesh(mesh, phys, GetAndExportAutoPhysicsMaterialName, deletedTriangles, transform, materialOverride);
            }

            if (meshes.Length > 0)
            {
                foreach (var meshData in physicsMeshBuilder.GenerateMeshes(HammerMeshWeldDistance))
                {
                    if (meshData.FaceEdgeIndices.Count == 0)
                    {
                        continue;
                    }

                    AddMeshMaterials(meshData);

                    var hammerMesh = new CMapMesh() { MeshData = meshData };

                    var selectionSet = string.IsNullOrEmpty(entityClassname) ? meshesSelectionSet : meshesEntitySelectionSet;
                    selectionSet.ObjectSelection.SelectedObjects.Add(hammerMesh);

                    cMapMeshesToReturn.Add(hammerMesh);
                }
            }

            if (hulls.Length != 0)
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

            if (meshes.Length != 0)
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
        var rounded = Vector4.Clamp(Vector4.Round(tint), Vector4.Zero, new Vector4(255f));
        Span<byte> color32 = stackalloc byte[] { (byte)rounded.X, (byte)rounded.Y, (byte)rounded.Z, (byte)rounded.W };
        return Datamodel.Color.FromBytes(color32);
    }

    private void HandleWorldNode(WorldNode node)
    {
        var layerNodes = CreateWorldLayerNodes(node.LayerNames);

        var extraVertexStreamOverrides = node.ExtraVertexStreamOverrides.ToLookup(static streamOverride => streamOverride.SceneObjectIndex);
        var extraVertexStreams = node.GetExtraVertexStreams();
        var materialOverrides = node.MaterialOverrides.ToLookup(static materialOverride => materialOverride.SceneObjectIndex);
        var overrides = new SceneObjectOverrides(extraVertexStreamOverrides, extraVertexStreams, materialOverrides);

        var sceneObjects = node.SceneObjects;
        var sceneObjectLayerIndices = node.SceneObjectLayerIndices;
        for (var i = 0; i < sceneObjects.Count; i++)
        {
            var sceneObject = sceneObjects[i];
            var layerIndex = (int)(sceneObjectLayerIndices?[i] ?? -1);
            ProcessSceneObject(sceneObject, i, layerIndex, layerNodes, overrides);
        }

        foreach (var aggregateSceneObject in node.AggregateSceneObjects)
        {
            var layerIndex = aggregateSceneObject.GetInt32Property("m_nLayer");
            ProcessAggregate(aggregateSceneObject, layerIndex, layerNodes);
        }

        foreach (var clutterSceneObject in node.ClutterSceneObjects)
        {
            // TODO: Clutter
        }
    }

    private readonly record struct SceneObjectOverrides(
        ILookup<uint, WorldNode.ExtraVertexStreamOverride> ExtraVertexStreamOverrides,
        VBIB ExtraVertexStreams,
        ILookup<uint, WorldNode.MaterialOverride> MaterialOverrides);

    private List<MapNode> CreateWorldLayerNodes(IReadOnlyList<string> layerNames)
    {
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

        return layerNodes;
    }

    private MapNode GetWorldLayerNode(int layerIndex, List<MapNode> layerNodes)
    {
        if (layerIndex > -1)
        {
            return layerNodes[layerIndex];
        }

        return MapDocument.World;
    }

    private void AddChildMaybeGrouped(MapNode node, MapNode child, string? selectionSetName)
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

                selectionSet.ObjectSelection.SelectedObjects.Add(child);
            }
        }
    }

    private void StaticPropFinalize(MapNode node, int layerIndex, List<MapNode> layerNodes, bool isBakedToWorld)
    {
        var destNode = GetWorldLayerNode(layerIndex, layerNodes);

        // Only use this group in the base world layer
        var bakedGroup = isBakedToWorld && destNode == MapDocument.World
            ? "Baked World Models"
            : null;

        AddChildMaybeGrouped(destNode, node, bakedGroup);
    }

    private static void SetTintAlpha(BaseEntity entity, Vector4 tint)
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

    private static void SetPropertiesFromFlags(BaseEntity prop, ObjectTypeFlags objectFlags)
    {
        var properties = prop.EntityProperties;
        properties["renderwithdynamic"] = StringBool(objectFlags.HasFlag(ObjectTypeFlags.RenderWithDynamic));
        properties["rendertocubemaps"] = StringBool(objectFlags.HasFlag(ObjectTypeFlags.RenderToCubemaps));
        properties["disableinlowquality"] = StringBool(objectFlags.HasFlag(ObjectTypeFlags.DisabledInLowQuality));
    }

    private void ProcessSceneObject(KVObject sceneObject, int sceneObjectIndex, int layerIndex, List<MapNode> layerNodes, SceneObjectOverrides overrides)
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
        Vector4? drawCallTint = null;

        if (SceneObjectShouldConvertToHammerMesh(modelName))
        {
            var isConverted = TryConvertSceneObjectToHammerMesh(modelName!, objectTransform, out drawCallTint);
            if (isConverted)
            {
                return;
            }
        }

        SceneObjectsToExtract.Add(modelName!);
        AssetReferences.Add(modelName!);

        var propStatic = CreateStaticPropFromSceneObject(sceneObject, sceneObjectIndex, modelName!, objectFlags, objectTransform, drawCallTint, overrides);
        var isEmbeddedModel = !objectFlags.HasFlag(ObjectTypeFlags.Model);

        StaticPropFinalize(propStatic, layerIndex, layerNodes, isEmbeddedModel);
    }

    /// <summary>
    /// Converts a scene object to hammer meshes. Returns false when the object is a prop that stays a model, with the
    /// tint the compiler baked into the draw calls of that model.
    /// </summary>
    private bool TryConvertSceneObjectToHammerMesh(string modelName, Matrix4x4 objectTransform, out Vector4? drawCallTint)
    {
        drawCallTint = null;

        var meshNameCompiled = modelName + GameFileLoader.CompiledFileSuffix;
        using var mesh = FileLoader.LoadFile(meshNameCompiled);

        if (mesh is null || mesh.DataBlock is null)
        {
            return true;
        }

        var model = (Model)mesh.DataBlock;

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

        if (!HasFoliageAnimationStreams(model))
        {
            foreach (var hammermesh in RenderMeshToHammerMesh(model, mesh, transform: scaleTransform))
            {
                hammermesh.Origin = meshOrigin;
                hammermesh.Angles = meshAngles;
                MapDocument.World.Children.Add(hammermesh);
            }

            return true;
        }

        // the compiler bakes the tint of the prop into the draw calls of its model
        drawCallTint = GetUniformDrawCallTint(model);

        return false;
    }

    private BaseEntity CreateStaticPropFromSceneObject(KVObject sceneObject, int sceneObjectIndex, string modelName, ObjectTypeFlags objectFlags, Matrix4x4 objectTransform, Vector4? drawCallTint, SceneObjectOverrides overrides)
    {
        var propStatic = new CMapEntity()
            .WithClassName("prop_static")
            .WithProperty("model", modelName);

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
        else if (drawCallTint is { } bakedTint && bakedTint != new Vector4(255f))
        {
            SetTintAlpha(propStatic, bakedTint);
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

        if (!objectFlags.HasFlag(ObjectTypeFlags.Model))
        {
            propStatic.EntityProperties["baketoworld"] = StringBool(true);
            ModelsToExtract.Add(modelName);
        }

        if (Path.GetFileName(modelName).Contains("nomerge", StringComparison.Ordinal))
        {
            propStatic.EntityProperties["disablemerging"] = StringBool(true);
        }

        if (GetWholeModelMaterialOverride(modelName, [.. overrides.MaterialOverrides[(uint)sceneObjectIndex]]) is { } materialOverride)
        {
            propStatic.EntityProperties["materialoverride"] = materialOverride;
        }

        if (GetExtraVertexData(modelName, [.. overrides.ExtraVertexStreamOverrides[(uint)sceneObjectIndex]], overrides.ExtraVertexStreams) is { } extraVertexData)
        {
            propStatic["extra_vertex_data"] = extraVertexData;
        }

        return propStatic;
    }

    private void ProcessAggregate(KVObject agg, int layerIndex, List<MapNode> layerNodes)
    {
        var modelName = agg.GetStringProperty("m_renderableModel");
        var allFlags = agg.GetEnumValue<ObjectTypeFlags>("m_allFlags", normalize: true);

        var hasModelFlag = allFlags.HasFlag(ObjectTypeFlags.Model);
        var convertToHalfEdge = !hasModelFlag;

        var aggregateMeshes = agg.GetArray("m_aggregateMeshes");

        IReadOnlyList<KVObject> drawCalls = [];
        var drawCenters = Array.Empty<Vector3>();

        var fragmentTransforms = agg.GetArray("m_fragmentTransforms") ?? [];

        var aggregateHasTransforms = fragmentTransforms.Count > 0;

        FolderExtractFilter.Add(modelName);
        using var modelRes = FileLoader.LoadFileCompiled(modelName);

        if (modelRes is null || modelRes.DataBlock is null)
        {
            return;
        }

        AddMaterialReferences(modelRes);

        var model = (Model)modelRes.DataBlock;

        // TODO: reference meshes
        var mesh = ((Model)modelRes.DataBlock).GetEmbeddedMeshes().First();
        var sceneObject = mesh.Mesh.Data.GetArray("m_sceneObjects")[0];
        drawCalls = sceneObject.GetArray("m_drawCalls");

        if (convertToHalfEdge)
        {
            if (aggregateHasTransforms)
            {
                throw new InvalidOperationException("Unhandled aggregate with instanced transforms exported as hammer mesh!");
            }

            AddAggregateHammerMesh(model, modelRes, aggregateMeshes, drawCalls, allFlags);
            return;
        }

        if (!aggregateHasTransforms)
        {
            drawCenters = (sceneObject.GetArray("m_drawBounds") ?? [])
                .Select(aabb => (aabb.GetSubCollection("m_vMinBounds").ToVector3() + aabb.GetSubCollection("m_vMaxBounds").ToVector3()) / 2f)
                .ToArray();
        }

        var modelFiles = ModelExtract.GetContentFiles_DrawCallSplit(modelRes, FileLoader, drawCenters, drawCalls.Count);
        PreExportedFragments.AddRange(modelFiles);

        AddAggregateFragmentProps(modelName, aggregateMeshes, drawCalls, drawCenters, fragmentTransforms, layerIndex, layerNodes);
    }

    private void AddAggregateHammerMesh(Model model, Resource modelRes, IReadOnlyList<KVObject> aggregateMeshes, IReadOnlyList<KVObject> drawCalls, ObjectTypeFlags allFlags)
    {
        // the fragment tint multiplies the draw call tint, one fragment per draw call is expected here
        var fragmentTints = new Dictionary<int, Vector4>();
        var fragmentFlags = new Dictionary<int, ObjectTypeFlags>();
        foreach (var fragment in aggregateMeshes)
        {
            var drawCallIndex = fragment.GetInt32Property("m_nDrawCallIndex");
            if (fragment.ContainsKey("m_vTintColor"))
            {
                fragmentTints.TryAdd(drawCallIndex, GetFragmentTint(fragment));
            }

            if (fragment.ContainsKey("m_objectFlags"))
            {
                fragmentFlags.TryAdd(drawCallIndex, fragment.GetEnumValue<ObjectTypeFlags>("m_objectFlags", normalize: true));
            }
        }

        Vector4 HammerMeshTint(int drawCallIndex)
        {
            Vector4 tint = GetDrawCallTint(drawCalls[drawCallIndex]);

            if (fragmentTints.TryGetValue(drawCallIndex, out var fragmentTint))
            {
                tint = fragmentTint * tint / 255f;
            }

            return tint;
        }

        int HammerMeshShadows(int drawCallIndex)
            => DisableShadowsFromFlags(fragmentFlags.GetValueOrDefault(drawCallIndex, allFlags));

        // world geometry is welded across all aggregates, the meshes are made once every world node is in
        WorldHammerMeshDrawCalls += AddRenderMeshToBuilders(model, modelRes, Matrix4x4.Identity, HammerMeshTint, HammerMeshShadows, WorldHammerMeshBuilders);
    }

    private void AddAggregateFragmentProps(string modelName, IReadOnlyList<KVObject> aggregateMeshes, IReadOnlyList<KVObject> drawCalls, Vector3[] drawCenters, IReadOnlyList<KVObject> fragmentTransforms, int layerIndex, List<MapNode> layerNodes)
    {
        var aggregateHasTransforms = fragmentTransforms.Count > 0;
        var transformIndex = 0;

        var drawSelectionSet = new CMapSelectionSet
        {
            SelectionSetName = "prop_static render mesh " + (aggregateHasTransforms ? "(instanced) " : "(" + drawCalls.Count + " split draw meshes) ") + Path.GetFileNameWithoutExtension(modelName),
        };
        StaticPropsSelectionSet?.Children.Add(drawSelectionSet);

        foreach (var fragment in aggregateMeshes)
        {
            var i = fragment.GetInt32Property("m_nDrawCallIndex");
            var fragmentFlags = fragment.GetEnumValue<ObjectTypeFlags>("m_objectFlags", normalize: true);

            Vector4 fragmentTint = GetFragmentTint(fragment);
            Vector4 drawCallTint = GetDrawCallTint(drawCalls[i]) / 255f;

            Vector3 tint = fragmentTint.AsVector3() * drawCallTint.AsVector3();
            float alpha = fragmentTint.W * drawCallTint.W;

            var fragmentModelName = ModelExtract.GetFragmentModelName(modelName, i);
            AssetReferences.Add(fragmentModelName);

            var instance = NewAggregateFragmentProp(fragmentModelName);

            // Only the fragments that have a transform take one, the others are placed as authored
            if (aggregateHasTransforms && fragment.GetBooleanProperty("m_bHasTransform"))
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
                drawSelectionSet.ObjectSelection.SelectedObjects.Add(instance);
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
            drawSelectionSet.ObjectSelection.SelectedObjects.Add(instance);
        }
    }

    private static BaseEntity NewAggregateFragmentProp(string modelName) => new CMapEntity()
        .WithClassName("prop_static")
        .WithProperty("model", modelName)
        .WithProperty("baketoworld", StringBool(true))
        .WithProperty("disablemerging", StringBool(true))
        .WithProperty("visoccluder", StringBool(true));

    internal static string GetAutoPhysicsMaterialName(string rootFolder, string surfaceProperty)
        => NormalizePath(Path.Combine(rootFolder, "_vrf", "physics_surfaces", surfaceProperty.ToLowerInvariant() + ".vmat"))!;

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
        var entities = entityLump.GetEntities();
        if (ResourceProvider is { } provider)
        {
            entities = provider.GetExportEntities(entities);
        }

        var pathNodeEntities = GroupPathNodeEntities(entities);

        foreach (var compiledEntity in entities)
        {
            var className = compiledEntity.GetStringProperty("classname");

            if (className == null)
            {
                continue;
            }

            if (compiledEntity.GetStringProperty("path_uniqueid") is { } ownerPathId && pathNodeEntities.ContainsKey(ownerPathId))
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

            var pathNodes = compiledEntity.GetStringProperty("pathnodes") is { } pathNodesBlob ? PathParticleRope.ParseNodes(pathNodesBlob) : [];
            var mapEntity = CreateMapEntity(className, pathNodes.Count);

            var entityLineage = AddProperties(className, compiledEntity, mapEntity);
            var localTransform = EntityTransformHelper.ToTransformationMatrix(compiledEntity);
            var worldTransform = parentTransform is { } parent ? localTransform * parent : localTransform;
            if (parentTransform is not null)
            {
                // Under a rigid parent (rotation and translation only), decompose fails only if the child shears
                // (non-uniform scale with rotation). Then keep the entity's own placement, not the identity rotation.
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

            if (mapEntity is CMapPath mapPath)
            {
                Dictionary<int, Entity>? nodeEntities = null;

                if (compiledEntity.GetStringProperty("hammeruniqueid") is { } pathId)
                {
                    pathNodeEntities.TryGetValue(pathId, out nodeEntities);
                }

                AddPathNodes(mapPath, className, compiledEntity, pathNodes, nodeEntities, worldTransform);
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
                        selectionSet.ObjectSelection.SelectedObjects.Add(mapEntity);
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

            if (modelName != null && PathIsSubPath(modelName, LumpFolder) && mapEntity is CMapPath)
            {
                if (mapEntity is CMapCable cable)
                {
                    RecoverCableAttributes(cable, className, compiledEntity, modelName, pathNodes);
                }

                mapEntity.EntityProperties.Remove("model");
            }
            else if (modelName != null && PathIsSubPath(modelName, LumpFolder))
            {
                var firstReference = ModelEntityAssociations.TryAdd(modelName, className);
                if (!firstReference)
                {
                    var otherClass = ModelEntityAssociations[modelName];
                    Debug.Assert(className == otherClass, "Model living in lump folder referenced by more than one entity type!\n" +
                        $"model = {modelName} {className} != {otherClass}");
                }

                ExtractEntityModel(mapEntity, modelName, worldTransform);

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

    private void ExtractEntityModel(CMapEntity mapEntity, string modelName, Matrix4x4 worldTransform)
    {
        using var model = FileLoader.LoadFileCompiled(modelName);
        if (model is null || model.DataBlock is null)
        {
            return;
        }

        var EntitiesToHammerMesh = true;
        ModelEntityAssociations.TryGetValue(modelName, out var associatedEntityClass);

        var data = (Model)model.DataBlock;

        var hasMeshes = data.GetEmbeddedMeshes().Any() || data.GetReferenceMeshNamesAndLoD().Any();
        var hasPhysics = data.GetEmbeddedPhys() != null || data.GetReferencedPhysNames().Any();
        var isJustPhysics = hasPhysics && !hasMeshes;

        if (EntitiesToHammerMesh)
        {
            AddEntityModelHammerMeshes(mapEntity, data, model, isJustPhysics, associatedEntityClass, worldTransform);

            // The model is a compiler output that the entity holds as hammer meshes.
            mapEntity.EntityProperties.Remove("model");
            ProvidedResources.Remove(modelName);
            return;
        }

        var toolTexture = ToolsMaterials.GetForEntity(associatedEntityClass, FileLoader);
        Debug.Assert(toolTexture is not null);
        var modelExtract = new ModelExtract(model, FileLoader)
        {
            Type = isJustPhysics
                ? ModelExtract.ModelExtractType.Map_PhysicsToRenderMesh
                : ModelExtract.ModelExtractType.Default,
            PhysicsToRenderMaterialNameProvider = (_) => toolTexture,
            ReconstructSoftbody = ReconstructSoftbody,
        };

        var vmdl = modelExtract.ToContentFile();
        EntityModels.Add(vmdl);
    }

    private void AddEntityModelHammerMeshes(CMapEntity mapEntity, Model data, Resource model, bool isJustPhysics, string? associatedEntityClass, Matrix4x4 worldTransform)
    {
        if (isJustPhysics)
        {
            var phys = data.GetEmbeddedPhys();
            if (phys != null)
            {
                AddPhysHammerMeshes(mapEntity, phys, associatedEntityClass, worldTransform);
                return;
            }

            var referencedPhysName = data.GetReferencedPhysNames().FirstOrDefault();
            if (referencedPhysName != null)
            {
                using var physResource = FileLoader.LoadFileCompiled(referencedPhysName);
                if (physResource?.DataBlock is PhysAggregateData referencedPhys)
                {
                    AddPhysHammerMeshes(mapEntity, referencedPhys, associatedEntityClass, worldTransform);
                }
            }

            return;
        }

        foreach (var hammermesh in RenderMeshToHammerMesh(data, model, associatedEntityClass, worldTransform))
        {
            mapEntity.Children.Add(hammermesh);
        }
    }

    private void AddPhysHammerMeshes(CMapEntity mapEntity, PhysAggregateData phys, string? associatedEntityClass, Matrix4x4 worldTransform)
    {
        foreach (var hammermesh in PhysToHammerMeshes(phys, worldTransform, associatedEntityClass))
        {
            mapEntity.Children.Add(hammermesh);
        }
    }

    private int[] AddProperties(string className, Entity compiledEntity, BaseEntity mapEntity)
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

            var editString = ApplyNameFixup(ToEditString(value) ?? string.Empty, string.Empty, string.Empty);

            mapEntity.EntityProperties.Add(propertyKey, editString);
        }

        ResourceProvider?.AddExportProperties(className, mapEntity.EntityProperties);

        if (compiledEntity.Connections != null)
        {
            foreach (var connection in compiledEntity.Connections)
            {
                var dmeConnection = new DmeConnectionData
                {
                    OutputName = connection.OutputName,
                    TargetType = (int)connection.TargetType,
                    TargetName = ApplyNameFixup(connection.TargetName, string.Empty, string.Empty),
                    InputName = connection.InputName,
                    OverrideParam = connection.OverrideParam,
                    Delay = connection.Delay,
                    TimesToFire = connection.TimesToFire,
                };

                if (connection.ParamMap != null)
                {
                    dmeConnection.Add("paramMap", ToDmeKeyValues3(connection.ParamMap, "paramMap"));
                }

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
        else if (IsPathProperty(key, mapEntity))
        {
            return true;
        }
        else if (key == "hammeruniqueid")
        {
            try
            {
                var hammerUniqueIdString = compiledEntity.TryGetValue(key, out var hammerValue) ? ToEditString(hammerValue) : null;
                if (!string.IsNullOrEmpty(hammerUniqueIdString))
                {
                    lineage = Array.ConvertAll(hammerUniqueIdString.Split(':'), static part => int.Parse(part, CultureInfo.InvariantCulture));
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

    // Hammer stores KV3 inside DMX as nested elements: a table is an element whose attributes are its keys,
    // and array items that do not fit a typed DMX array are wrapped in elements holding a "value" attribute.
    // Hammer names each table element after its key.
    private static object? ToDmeKeyValues3(KVObject value, string name)
    {
        switch (value.ValueType)
        {
            case KVValueType.Collection:
                var element = new Datamodel.Element { Name = name, ClassName = "DmElement" };

                foreach (var (key, child) in value.Children)
                {
                    var converted = ToDmeKeyValues3(child, key);

                    if (converted != null)
                    {
                        // An attribute called "name" would clash with the element's own name
                        element.Add(key == "name" ? "__dmekv3_attribute_will_be_name__" : key, converted);
                    }
                }

                return element;

            case KVValueType.Array:
                var items = value.Elements;

                if (ToDmeTypedArray(items) is { } typedArray)
                {
                    return typedArray;
                }

                var elements = new Datamodel.ElementArray(items.Count);

                for (var i = 0; i < items.Count; i++)
                {
                    var wrapper = new Datamodel.Element { Name = $"array_{i}", ClassName = "DmElement" };
                    var converted = ToDmeKeyValues3(items[i], "value");

                    if (converted != null)
                    {
                        wrapper.Add("value", converted);
                    }

                    elements.Add(wrapper);
                }

                return elements;

            case KVValueType.Boolean:
                return (bool)value;

            case KVValueType.String:
                // The compiler prefixes entity names used as literals, like it does targetnames
                var text = ApplyNameFixup((string)value, string.Empty, string.Empty);

                var specificType = value.Flag.SerializeFlagName();

                if (specificType == null)
                {
                    return text;
                }

                // Typed strings such as entity names are wrapped in an element carrying the type
                var typed = new Datamodel.Element { Name = "value_with_specific_type", ClassName = "DmElement" };
                typed.Add("specific_type", specificType);
                typed.Add("value", text);
                return typed;

            case var type when IsKeyValues3Unsigned(type):
                return Convert.ToUInt64(value, CultureInfo.InvariantCulture);

            case var type when IsKeyValues3Float(type):
                return Convert.ToSingle(value, CultureInfo.InvariantCulture);

            case var type when IsKeyValues3Signed(type):
                var integer = Convert.ToInt64(value, CultureInfo.InvariantCulture);
                return integer is >= int.MinValue and <= int.MaxValue ? (int)integer : 0;

            default:
                return null;
        }
    }

    // A typed DMX array holds items of one type with no string flag, or 2 to 4 component float vectors.
    // Anything else is written as an element array.
    private static object? ToDmeTypedArray(IReadOnlyList<KVObject> items)
    {
        if (items.Count == 0)
        {
            return null;
        }

        var first = items[0];

        if (first.ValueType == KVValueType.Array)
        {
            return ToDmeVectorArray(items);
        }

        if (first.Flag != KVFlag.None || items.Any(item => item.ValueType != first.ValueType || item.Flag != KVFlag.None))
        {
            return null;
        }

        return first.ValueType switch
        {
            KVValueType.Boolean => new Datamodel.BoolArray(items.Select(static item => (bool)item)),
            KVValueType.String => new Datamodel.StringArray(items.Select(static item => ApplyNameFixup((string)item, string.Empty, string.Empty))),
            var type when IsKeyValues3Unsigned(type) => new Datamodel.UInt64Array(items.Select(static item => Convert.ToUInt64(item, CultureInfo.InvariantCulture))),
            var type when IsKeyValues3Float(type) => new Datamodel.FloatArray(items.Select(static item => Convert.ToSingle(item, CultureInfo.InvariantCulture))),
            var type when IsKeyValues3Signed(type) => new Datamodel.IntArray(items.Select(static item => unchecked((int)Convert.ToInt64(item, CultureInfo.InvariantCulture)))),
            _ => null,
        };
    }

    private static object? ToDmeVectorArray(IReadOnlyList<KVObject> items)
    {
        var vectors = new List<float[]>(items.Count);

        foreach (var item in items)
        {
            if (item.ValueType != KVValueType.Array)
            {
                return null;
            }

            var components = item.Elements;

            if ((vectors.Count > 0 && components.Count != vectors[0].Length) || !components.All(static component => IsKeyValues3Float(component.ValueType)))
            {
                return null;
            }

            vectors.Add([.. components.Select(static component => Convert.ToSingle(component, CultureInfo.InvariantCulture))]);
        }

        return vectors[0].Length switch
        {
            2 => new Datamodel.Vector2Array(vectors.Select(static v => new Vector2(v[0], v[1]))),
            3 => new Datamodel.Vector3Array(vectors.Select(static v => new Vector3(v[0], v[1], v[2]))),
            4 => new Datamodel.Vector4Array(vectors.Select(static v => new Vector4(v[0], v[1], v[2], v[3]))),
            _ => null,
        };
    }

    private static bool IsKeyValues3Float(KVValueType type)
        => type is KVValueType.FloatingPoint or KVValueType.FloatingPoint64;

    private static bool IsKeyValues3Signed(KVValueType type)
        => type is KVValueType.Int16 or KVValueType.Int32 or KVValueType.Int64;

    private static bool IsKeyValues3Unsigned(KVValueType type)
        => type is KVValueType.UInt16 or KVValueType.UInt32 or KVValueType.UInt64;

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
            return System.Text.Encoding.UTF8.GetString(ms.GetBuffer(), 0, (int)ms.Length);
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
