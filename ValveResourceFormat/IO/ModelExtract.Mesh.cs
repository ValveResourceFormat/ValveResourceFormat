using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using Datamodel;
using ValveResourceFormat.Blocks;
using ValveResourceFormat.IO.ContentFormats.DmxModel;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.ResourceTypes.ModelAnimation;
using ValveResourceFormat.ResourceTypes.RubikonPhysics;
using ValveResourceFormat.Serialization.KeyValues;
using RnShapes = ValveResourceFormat.ResourceTypes.RubikonPhysics.Shapes;

namespace ValveResourceFormat.IO;

partial class ModelExtract
{
    /// <summary>
    /// Gets the list of physics hulls to be extracted with their output file names.
    /// </summary>
    public List<(HullDescriptor Hull, string FileName, string ParentBone)> PhysHullsToExtract { get; } = [];

    /// <summary>
    /// Gets the list of physics meshes to be extracted with their output file names.
    /// </summary>
    public List<(MeshDescriptor Mesh, string FileName, string ParentBone)> PhysMeshesToExtract { get; } = [];

    /// <summary>
    /// Gets the list of render meshes to be extracted.
    /// </summary>
    public List<RenderMeshExtractConfiguration> RenderMeshesToExtract { get; } = [];

    /// <summary>
    /// Gets the list of cloth proxy meshes (cloth "sheets") to be extracted as sub-DMX files. Built from
    /// the soft-body <see cref="FeModel"/> surface so a recompile regenerates the <c>$cloth_*</c> nodes.
    /// </summary>
    public List<(string FileName, string Name, FeModel.ProxyMesh Proxy)> ClothProxyMeshesToExtract { get; } = [];

    /// <summary>
    /// Gets the list of cloth sheet grids generated over neighbouring bone chains (skirts/capes whose
    /// original cloth is chain-only), extracted as sub-DMX files. The sheet simulates the surface between
    /// the chains and drives the render mesh directly, like hand-authored item proxies.
    /// </summary>
    public List<(string FileName, string Name, FeModel.ChainGrid Grid)> ClothChainGridsToExtract { get; } = [];

    /// <summary>
    /// Gets the material input signatures for mapping DirectX semantic names.
    /// </summary>
    public Dictionary<string, Material.VsInputSignature> MaterialInputSignatures { get; } = [];

    /// <summary>
    /// Gets the physics surface property names discovered in the aggregate data.
    /// </summary>
    public string[] PhysicsSurfaceNames { get; private set; } = [];

    /// <summary>
    /// Gets the physics collision tag sets associated with the current aggregate data.
    /// </summary>
    public HashSet<string>[] PhysicsCollisionTags { get; private set; } = [];

    /// <summary>
    /// Gets the set of surface tag combinations.
    /// </summary>
    public HashSet<SurfaceTagCombo> SurfaceTagCombos { get; } = [];

    /// <summary>
    /// Gets the function to provide render material names for physics surface tags.
    /// </summary>
    public Func<SurfaceTagCombo, string>? PhysicsToRenderMaterialNameProvider { get; init; }

    /// <summary>
    /// Gets or sets the translation offset for the model.
    /// </summary>
    public Vector3 Translation { get; set; }

    /// <summary>
    /// Options for extracting a render mesh to datamodel format.
    /// </summary>
    public readonly struct DatamodelRenderMeshExtractOptions
    {
        /// <summary>
        /// Split draw calls into sub-meshes named draw0, draw1, draw2...
        /// </summary>
        public bool SplitDrawCallsIntoSeparateSubmeshes { get; init; }

        /// <summary>
        /// Pre-parsed input signatures used to map DirectX semantic names to engine semantic names.
        /// </summary>
        public Dictionary<string, Material.VsInputSignature> MaterialInputSignatures { get; init; }

        /// <summary>
        /// Remap table for the mesh bone indices.
        /// </summary>
        public int[]? BoneRemapTable { get; init; }

        /// <summary>
        /// Skeleton whose bones the mesh's BLENDINDICES reference (post-remap, in <see cref="Bone.Index"/> order).
        /// When provided, bones are emitted into the DMX <c>jointList</c> so ModelDoc can resolve indices.
        /// </summary>
        public Skeleton? Skeleton { get; init; }
    }

    /// <summary>
    /// Configuration for extracting a render mesh.
    /// </summary>
    public record struct RenderMeshExtractConfiguration(
        Mesh Mesh,
        string Name,
        int Index,
        string FileName,
        int[]? BoneRemapTable = null,
        Skeleton? Skeleton = null,
        ImportFilter ImportFilter = default
    );

    /// <summary>
    /// Represents a combination of surface property and collision tags.
    /// </summary>
    public sealed record SurfaceTagCombo(string SurfacePropName, HashSet<string> InteractAsStrings)
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="SurfaceTagCombo"/> record.
        /// </summary>
        public SurfaceTagCombo(string surfacePropName, string[] collisionTags)
            : this(surfacePropName, new HashSet<string>(collisionTags))
        { }

        /// <summary>
        /// Gets the string representation of the material.
        /// </summary>
        public string StringMaterial => string.Join('+', InteractAsStrings) + '$' + SurfacePropName;

        /// <inheritdoc/>
        /// <remarks>
        /// Returns the hash code of the string material representation.
        /// </remarks>
        public override int GetHashCode() => StringMaterial.GetHashCode(StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// Determines whether the specified <see cref="SurfaceTagCombo"/> is equal to the current instance.
        /// </summary>
        public bool Equals(SurfaceTagCombo? other) => other is not null && GetHashCode() == other.GetHashCode();
    }

    string GetDmxFileName_ForEmbeddedMesh(string subString, int number = 0)
    {
        var fileName = ModelName;
        return (Path.GetDirectoryName(fileName)
            + Path.DirectorySeparatorChar
            + Path.GetFileNameWithoutExtension(fileName)
            + "_"
            + subString
            + (number > 0 ? number : string.Empty)
            + ".dmx")
            .Replace('\\', '/');
    }

    static string GetDmxFileName_ForReferenceMesh(string fileName)
        => Path.ChangeExtension(fileName, ".dmx").Replace('\\', '/');

    private void EnqueueMeshes()
    {
        if (fileLoader is not null) // May be null for mesh-only constructor
        {
            FileExtract.EnsurePopulatedStringToken(fileLoader);
        }
        EnqueueRenderMeshes();
        EnqueuePhysMeshes();
        EnqueueClothProxyMesh();
    }

    // Queues a cloth proxy-mesh DMX when the model carries a soft-body FeModel with a surface (quads/tris),
    // or generated sheet grids over the bone chains when the original cloth is chain-only.
    private void EnqueueClothProxyMesh()
    {
        if (model is null || physAggregateData?.FeModel is not { } feModel)
        {
            return;
        }

        var proxyIndex = 0;
        foreach (var proxyMesh in feModel.BuildProxyMeshes())
        {
            // One proxy per island, like the originals (node names $cloth_mXpY encode the mesh index).
            var proxyName = "cloth_proxy" + (proxyIndex > 0 ? proxyIndex.ToString(CultureInfo.InvariantCulture) : string.Empty);
            ClothProxyMeshesToExtract.Add((GetDmxFileName_ForEmbeddedMesh(proxyName), proxyName, proxyMesh));
            proxyIndex++;
        }

        // Regular sheet grids over the bone chains are generated in BOTH cases: as the only sheet for
        // chain-only cloth, and as an alternative clean editable grid next to a recovered surface.
        // They always ship disabled (see the vmdl emission) - purely a ready-made authoring asset.
        var gridIndex = 0;
        foreach (var grid in feModel.BuildChainGrids())
        {
            var name = "cloth_grid" + (gridIndex > 0 ? gridIndex.ToString(CultureInfo.InvariantCulture) : string.Empty);
            ClothChainGridsToExtract.Add((GetDmxFileName_ForEmbeddedMesh(name), name, grid));
            gridIndex++;
        }
    }

    private void EnqueueRenderMeshes()
    {
        if (model == null)
        {
            return;
        }

        GrabMaterialInputSignatures(modelResource);

        var i = 0;
        foreach (var embedded in model.GetEmbeddedMeshes())
        {
            // Decode the morph (face flex) atlas now while the FileLoader is available so that
            // ConvertMeshToDatamodelMesh can emit DMX delta states from embedded.Mesh.MorphData.
            if (fileLoader is not null)
            {
                embedded.Mesh.LoadExternalMorphData(fileLoader);
            }

            var remapTable = model.GetRemapTable(embedded.MeshIndex);
            RenderMeshesToExtract.Add(new(embedded.Mesh, embedded.Name, embedded.MeshIndex, GetDmxFileName_ForEmbeddedMesh(embedded.Name, i++), remapTable, model.Skeleton));
        }

        foreach (var reference in model.GetReferenceMeshNamesAndLoD())
        {
            Debug.Assert(fileLoader is not null, "fileLoader should not be null when loading reference meshes");

            using var resource = fileLoader.LoadFileCompiled(reference.MeshName);

            if (resource is null)
            {
                continue;
            }

            GrabMaterialInputSignatures(resource);

            if (resource.DataBlock is not Mesh mesh)
            {
                continue;
            }

            model.SetExternalMeshData(mesh);
            mesh.LoadExternalMorphData(fileLoader);

            var remapTable = model.GetRemapTable(reference.MeshIndex);
            var meshKey = Path.GetFileNameWithoutExtension(reference.MeshName);

            RenderMeshesToExtract.Add(new(mesh, meshKey, reference.MeshIndex, GetDmxFileName_ForReferenceMesh(reference.MeshName), remapTable, model.Skeleton));
        }
    }

    internal void GrabMaterialInputSignatures(Resource? resource)
    {
        Debug.Assert(fileLoader is not null, "fileLoader should not be null when grabbing material signatures");

        var materialReferences = resource?.ExternalReferences?.ResourceRefInfoList.Where(static r => r.Name[^4..] == "vmat");
        foreach (var material in materialReferences ?? [])
        {
            using var materialResource = fileLoader.LoadFileCompiled(material.Name);
            MaterialInputSignatures[material.Name] = (materialResource?.DataBlock as Material)?.InputSignature ?? Material.VsInputSignature.Empty;
        }
    }

    private void EnqueuePhysMeshes()
    {
        if (physAggregateData == null)
        {
            return;
        }

        PhysicsSurfaceNames = physAggregateData.SurfacePropertyHashes.Select(StringToken.GetKnownString).ToArray();

        PhysicsCollisionTags = physAggregateData.CollisionAttributes.Select(attributes =>
            (attributes.GetArray<string>("m_InteractAsStrings") ?? attributes.GetArray<string>("m_PhysicsTagStrings"))!.ToHashSet()
        ).ToArray();

        // Fix index error on some old vphys files
        if (PhysicsSurfaceNames.Length == 0)
        {
            PhysicsSurfaceNames = [string.Empty];
        }

        if (PhysicsCollisionTags.Length == 0)
        {
            PhysicsCollisionTags = [[]];
        }

        var i = 0;
        for (var partIndex = 0; partIndex < physAggregateData.Parts.Length; partIndex++)
        {
            var physicsPart = physAggregateData.Parts[partIndex];
            var parentBone = physAggregateData.GetParentBoneName(partIndex);

            foreach (var hull in physicsPart.Shape.Hulls)
            {
                PhysHullsToExtract.Add((hull, GetDmxFileName_ForEmbeddedMesh("hull", i++), parentBone));
                StoreSurfaceTagCombo(hull);
            }

            foreach (var mesh in physicsPart.Shape.Meshes)
            {
                PhysMeshesToExtract.Add((mesh, GetDmxFileName_ForEmbeddedMesh("phys", i++), parentBone));

                StoreSurfaceTagCombo(mesh);

                foreach (var surfaceIndex in mesh.Shape.Materials)
                {
                    StoreSurfaceTagCombo(mesh.CollisionAttributeIndex, surfaceIndex);
                }
            }
        }
    }

    private void StoreSurfaceTagCombo<T>(ShapeDescriptor<T> shapeDesc) where T : struct
        => StoreSurfaceTagCombo(shapeDesc.CollisionAttributeIndex, shapeDesc.SurfacePropertyIndex);

    private void StoreSurfaceTagCombo(int collisionAttributeIndex, int surfacePropertyIndex)
    {
        if (PhysicsCollisionTags.Length <= collisionAttributeIndex
        || PhysicsSurfaceNames.Length <= surfacePropertyIndex)
        {
            return;
        }

        SurfaceTagCombos.Add(new SurfaceTagCombo(
            PhysicsSurfaceNames[surfacePropertyIndex],
            PhysicsCollisionTags[collisionAttributeIndex]
        ));
    }

    /// <summary>
    /// Extracts content files from an aggregate model resource, splitting by draw calls.
    /// </summary>
    public static IEnumerable<ContentFile> GetContentFiles_DrawCallSplit(Resource aggregateModelResource, IFileLoader fileLoader, Vector3[] drawOrigins, int drawCallCount)
    {
        var extract = new ModelExtract(aggregateModelResource, fileLoader) { Type = ModelExtractType.Map_AggregateSplit };
        Debug.Assert(extract.RenderMeshesToExtract.Count == 1);

        if (extract.RenderMeshesToExtract.Count == 0)
        {
            yield break;
        }

        var (mesh, name, index, fileName, boneRemapTable, skeleton, _) = extract.RenderMeshesToExtract[0];

        var options = new DatamodelRenderMeshExtractOptions
        {
            MaterialInputSignatures = extract.MaterialInputSignatures,
            SplitDrawCallsIntoSeparateSubmeshes = true,
            BoneRemapTable = boneRemapTable,
            Skeleton = skeleton,
        };

        byte[] sharedDmxExtractMethod() => ToDmxMesh(
            mesh,
            Path.GetFileNameWithoutExtension(fileName),
            options
        );

        var sharedMeshExtractConfiguration = new RenderMeshExtractConfiguration(mesh, name, index, fileName, boneRemapTable, skeleton, new(true, new(1)));
        extract.RenderMeshesToExtract.Clear();
        extract.RenderMeshesToExtract.Add(sharedMeshExtractConfiguration);

        for (var i = 0; i < drawCallCount; i++)
        {
            sharedMeshExtractConfiguration.ImportFilter.Filter.Clear();
            sharedMeshExtractConfiguration.ImportFilter.Filter.Add("draw" + i);

            extract.Translation = drawOrigins.Length > i
                ? -1 * drawOrigins[i]
                : Vector3.Zero;

            var vmdl = new ContentFile
            {
                Data = Encoding.UTF8.GetBytes(extract.ToValveModel()),
                FileName = GetFragmentModelName(extract.ModelName, i),
            };

            if (i == 0)
            {
                vmdl.AddSubFile(Path.GetFileName(fileName), sharedDmxExtractMethod);
            }

            yield return vmdl;
        }
    }

    /// <summary>
    /// Gets the fragment model name for a draw call index.
    /// </summary>
    public static string GetFragmentModelName(string aggModelName, int drawCallIndex)
    {
        const string vmdlExt = ".vmdl";
        return aggModelName[..^vmdlExt.Length] + "_draw" + drawCallIndex + vmdlExt;
    }

    private static void FillDatamodelVertexData(VBIB.OnDiskBufferData vertexBuffer, DmeVertexData vertexData, Material.VsInputSignature materialInputSignature,
        int boneWeightCount, int[]? boneRemapTable)
    {
        var indices = Enumerable.Range(0, (int)vertexBuffer.ElementCount).ToArray(); // May break with non-unit strides, non-tri faces

        var boneArrayComponents = boneWeightCount > 4 ? 8 : 4;

        foreach (var attribute in vertexBuffer.InputLayoutFields)
        {
            var attributeFormat = VBIB.GetFormatInfo(attribute);
            var semantic = attribute.SemanticName.ToLowerInvariant() + "$" + attribute.SemanticIndex;

            if (attribute.SemanticName is "NORMAL")
            {
                var (normals, tangents) = VBIB.GetNormalTangentArray(vertexBuffer, attribute);
                vertexData.AddIndexedStream(semantic, normals, indices);

                if (tangents.Length > 0)
                {
                    vertexData.AddIndexedStream("tangent$" + attribute.SemanticIndex, tangents, indices);
                }

                continue;
            }
            else if (attribute.SemanticName is "BLENDINDICES")
            {
                vertexData.JointCount = boneWeightCount;

                var boneIndices = VBIB.GetBlendIndicesArray(vertexBuffer, attribute, boneRemapTable);
                var compactedLength = boneIndices.Length / boneArrayComponents * boneWeightCount;

                var compactIndices = new int[compactedLength];
                for (var i = 0; i < boneIndices.Length; i += boneArrayComponents)
                {
                    for (var j = 0; j < boneWeightCount; j++)
                    {
                        compactIndices[i / boneArrayComponents * boneWeightCount + j] = boneIndices[i + j];
                    }
                }

                vertexData.AddStream(semantic, compactIndices);
                continue;
            }
            else if (attribute.SemanticName is "BLENDWEIGHT" or "BLENDWEIGHTS")
            {
                var vectorWeights = VBIB.GetBlendWeightsArray(vertexBuffer, attribute);
                var flatWeights = MemoryMarshal.Cast<Vector4, float>(vectorWeights).ToArray();

                var compactWeights = new float[flatWeights.Length / boneArrayComponents * boneWeightCount];
                for (var i = 0; i < flatWeights.Length; i += boneArrayComponents)
                {
                    for (var j = 0; j < boneWeightCount; j++)
                    {
                        compactWeights[i / boneArrayComponents * boneWeightCount + j] = flatWeights[i + j];
                    }
                }

                vertexData.AddStream("blendweights$" + attribute.SemanticIndex, compactWeights);
                continue;
            }

            if (materialInputSignature.Elements.Length > 0)
            {
                var insgElement = Material.FindD3DInputSignatureElement(materialInputSignature, attribute.SemanticName, attribute.SemanticIndex);

                // Use engine semantics for attributes that need them
                if (insgElement.Semantic is "VertexPaintBlendParams" or "VertexPaintTintColor")
                {
                    semantic = insgElement.Semantic + "$0";
                }
            }

            switch (attributeFormat.ElementCount)
            {
                case 1:
                    var scalar = VBIB.GetScalarAttributeArray(vertexBuffer, attribute);
                    vertexData.AddIndexedStream(semantic, scalar, indices);
                    break;
                case 2:
                    var vec2 = VBIB.GetVector2AttributeArray(vertexBuffer, attribute);
                    vertexData.AddIndexedStream(semantic, vec2, indices);
                    break;
                case 3:
                    var vec3 = VBIB.GetVector3AttributeArray(vertexBuffer, attribute);
                    vertexData.AddIndexedStream(semantic, vec3, indices);
                    break;
                case 4:
                    var vec4 = VBIB.GetVector4AttributeArray(vertexBuffer, attribute);
                    vertexData.AddIndexedStream(semantic, vec4, indices);
                    break;
                default:
                    throw new NotImplementedException($"Stream {semantic} has an unexpected number of components: {attributeFormat.ElementCount}.");
            }
        }

        if (vertexData.VertexFormat.Contains("blendindices$0") && !vertexData.VertexFormat.Contains("blendweights$0"))
        {
            if (!vertexData.TryGetValue("blendindices$0", out var blendIndices) || blendIndices is not ICollection<int> collection)
            {
                throw new InvalidOperationException("blendindices$0 stream not found");
            }

            vertexData.AddStream("blendweights$0", Enumerable.Repeat(1f, collection.Count).ToArray());
        }
    }

    /// <summary>
    /// Converts a mesh to DMX format.
    /// </summary>
    public static byte[] ToDmxMesh(Mesh mesh, string name, DatamodelRenderMeshExtractOptions options = default)
    {
        using var dmx = ConvertMeshToDatamodelMesh(mesh, name, options);
        using var stream = new MemoryStream();
        dmx.Save(stream, "binary", 9);

        return stream.ToArray();
    }

    /// <summary>
    /// Converts a mesh to a datamodel mesh representation.
    /// </summary>
    public static Datamodel.Datamodel ConvertMeshToDatamodelMesh(Mesh mesh, string name, DatamodelRenderMeshExtractOptions options)
    {
        var mdat = mesh.Data;
        var mbuf = mesh.VBIB;
        var indexBuffers = mbuf.IndexBuffers.Select(ib => new Lazy<int[]>(() => GltfModelExporter.ReadIndices(ib, 0, (int)ib.ElementCount, 0))).ToArray();

        var datamodel = new Datamodel.Datamodel("model", 22);
        var dmeModel = new DmeModel() { Name = name };
        var dmeVertexBuffers = new Dictionary<(int, int), (DmeDag Dag, DmeVertexData VertexData)>(mbuf.VertexBuffers.Count);

        // Populate the joint list with bones up-front so DMX BLENDINDICES line up with Bone.Index.
        // ModelDoc resolves mesh skinning indices through this list; without it the mesh is bound to "no skeleton".
        if (options.Skeleton is { Bones.Length: > 0 } skeleton)
        {
            dmeModel = BuildDmeDagSkeleton(skeleton, out _);
            dmeModel.Name = name;
        }

        var materialInputSignature = Material.VsInputSignature.Empty;
        var drawCallIndex = 0;

        // Per draw call ranges used to map morph deltas (indexed in mesh vertex order) onto the
        // DMX vertex buffers. GlobalOffset is the cumulative vertex offset across draw calls (the
        // index space the morph atlas uses); BaseVertex is where the draw call's verts start in the
        // DMX vertex buffer (== GlobalOffset for single-buffer meshes such as hero face models).
        var morphDrawCalls = new List<(DmeMesh DmeMesh, int BaseVertex, int VertexCount, int GlobalOffset)>();
        var morphVertexOffset = 0;
        var hasMorphData = mesh.MorphData is not null;

        foreach (var sceneObject in mdat.GetArray("m_sceneObjects"))
        {
            foreach (var drawCall in sceneObject.GetArray("m_drawCalls"))
            {
                var vertexBuffers = drawCall.GetArray("m_vertexBuffers");

                Debug.Assert(vertexBuffers.Count <= 2); // Hello traveler, if you are here to update this code to support more than 2 buffers!

                var dmeVertexBufferKey = (
                    vertexBuffers[0].GetInt32Property("m_hBuffer"),
                    vertexBuffers.Count > 1 ? vertexBuffers[1].GetInt32Property("m_hBuffer") : -1
                );

                if (!dmeVertexBuffers.TryGetValue(dmeVertexBufferKey, out var dmeVertexBuffer))
                {
                    dmeVertexBuffer = CreateDmxDagVertexData(dmeModel, name);
                    dmeVertexBuffers[dmeVertexBufferKey] = dmeVertexBuffer;
                }

                var indexBufferInfo = drawCall.GetSubCollection("m_indexBuffer");
                var indexBufferIndex = indexBufferInfo.GetInt32Property("m_hBuffer");
                ReadOnlySpan<int> indexBuffer = indexBuffers[indexBufferIndex].Value;

                var material = drawCall.GetStringProperty("m_material") ?? drawCall.GetStringProperty("m_pMaterial");

                if (material != null && options.MaterialInputSignatures != null && (materialInputSignature.Elements == null || materialInputSignature.Elements.Length == 0))
                {
                    materialInputSignature = options.MaterialInputSignatures.GetValueOrDefault(material);
                }

                if (material == null && Mesh.IsOccluder(drawCall))
                {
                    material = "materials/tools/toolsoccluder.vmat";
                }

                material ??= "materials/default.vmat";

                var baseVertex = drawCall.GetInt32Property("m_nBaseVertex");
                var startIndex = drawCall.GetInt32Property("m_nStartIndex");
                var indexCount = drawCall.GetInt32Property("m_nIndexCount");

                var dag = dmeVertexBuffer.Dag;

                if (options.SplitDrawCallsIntoSeparateSubmeshes)
                {
                    var subMeshName = "draw" + drawCallIndex;

                    if (drawCallIndex > 0)
                    {
                        // new submesh with same vertex buffer as first submesh
                        dag = CreateDmxDag(dmeModel, dmeVertexBuffer.VertexData, subMeshName);
                    }

                    dag.Shape!.Name = subMeshName;
                }

                GenerateTriangleFaceSetFromIndexBuffer(
                    dag,
                    indexBuffer[startIndex..(startIndex + indexCount)],
                    baseVertex,
                    material,
                    $"{startIndex}..{startIndex + indexCount}"
                );

                if (hasMorphData && dag.Shape is DmeMesh morphTargetMesh)
                {
                    var vertexCount = drawCall.GetInt32Property("m_nVertexCount");
                    morphDrawCalls.Add((morphTargetMesh, baseVertex, vertexCount, morphVertexOffset));
                    morphVertexOffset += vertexCount;
                }

                drawCallIndex++;
            }
        }

        var boneWeightCount = mesh.Data.GetSubCollection("m_skeleton")?.GetInt32Property("m_nBoneWeightCount") ?? 0;

        foreach (var (vertexBufferIndices, dmeObjects) in dmeVertexBuffers)
        {
            FillDatamodelVertexData(mbuf.VertexBuffers[vertexBufferIndices.Item1], dmeObjects.VertexData, materialInputSignature, boneWeightCount, options.BoneRemapTable);

            if (vertexBufferIndices.Item2 != -1)
            {
                FillDatamodelVertexData(mbuf.VertexBuffers[vertexBufferIndices.Item2], dmeObjects.VertexData, materialInputSignature, boneWeightCount, options.BoneRemapTable);
            }
        }

        // Emit face-flex morph targets as DMX delta states so a recompile rebuilds the MRPH block
        // (morph atlas + flex controllers/rules). Without this the model loses all face flex.
        DmeCombinationOperator? combinationOperator = null;
        if (mesh.MorphData is { } morphData && morphDrawCalls.Count > 0)
        {
            combinationOperator = AddMorphDeltaStates(morphData, morphDrawCalls);
        }

        TieElementRoot(datamodel, dmeModel, combinationOperator);
        return datamodel;
    }

    /// <summary>
    /// Builds the cloth proxy-mesh DMX (the cloth "sheet") from the soft-body <see cref="FeModel"/>.
    /// Vertices are the FeModel surface control nodes (positions = their rest pose), faces come from the
    /// quad/tri surface, each vertex carries a <c>cloth_enable$0</c> paint value (1 = simulated, 0 = pinned)
    /// and is skinned to the real skeleton bone it is anchored to. A recompile turns this back into the
    /// <c>$cloth_*</c> FeModel nodes (one per enabled vertex). The skeleton is emitted into the DMX joint
    /// list so the skinning resolves, exactly like a render mesh.
    /// </summary>
    internal byte[] BuildClothProxyMeshDmx(FeModel.ProxyMesh proxy, string name)
    {
        Debug.Assert(model is not null, "model required for cloth proxy mesh");

        var skeleton = model.Skeleton;

        using var dmx = new Datamodel.Datamodel("model", 22);

        // Joint list = the full skeleton, so BLENDINDICES resolve (mirrors ConvertMeshToDatamodelMesh).
        var dmeModel = BuildDmeDagSkeleton(skeleton, out _);
        dmeModel.Name = name;

        var (dag, vertexData) = CreateDmxDagVertexData(dmeModel, name);
        dag.Shape!.Name = name;

        var vertexCount = proxy.Positions.Length;
        var identity = Enumerable.Range(0, vertexCount).ToArray();

        vertexData.AddIndexedStream("position$0", proxy.Positions, identity);

        // Constant normals, deliberately: feeding computed per-vertex surface normals makes the cloth
        // importer drop ALL quads/tris and degrade the whole sheet to distance rods (no bend/shear
        // solve), and the recompiled fe node orientations stop matching the original. With constant
        // normals the quad/tri surface survives and the fe node frames come out identical to Valve's.
        vertexData.AddIndexedStream("normal$0", Enumerable.Repeat(Vector3.UnitZ, vertexCount).ToArray(), identity);

        // The cloth importer needs texcoords on the proxy (authored proxies always carry them; without
        // UVs the surface is not accepted as a sheet). A bounding-box projection along the two largest
        // extents is enough - the UVs only need to vary smoothly across the sheet.
        var boundsMin = proxy.Positions.Aggregate(Vector3.Min);
        var boundsMax = proxy.Positions.Aggregate(Vector3.Max);
        var extent = boundsMax - boundsMin;
        Span<int> axes = [0, 1, 2];
        axes.Sort((a, b) => extent[b].CompareTo(extent[a]));
        var (axisU, axisV) = (axes[0], axes[1]);
        var texcoords = new Vector2[vertexCount];
        for (var v = 0; v < vertexCount; v++)
        {
            texcoords[v] = new Vector2(
                extent[axisU] > 1e-6f ? (proxy.Positions[v][axisU] - boundsMin[axisU]) / extent[axisU] : 0f,
                extent[axisV] > 1e-6f ? (proxy.Positions[v][axisV] - boundsMin[axisV]) / extent[axisV] : 0f);
        }

        vertexData.AddIndexedStream("texcoord$0", texcoords, identity);

        // Per-vertex cloth paint layers. The names + the full layer set match a current authored cloth
        // proxy (meepo_scream qop_body_proxy.dmx): cloth_goal_strength_v2 is the modern attribute the
        // ModelDoc cloth editor actually paints (the legacy cloth_goal_strength reads as 0 in the current
        // editor), and friction/drag/ground_collision are the paint layers the editor shows - omitting them
        // is what made other cloth items "disappear". All are recovered 0..1 paint values (NOT raw compiled
        // solver numbers): the old code wrote cloth_goal_damping = flPointDamping (~6.0), 60x outside the
        // slider's 0..1 range, which is why the editor showed 0 in the text while the slider sat pegged.
        vertexData.AddIndexedStream("cloth_enable$0", proxy.ClothEnable, identity);
        vertexData.AddIndexedStream("cloth_goal_strength_v2$0", proxy.GoalStrength, identity);
        vertexData.AddIndexedStream("cloth_goal_damping$0", proxy.GoalDamping, identity);
        vertexData.AddIndexedStream("cloth_collision_radius$0", proxy.CollisionRadius, identity);
        vertexData.AddIndexedStream("cloth_ground_collision$0", proxy.GroundCollision, identity);
        vertexData.AddIndexedStream("cloth_friction$0", proxy.Friction, identity);
        vertexData.AddIndexedStream("cloth_drag$0", proxy.Drag, identity);

        // Per-vertex gravity, painted VERBATIM: cloth_gravity$0 compiles into flGravity with no scaling
        // (measured: 0.002778 lands 0.002778, 1.0 lands 1). Without this stream the compiler defaults
        // every vertex to 360, silently discarding authored variation - dark_willow paints its hair
        // strands and paper lantern nearly weightless (flGravity=1) while the coattail is full weight
        // (360). A cloth_animation_attract$0 stream for the same integrator's out-of-range legacy
        // flAnimationVertexAttraction (15/10.5/6/5.25 on dark_willow) is IGNORED by the proxy importer (the
        // name belongs to ClothMapFilter's map list) - that field stays a legacy platform ceiling, do not
        // re-emit it.
        vertexData.AddIndexedStream("cloth_gravity$0", proxy.Gravity, identity);

        // cloth_drag_v2 and cloth_mass have no measurable effect on the compiled flPointDamping/
        // m_NodeInvMasses - cloth_drag (no suffix, unlike goal_strength) is already the attribute the
        // compiler reads, so they are intentionally omitted.

        // cloth_make_rods / cloth_bend_stiffness are compile-time-only per-face paint (no trace survives in
        // the compiled FeModel, so nothing is recoverable) that gates whether the mesh importer adds its
        // extra auto-derived bend/shear rods on top of the structural ones. The face-survival decision is
        // made PER FACE from that face's vertices' values relative to a ~0.5 threshold (meepo's authored
        // jaket paints them in a narrow band straddling 0.5, and its 52 quads compile to 1 quad + 1 tri), so
        // exact m_Tris/m_Quads are not recoverable from compiled data - the same class of gap as
        // DmeCombinationDominationRule. Kept UNDER the threshold (uniform 0.4): any value high enough to
        // discard a synthesized island's placeholder Delaunay faces also auto-derives a denser rod network
        // than AddClothProxySprings' own exact m_Rods reconstruction (a Delaunay cover has more adjacency
        // edges than a real hand-designed mesh), inflating m_Rods well past the original. Correct rod
        // topology matters more for simulated behaviour than the compiled quad/tri surface count.
        vertexData.AddIndexedStream("cloth_use_rods$0", Enumerable.Repeat(1f, vertexCount).ToArray(), identity);
        vertexData.AddIndexedStream("cloth_make_rods$0", Enumerable.Repeat(0.4f, vertexCount).ToArray(), identity);
        vertexData.AddIndexedStream("cloth_bend_stiffness$0", Enumerable.Repeat(0.2f, vertexCount).ToArray(), identity);

        // Skin the proxy vertices. Pinned (cloth_enable 0) vertices follow their anchor bone with weight 1;
        // simulated vertices carry smooth two-joint chain weights (see FeModel.ProxyMesh.SkinInfluences) so
        // the compiler back-solves each chain joint with a proper fit matrix instead of a point rope.
        //
        // Match bone names case-INSENSITIVELY: Source treats bone names case-insensitively (the model
        // compiler matched them that way originally), and a model's compiled FeModel m_CtrlName array does
        // NOT always agree in case with its skeleton. kez ships skeleton bones CapeLeafB_0/CapeLeafC_0 but
        // stores the cloth control nodes as capeLeafB_0/capeLeafC_0 - an Ordinal lookup drops every one of
        // those influences, the affected simulated vertices end up with all-zero blend weights, and with
        // back_solve_joints on the compiler then hits "Cannot find most-bound-joint for position N in mesh
        // cloth_proxyK_shape" and ACCESS-VIOLATION crashes (its CapeLeafA_* chain, which happens to agree in
        // case, back-solved fine - which is exactly how the bug hid). OrdinalIgnoreCase resolves them.
        var boneIndexByName = new Dictionary<string, int>(skeleton.Bones.Length * 2, StringComparer.OrdinalIgnoreCase);
        foreach (var bone in skeleton.Bones)
        {
            boneIndexByName.TryAdd(bone.Name, bone.Index);
            boneIndexByName.TryAdd(GetExportBoneName(bone), bone.Index);
        }

        const int JointCount = 4;
        var blendIndices = new int[vertexCount * JointCount];
        var blendWeights = new float[vertexCount * JointCount];
        for (var v = 0; v < vertexCount; v++)
        {
            var slot = 0;
            foreach (var (boneName, weight) in proxy.SkinInfluences[v])
            {
                if (slot >= JointCount || !boneIndexByName.TryGetValue(boneName, out var bi))
                {
                    continue;
                }

                blendIndices[v * JointCount + slot] = bi;
                blendWeights[v * JointCount + slot] = weight;
                slot++;
            }

        }

        vertexData.JointCount = JointCount;
        vertexData.AddStream("blendindices$0", blendIndices);
        vertexData.AddStream("blendweights$0", blendWeights);

        var faceSet = new DmeFaceSet { Name = "cloth" };
        faceSet.Material.MaterialName = "cloth";
        if (dag.Shape is DmeMesh dmeMesh)
        {
            dmeMesh.FaceSets.Add(faceSet);
        }

        foreach (var face in proxy.Faces)
        {
            foreach (var index in face)
            {
                faceSet.Faces.Add(index);
            }

            faceSet.Faces.Add(-1);
        }

        TieElementRoot(dmx, dmeModel);
        using var stream = new MemoryStream();
        dmx.Save(stream, "binary", 9);
        return stream.ToArray();
    }

    /// <summary>
    /// Builds a generated cloth sheet grid DMX over a group of bone chains (see
    /// <see cref="FeModel.BuildChainGrids"/>). Mirrors hand-authored item proxies: rows/columns of
    /// vertices spanning the chains, bilinear chain-joint skinning, recovered cloth paints, quad faces.
    /// </summary>
    internal byte[] BuildClothChainGridDmx(FeModel.ChainGrid grid, string name)
    {
        Debug.Assert(model is not null, "model required for cloth grid");

        var skeleton = model.Skeleton;

        using var dmx = new Datamodel.Datamodel("model", 22);

        var dmeModel = BuildDmeDagSkeleton(skeleton, out _);
        dmeModel.Name = name;

        var (dag, vertexData) = CreateDmxDagVertexData(dmeModel, name);
        dag.Shape!.Name = name;

        var vertexCount = grid.Positions.Length;
        var identity = Enumerable.Range(0, vertexCount).ToArray();

        vertexData.AddIndexedStream("position$0", grid.Positions, identity);
        vertexData.AddIndexedStream("normal$0", Enumerable.Repeat(Vector3.UnitZ, vertexCount).ToArray(), identity);
        vertexData.AddIndexedStream("texcoord$0", grid.Texcoords, identity);

        // Full paint set, matching BuildClothProxyMeshDmx - a grid missing friction/drag has nothing
        // damping its fall once goal_strength lets go, which is why an isolated cloth_grid (no
        // cloth_proxy back-solve driving it) looked like it "just falls".
        vertexData.AddIndexedStream("cloth_enable$0", grid.ClothEnable, identity);
        vertexData.AddIndexedStream("cloth_goal_strength_v2$0", grid.GoalStrength, identity);
        vertexData.AddIndexedStream("cloth_goal_damping$0", grid.GoalDamping, identity);
        vertexData.AddIndexedStream("cloth_collision_radius$0", grid.CollisionRadius, identity);
        vertexData.AddIndexedStream("cloth_ground_collision$0", Enumerable.Repeat(0f, vertexCount).ToArray(), identity);
        vertexData.AddIndexedStream("cloth_friction$0", grid.Friction, identity);
        vertexData.AddIndexedStream("cloth_drag$0", grid.Drag, identity);
        vertexData.AddIndexedStream("cloth_use_rods$0", Enumerable.Repeat(1f, vertexCount).ToArray(), identity);
        vertexData.AddIndexedStream("cloth_make_rods$0", Enumerable.Repeat(0.4f, vertexCount).ToArray(), identity);
        vertexData.AddIndexedStream("cloth_bend_stiffness$0", Enumerable.Repeat(0.2f, vertexCount).ToArray(), identity);

        // Case-insensitive bone-name resolution - see BuildClothProxyMeshDmx for why (compiled cloth control
        // node names do not always agree in case with the skeleton; an Ordinal miss silently drops the skin).
        var boneIndexByName = new Dictionary<string, int>(skeleton.Bones.Length * 2, StringComparer.OrdinalIgnoreCase);
        foreach (var bone in skeleton.Bones)
        {
            boneIndexByName.TryAdd(bone.Name, bone.Index);
            boneIndexByName.TryAdd(GetExportBoneName(bone), bone.Index);
        }

        const int JointCount = 4;
        var blendIndices = new int[vertexCount * JointCount];
        var blendWeights = new float[vertexCount * JointCount];
        for (var v = 0; v < vertexCount; v++)
        {
            var slot = 0;
            foreach (var (boneName, weight) in grid.SkinInfluences[v])
            {
                if (slot >= JointCount || !boneIndexByName.TryGetValue(boneName, out var bi))
                {
                    continue;
                }

                blendIndices[v * JointCount + slot] = bi;
                blendWeights[v * JointCount + slot] = weight;
                slot++;
            }
        }

        vertexData.JointCount = JointCount;
        vertexData.AddStream("blendindices$0", blendIndices);
        vertexData.AddStream("blendweights$0", blendWeights);

        var faceSet = new DmeFaceSet { Name = "cloth" };
        faceSet.Material.MaterialName = "cloth";
        if (dag.Shape is DmeMesh dmeMesh)
        {
            dmeMesh.FaceSets.Add(faceSet);
        }

        foreach (var face in grid.Faces)
        {
            foreach (var index in face)
            {
                faceSet.Faces.Add(index);
            }

            faceSet.Faces.Add(-1);
        }

        TieElementRoot(dmx, dmeModel);
        using var stream = new MemoryStream();
        dmx.Save(stream, "binary", 9);
        return stream.ToArray();
    }

    /// <summary>
    /// Adds morph (face flex) delta states to the DMX meshes and builds the flex controller setup.
    /// Each flex descriptor becomes a <see cref="DmeVertexDeltaData"/> holding the sparse per-vertex
    /// position deltas (only changed vertices), plus a parallel zero entry in the mesh's delta-state
    /// weight arrays. A <see cref="DmeCombinationOperator"/> is built with one input control per raw
    /// (non-combo) flex; <c>a__b</c> corrective delta states are left for the compiler to synthesise
    /// combination rules from. Returns the combination operator to attach to the DMX root, or null
    /// when the mesh has no usable morph data.
    /// </summary>
    private static DmeCombinationOperator? AddMorphDeltaStates(Morph morphData,
        List<(DmeMesh DmeMesh, int BaseVertex, int VertexCount, int GlobalOffset)> morphDrawCalls)
    {
        var flexData = morphData.GetFlexVertexData();
        if (flexData.Count == 0)
        {
            return null;
        }

        // Preserve the original flex descriptor order so delta-state indices line up with the
        // compiled morph descriptors.
        var flexNames = morphData.GetFlexDescriptors();

        var targetMeshes = new List<DmeMesh>();
        var rawControlNames = new List<string>();
        var seenControls = new HashSet<string>(StringComparer.Ordinal);

        foreach (var meshGroup in morphDrawCalls.GroupBy(static d => d.DmeMesh))
        {
            var dmeMesh = meshGroup.Key;
            var ranges = meshGroup.ToArray();
            var meshGotDelta = false;

            foreach (var morphName in flexNames)
            {
                if (string.IsNullOrEmpty(morphName) || !flexData.TryGetValue(morphName, out var deltas))
                {
                    continue;
                }

                var indices = new List<int>();
                var values = new List<Vector3>();

                foreach (var (_, baseVertex, vertexCount, globalOffset) in ranges)
                {
                    for (var v = 0; v < vertexCount; v++)
                    {
                        var srcIndex = globalOffset + v;
                        if (srcIndex >= deltas.Length)
                        {
                            break;
                        }

                        var delta = deltas[srcIndex];
                        if (delta == Vector3.Zero)
                        {
                            continue;
                        }

                        indices.Add(baseVertex + v);
                        values.Add(delta);
                    }
                }

                if (values.Count == 0)
                {
                    continue;
                }

                var deltaState = new DmeVertexDeltaData { Name = morphName };
                deltaState.AddIndexedStream("position$0", values.ToArray(), indices.ToArray());

                dmeMesh.DeltaStates.Add(deltaState);
                dmeMesh.DeltaStateWeights.Add(Vector2.Zero);
                dmeMesh.DeltaStateWeightsLagged.Add(Vector2.Zero);
                meshGotDelta = true;

                // Raw (non-combo) flexes get an input control so they are drivable/scrubbable.
                // Combo correctives ("a__b") are derived by the compiler from the constituent controls.
                if (!morphName.Contains("__", StringComparison.Ordinal) && seenControls.Add(morphName))
                {
                    rawControlNames.Add(morphName);
                }
            }

            if (meshGotDelta)
            {
                targetMeshes.Add(dmeMesh);
            }
        }

        if (rawControlNames.Count == 0 || targetMeshes.Count == 0)
        {
            return null;
        }

        var combinationOperator = new DmeCombinationOperator { Name = "combinationOperator" };

        // Original controller ranges, matched by name against the model's compiled flex controllers:
        // paired controllers (eyeDownAndUp, jawSideways, ...) are authored [-1, 1], not [0, 1].
        var controllerRanges = new Dictionary<string, (float Min, float Max)>(StringComparer.Ordinal);
        foreach (var controller in morphData.FlexControllers)
        {
            controllerRanges.TryAdd(controller.Name, (controller.Min, controller.Max));
        }

        // Reconstruct PAIRED input controls: an original [-1, 1] controller with no raw flex of its own
        // name drives one raw flex on the positive half and one on the negative half (eyeDownAndUp ->
        // eyeUp / eyeDown via min/max rules). Probing the compiled flex rules with the controller at
        // +max / at min recovers that mapping, letting the control round-trip with its original range
        // instead of two split 0..1 sliders.
        var pairedByControl = new List<(string Name, string NegativeRaw, string PositiveRaw, float Min, float Max)>();
        var pairedRaws = new HashSet<string>(StringComparer.Ordinal);

        if (morphData.FlexRules.Length > 0 && morphData.FlexControllers.Length > 0)
        {
            var rawControlSet = new HashSet<string>(rawControlNames, StringComparer.Ordinal);
            var probe = new float[morphData.FlexControllers.Length];

            List<string> DrivenRaws(int controllerIndex, float value)
            {
                Array.Clear(probe);
                probe[controllerIndex] = value;
                var driven = new List<string>();
                foreach (var rule in morphData.FlexRules)
                {
                    if (rule.FlexID < 0 || rule.FlexID >= flexNames.Count)
                    {
                        continue;
                    }

                    try
                    {
                        if (rule.Evaluate(probe) > 0.25f)
                        {
                            driven.Add(flexNames[rule.FlexID]);
                        }
                    }
                    catch (Exception e) when (e is InvalidOperationException or NotImplementedException)
                    {
                        // Unsupported flex op - skip this rule.
                    }
                }

                return driven;
            }

            for (var i = 0; i < morphData.FlexControllers.Length; i++)
            {
                var controller = morphData.FlexControllers[i];
                if (controller.Min >= 0f || rawControlSet.Contains(controller.Name))
                {
                    continue;
                }

                var positive = DrivenRaws(i, controller.Max).Where(rawControlSet.Contains).Except(pairedRaws).ToList();
                var negative = DrivenRaws(i, controller.Min).Where(rawControlSet.Contains).Except(pairedRaws).ToList();

                if (positive.Count == 1 && negative.Count == 1 && positive[0] != negative[0])
                {
                    pairedByControl.Add((controller.Name, negative[0], positive[0], controller.Min, controller.Max));
                    pairedRaws.Add(positive[0]);
                    pairedRaws.Add(negative[0]);
                }
            }
        }

        void AddControlValueSlots()
        {
            combinationOperator.ControlValues.Add(new Vector3(0f, 0f, 0.5f));
            combinationOperator.ControlValuesLagged.Add(new Vector3(0f, 0f, 0.5f));
        }

        foreach (var (pairName, negativeRaw, positiveRaw, flexMin, flexMax) in pairedByControl)
        {
            var control = new DmeCombinationInputControl { Name = pairName, FlexMin = flexMin, FlexMax = flexMax };
            control.RawControlNames.Add(negativeRaw);
            control.RawControlNames.Add(positiveRaw);
            control.WrinkleScales.Add(0f);
            control.WrinkleScales.Add(0f);

            combinationOperator.Controls.Add(control);
            AddControlValueSlots();
        }

        // Single-raw controls are NOT emitted: the compiler creates them implicitly from the delta
        // states themselves (verified: stripping all 21 of them recompiles a byte-identical rule set),
        // and an explicit control stamps its name into ModelDoc's otherwise-empty morph rule display.
        // Controls whose ORIGINAL controller has a non-default range still need an explicit entry.
        foreach (var controlName in rawControlNames)
        {
            if (pairedRaws.Contains(controlName))
            {
                continue;
            }

            if (!controllerRanges.TryGetValue(controlName, out var range) || range == (0f, 1f))
            {
                continue;
            }

            var control = new DmeCombinationInputControl { Name = controlName, FlexMin = range.Min, FlexMax = range.Max };
            control.RawControlNames.Add(controlName);
            control.WrinkleScales.Add(0f);

            combinationOperator.Controls.Add(control);
            AddControlValueSlots();
        }

        foreach (var dmeMesh in targetMeshes)
        {
            combinationOperator.Targets.Add(dmeMesh);
        }

        return combinationOperator;
    }

    /// <summary>
    /// Converts a physics hull descriptor to DMX format.
    /// </summary>
    public byte[] ToDmxMesh(HullDescriptor hull)
    {
        var uniformSurface = PhysicsSurfaceNames[hull.SurfacePropertyIndex];
        var uniformCollisionTags = PhysicsCollisionTags[hull.CollisionAttributeIndex];
        // https://github.com/ValveResourceFormat/ValveResourceFormat/issues/660#issuecomment-1795499191
        var fixRenderMeshCompileCrash = Type == ModelExtractType.Map_PhysicsToRenderMesh;
        return ToDmxMesh(hull.Shape, hull.UserFriendlyName ?? "hull", uniformSurface, uniformCollisionTags, fixRenderMeshCompileCrash);
    }

    /// <summary>
    /// Converts a physics mesh descriptor to DMX format.
    /// </summary>
    public byte[] ToDmxMesh(MeshDescriptor mesh)
    {
        var uniformSurface = PhysicsSurfaceNames[mesh.SurfacePropertyIndex];
        var uniformCollisionTags = PhysicsCollisionTags[mesh.CollisionAttributeIndex];
        var fixRenderMeshCompileCrash = Type == ModelExtractType.Map_PhysicsToRenderMesh;
        return ToDmxMesh(mesh.Shape, mesh.UserFriendlyName ?? "mesh", uniformSurface, uniformCollisionTags, PhysicsSurfaceNames, fixRenderMeshCompileCrash);
    }

    /// <summary>
    /// Converts a Rubikon hull shape to DMX mesh format.
    /// </summary>
    public static byte[] ToDmxMesh(RnShapes.Hull hull, string name,
        string uniformSurface,
        HashSet<string> uniformCollisionTags,
        bool appendVertexNormalStream = false)
    {
        using var dmx = new Datamodel.Datamodel("model", 22);
        DmxModelBaseLayout(name, out var dmeModel, out var dag, out var vertexData);

        // n-gon face set
        var faceSet = new DmeFaceSet() { Name = "hull faces" };
        faceSet.Material.MaterialName = new SurfaceTagCombo(uniformSurface, uniformCollisionTags).StringMaterial;
        if (dag.Shape is DmeMesh dmeMesh)
        {
            dmeMesh.FaceSets.Add(faceSet);
        }

        var edges = hull.GetEdges();
        var faces = hull.GetFaces();
        var vertexPositions = hull.GetVertexPositions().ToArray();

        Debug.Assert(faces.Length + vertexPositions.Length == (edges.Length / 2) + 2);

        foreach (var face in faces)
        {
            var startEdge = face.Edge;
            var currentEdge = startEdge;
            do
            {
                var e = edges[currentEdge];
                faceSet.Faces.Add(e.Origin);
                currentEdge = e.Next;
            }
            while (currentEdge != startEdge);

            faceSet.Faces.Add(-1);
        }

        var indices = Enumerable.Range(0, vertexPositions.Length * 3).ToArray();
        vertexData.AddIndexedStream("position$0", vertexPositions, indices);

        if (appendVertexNormalStream)
        {
            vertexData.AddIndexedStream("normal$0", Enumerable.Repeat(new Vector3(0, 0, 0), vertexPositions.Length).ToArray(), indices);
        }

        TieElementRoot(dmx, dmeModel);
        using var stream = new MemoryStream();
        dmx.Save(stream, "binary", 9);

        return stream.ToArray();
    }

    /// <summary>
    /// Converts a Rubikon mesh shape to DMX mesh format.
    /// </summary>
    public static byte[] ToDmxMesh(RnShapes.Mesh mesh, string name,
        string uniformSurface,
        HashSet<string> uniformCollisionTags,
        string[] surfaceList,
        bool appendVertexNormalStream = false)
    {
        using var dmx = new Datamodel.Datamodel("model", 22);
        DmxModelBaseLayout(name, out var dmeModel, out var dag, out var vertexData);

        var triangles = mesh.GetTriangles();

        if (mesh.Materials.Length == 0)
        {
            var materialName = new SurfaceTagCombo(uniformSurface, uniformCollisionTags).StringMaterial;
            GenerateTriangleFaceSet(dag, 0, triangles.Length, materialName);
        }
        else if (dag.Shape is DmeMesh dmeMesh)
        {
            Debug.Assert(mesh.Materials.Length == triangles.Length);
            Debug.Assert(surfaceList.Length > 0);

            Span<DmeFaceSet> faceSets = new DmeFaceSet[surfaceList.Length];
            for (var t = 0; t < mesh.Materials.Length; t++)
            {
                var surfaceIndex = mesh.Materials[t];
                var faceSet = faceSets[surfaceIndex];

                if (faceSet == null)
                {
                    var surface = surfaceList[surfaceIndex];
                    faceSet = faceSets[surfaceIndex] = new DmeFaceSet()
                    {
                        Name = surface + '$' + surfaceIndex
                    };
                    faceSet.Material.MaterialName = new SurfaceTagCombo(surface, uniformCollisionTags).StringMaterial;
                    dmeMesh.FaceSets.Add(faceSet);
                }

                faceSet.Faces.Add(t * 3);
                faceSet.Faces.Add(t * 3 + 1);
                faceSet.Faces.Add(t * 3 + 2);
                faceSet.Faces.Add(-1);
            }
        }

        var indices = new int[triangles.Length * 3];
        for (var t = 0; t < triangles.Length; t++)
        {
            var triangle = triangles[t];
            indices[t * 3] = triangle.X;
            indices[t * 3 + 1] = triangle.Y;
            indices[t * 3 + 2] = triangle.Z;
        }

        var vertices = mesh.GetVertices().ToArray();

        vertexData.AddIndexedStream("position$0", vertices, indices);

        if (appendVertexNormalStream)
        {
            vertexData.AddIndexedStream("normal$0", Enumerable.Repeat(new Vector3(0, 0, 0), vertices.Length).ToArray(), indices);
        }

        TieElementRoot(dmx, dmeModel);
        using var stream = new MemoryStream();
        dmx.Save(stream, "binary", 9);

        return stream.ToArray();
    }

    private static void DmxModelBaseLayout(string name, out DmeModel dmeModel, out DmeDag dag, out DmeVertexData vertexData)
    {
        DmxModelMultiVertexBufferLayout(name, 1, out dmeModel, out var dags, out var dmeVertexBuffers);
        dag = dags[0];
        vertexData = dmeVertexBuffers[0];
    }

    private static void DmxModelMultiVertexBufferLayout(string name, int vertexBufferCount,
        out DmeModel dmeModel, out DmeDag[] dags, out DmeVertexData[] dmeVertexBuffers)
    {
        dmeModel = new DmeModel() { Name = name };
        dags = new DmeDag[vertexBufferCount];
        dmeVertexBuffers = new DmeVertexData[vertexBufferCount];

        for (var i = 0; i < vertexBufferCount; i++)
        {
            (dags[i], dmeVertexBuffers[i]) = CreateDmxDagVertexData(dmeModel, name);
        }
    }

    private static DmeDag CreateDmxDag(DmeModel dmeModel, DmeVertexData vertexData, string name)
    {
        var dag = new DmeDag() { Name = name };
        dmeModel.Children.Add(dag);
        dmeModel.JointList.Add(dag);

        var transformList = new DmeTransformsList();
        transformList.Transforms.Add(new DmeTransform());
        dmeModel.BaseStates.Add(transformList);

        var shape = new DmeMesh
        {
            Name = name,
            // bindState is the rest pose that morph (flex) delta states are applied on top of. Valve's
            // authored morph meshes set bindState == currentState == baseStates[0] (the 'bind' vertex
            // data); without bindState ModelDoc's flex editor has no base to add the weighted deltas to,
            // so dragging a flex slider does not deform the mesh even though the compiled MRPH is exact.
            BindState = vertexData,
            CurrentState = vertexData
        };
        shape.BaseStates.Add(vertexData);
        dag.Shape = shape;

        return dag;
    }

    private static (DmeDag, DmeVertexData) CreateDmxDagVertexData(DmeModel dmeModel, string name)
    {
        // dmx requires one dag per vertex buffer. Compiled UVs are exported verbatim with
        // flipVCoordinates=false: the compiler round-trips them correctly (a V-mirrored look in
        // Blender is that importer's convention, not an export defect).
        var vertexData = new DmeVertexData { Name = "bind" };
        var dag = CreateDmxDag(dmeModel, vertexData, name);

        return (dag, vertexData);
    }

    private static void GenerateTriangleFaceSet(DmeDag dag, int triangleStart, int triangleEnd, string material)
    {
        var faceSet = new DmeFaceSet() { Name = triangleStart + "-" + triangleEnd };
        if (dag.Shape is DmeMesh dmeMesh)
        {
            dmeMesh.FaceSets.Add(faceSet);
        }

        for (var i = triangleStart; i < triangleEnd; i++)
        {
            faceSet.Faces.Add(i * 3);
            faceSet.Faces.Add(i * 3 + 1);
            faceSet.Faces.Add(i * 3 + 2);
            faceSet.Faces.Add(-1);
        }

        faceSet.Material.MaterialName = material;
    }

    private static void GenerateTriangleFaceSetFromIndexBuffer(DmeDag dag, ReadOnlySpan<int> indices, int baseVertex,
        string material, string name)
    {
        var faceSet = new DmeFaceSet() { Name = name };
        if (dag.Shape is DmeMesh dmeMesh)
        {
            dmeMesh.FaceSets.Add(faceSet);
        }

        for (var i = 0; i < indices.Length; i += 3)
        {
            faceSet.Faces.Add(baseVertex + indices[i]);
            faceSet.Faces.Add(baseVertex + indices[i + 1]);
            faceSet.Faces.Add(baseVertex + indices[i + 2]);
            faceSet.Faces.Add(-1);
        }

        faceSet.Material.MaterialName = material;
    }

    private static void TieElementRoot(Datamodel.Datamodel dmx, DmeModel dmeModel, DmeCombinationOperator? combinationOperator = null)
    {
        var root = new Element(dmx, "root", null, "DmElement")
        {
            ["skeleton"] = dmeModel,
            ["model"] = dmeModel,
            ["exportTags"] = new Element(dmx, "exportTags", null, "DmeExportTags")
            {
                ["source"] = $"Generated with {StringToken.VRF_GENERATOR}",
            }
        };

        // The flex controller setup hangs off the root so ModelDoc/the compiler can build the
        // MRPH flex controllers and rules from the mesh delta states.
        if (combinationOperator != null)
        {
            root["combinationOperator"] = combinationOperator;
        }

        dmx.Root = root;
    }
}
