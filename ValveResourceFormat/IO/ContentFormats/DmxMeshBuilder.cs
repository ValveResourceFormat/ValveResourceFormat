using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using ValveResourceFormat.Blocks;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.ResourceTypes.ModelAnimation;
using ValveKeyValue;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.IO.ContentFormats.DmxModel;

/// <summary>
/// Everything one mesh-to-datamodel conversion needs beyond the mesh itself.
/// </summary>
internal readonly record struct DmxMeshBuildOptions
{
    /// <summary>Split draw calls into sub-meshes named draw0, draw1, draw2...</summary>
    public bool SplitDrawCallsIntoSeparateSubmeshes { get; init; }

    /// <summary>
    /// When set together with <see cref="SplitDrawCallsIntoSeparateSubmeshes"/>, receives each sub-mesh
    /// paired with the draw call it was made from, in draw call order.
    /// </summary>
    public List<(DmeDag Dag, KVObject DrawCall)>? SubmeshDrawCalls { get; init; }

    /// <summary>Pre-parsed input signatures used to map DirectX semantic names to engine semantic names.</summary>
    public Dictionary<string, Material.VsInputSignature>? MaterialInputSignatures { get; init; }

    /// <summary>Remap table for the mesh bone indices.</summary>
    public int[]? BoneRemapTable { get; init; }

    /// <summary>
    /// The skeleton dag the mesh's BLENDINDICES reference, already built. When provided it becomes the
    /// datamodel's model element.
    /// </summary>
    public DmeModel? SkeletonRoot { get; init; }

    /// <summary>
    /// The skeleton the mesh's BLENDINDICES reference, which names the cloth proxy bones a compiled cloth
    /// generated.
    /// </summary>
    public Skeleton? Skeleton { get; init; }
}

/// <summary>
/// Writes a compiled mesh and its morph targets back out as a datamodel: one dme mesh per vertex
/// buffer, each draw call a face set of it, and the flex controllers as a combination operator.
/// </summary>
internal static class DmxMeshBuilder
{
    /// <summary>
    /// Engine semantics a vertex buffer can carry under their own name, in the spelling the model compiler reads.
    /// </summary>
    private static readonly string[] EngineSemantics = ["VertexPaintBlendParams", "VertexPaintTintColor"];

    /// <summary>
    /// The values the streams of one vertex data element are decoded against: the input signature of the
    /// first material its draw calls use that has one, and the mesh's skinning.
    /// </summary>
    private readonly record struct VertexStreams(Material.VsInputSignature MaterialInputSignature, int BoneWeightCount, int[]? BoneRemapTable,
        bool[]? ClothBones = null, int[]? ClothCompaction = null);

    /// <summary>
    /// A mesh's vertex buffers concatenated into one, the vertex each original buffer starts at, and the
    /// matching tools buffers concatenated the same way.
    /// </summary>
    private readonly record struct MergedVertexBuffers(VBIB.OnDiskBufferData Buffer, Dictionary<int, int> VertexOffsets, VBIB.OnDiskBufferData? MergedToolsBuffer);

    /// <summary>
    /// Concatenates the vertex buffers of a mesh when every draw call reads a single buffer and all of
    /// them share one layout. Returns null when they cannot be merged.
    /// </summary>
    private static MergedVertexBuffers? TryMergeVertexBuffers(KVObject mdat, VBIB mbuf, ToolsBufferMatcher toolsBuffers)
    {
        var usedBuffers = new List<int>();

        foreach (var sceneObject in mdat.GetArray("m_sceneObjects"))
        {
            foreach (var drawCall in sceneObject.GetArray("m_drawCalls"))
            {
                var vertexBuffers = drawCall.GetArray("m_vertexBuffers");

                if (vertexBuffers.Count != 1)
                {
                    return null;
                }

                var bufferIndex = vertexBuffers[0].GetInt32Property("m_hBuffer");

                if (!usedBuffers.Contains(bufferIndex))
                {
                    usedBuffers.Add(bufferIndex);
                }
            }
        }

        if (usedBuffers.Count < 2)
        {
            return null;
        }

        var buffers = usedBuffers.ConvertAll(index => mbuf.VertexBuffers[index]);

        if (buffers.Exists(buffer => !VBIB.HasSameLayout(buffers[0], buffer)))
        {
            return null;
        }

        var offsets = new Dictionary<int, int>(usedBuffers.Count);
        var vertexOffset = 0u;

        for (var i = 0; i < usedBuffers.Count; i++)
        {
            offsets[usedBuffers[i]] = (int)vertexOffset;
            vertexOffset += buffers[i].ElementCount;
        }

        return new MergedVertexBuffers(VBIB.Concatenate(buffers), offsets, toolsBuffers.TryClaimMerged(buffers));
    }

    /// <summary>
    /// Fills a render vertex buffer's streams, then those of every tools buffer that augments it. The
    /// render buffer is filled first and wins a name collision; see <see cref="FillDatamodelVertexData"/>.
    /// </summary>
    private static void FillBufferAndItsToolsBuffers(VBIB.OnDiskBufferData vertexBuffer, DmeVertexData vertexData,
        in VertexStreams streams, ToolsBufferMatcher toolsBuffers, List<int>? clothTriangles = null)
    {
        FillDatamodelVertexData(vertexBuffer, vertexData, streams, clothTriangles: clothTriangles);

        while (toolsBuffers.TryClaim(vertexBuffer.ElementCount) is { } found)
        {
            FillDatamodelVertexData(found, vertexData, streams, isToolsBuffer: true);
        }
    }

    /// <summary>
    /// Fills a datamodel vertex data element with the streams of a vertex buffer, either a mesh's render
    /// buffer or one of its tools buffers. A render buffer's vertex paint attributes take their engine name
    /// from the material input signature, while a tools buffer's attributes keep their own names. An
    /// attribute whose stream name is already present in <paramref name="vertexData"/>, ignoring case, is
    /// left alone.
    /// </summary>
    private static void FillDatamodelVertexData(VBIB.OnDiskBufferData vertexBuffer, DmeVertexData vertexData,
        in VertexStreams streams, bool isToolsBuffer = false, List<int>? clothTriangles = null)
    {
        var indices = Enumerable.Range(0, (int)vertexBuffer.ElementCount).ToArray(); // May break with non-unit strides, non-tri faces

        var boneWeightCount = streams.BoneWeightCount;
        var boneArrayComponents = boneWeightCount > 4 ? 8 : 4;
        int[]? clothBlendIndices = null;
        float[]? clothBlendWeights = null;

        foreach (var attribute in vertexBuffer.InputLayoutFields)
        {
            var attributeFormat = VBIB.GetFormatInfo(attribute);
            var semantic = GetStreamName(attribute);

            if (attribute.SemanticName is "NORMAL")
            {
                if (HasStream(vertexData, semantic))
                {
                    continue;
                }

                var (normals, tangents) = VBIB.GetNormalTangentArray(vertexBuffer, attribute);
                vertexData.AddIndexedStream(semantic, normals, indices);

                var tangentSemantic = "tangent$" + attribute.SemanticIndex;

                if (tangents.Length > 0 && !HasStream(vertexData, tangentSemantic))
                {
                    vertexData.AddIndexedStream(tangentSemantic, tangents, indices);
                }

                continue;
            }
            else if (attribute.SemanticName is "BLENDINDICES")
            {
                vertexData.JointCount = boneWeightCount;

                // An unskinned mesh can still carry the attribute, with indices that reference nothing.
                if (boneWeightCount == 0 || HasStream(vertexData, semantic))
                {
                    continue;
                }

                var boneIndices = VBIB.GetBlendIndicesArray(vertexBuffer, attribute, streams.BoneRemapTable);
                var compactedLength = boneIndices.Length / boneArrayComponents * boneWeightCount;

                var compactIndices = new int[compactedLength];
                for (var i = 0; i < boneIndices.Length; i += boneArrayComponents)
                {
                    for (var j = 0; j < boneWeightCount; j++)
                    {
                        compactIndices[i / boneArrayComponents * boneWeightCount + j] = boneIndices[i + j];
                    }
                }

                clothBlendIndices = (int[])compactIndices.Clone();

                if (streams.ClothCompaction != null)
                {
                    for (var i = 0; i < compactIndices.Length; i++)
                    {
                        compactIndices[i] = ModelExtract.CompactBoneIndex(streams.ClothCompaction, compactIndices[i]);
                    }
                }

                vertexData.AddStream(semantic, compactIndices);
                continue;
            }
            else if (attribute.SemanticName is "BLENDWEIGHT" or "BLENDWEIGHTS")
            {
                var weightsSemantic = "blendweights$" + attribute.SemanticIndex;

                if (boneWeightCount == 0 || HasStream(vertexData, weightsSemantic))
                {
                    continue;
                }

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

                clothBlendWeights = compactWeights;
                vertexData.AddStream(weightsSemantic, compactWeights);
                continue;
            }

            if (!isToolsBuffer && streams.MaterialInputSignature.Elements is { Length: > 0 })
            {
                var insgElement = Material.FindD3DInputSignatureElement(streams.MaterialInputSignature, attribute.SemanticName, attribute.SemanticIndex);

                // Use engine semantics for attributes that need them
                if (EngineSemantics.Contains(insgElement.Semantic))
                {
                    semantic = insgElement.Semantic + "$0";
                }
            }

            if (HasStream(vertexData, semantic))
            {
                continue;
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

        if (HasStream(vertexData, "blendindices$0") && !HasStream(vertexData, "blendweights$0"))
        {
            if (!vertexData.TryGetValue("blendindices$0", out var blendIndices) || blendIndices is not ICollection<int> collection)
            {
                throw new InvalidOperationException("blendindices$0 stream not found");
            }

            vertexData.AddStream("blendweights$0", Enumerable.Repeat(1f, collection.Count).ToArray());
        }

        if (!isToolsBuffer)
        {
            AddClothEnablePaint(vertexData, indices, boneWeightCount, streams.ClothBones, clothBlendIndices, clothBlendWeights, clothTriangles);
        }
    }

    /// <summary>
    /// Writes the <c>cloth_enable</c> vertex paint a render mesh needs for its cloth to be rebuilt.
    /// </summary>
    /// <remarks>
    /// The compiler does not read a render mesh's cloth skinning back: it regenerates the
    /// <c>$cloth_m&lt;mesh&gt;p&lt;point&gt;</c> bones from the proxy and re-binds the mesh to them
    /// itself, on the vertices this paint marks. Without the paint no vertex is re-bound, so the
    /// bones are never added and the FeModel's control nodes resolve to nothing. The compiled model
    /// no longer carries the paint - it carries the result - so it is reconstructed here as the
    /// weight each vertex holds on the generated cloth bones, which is what the compiler produced
    /// from the paint in the first place.
    /// </remarks>
    private static void AddClothEnablePaint(DmeVertexData vertexData, int[] indices, int boneWeightCount,
        bool[]? clothBones, int[]? blendIndices, float[]? blendWeights, List<int>? triangles)
    {
        if (clothBones == null || blendIndices == null || blendWeights == null || boneWeightCount <= 0
            || vertexData.VertexFormat.Contains("cloth_enable$0"))
        {
            return;
        }

        var vertexCount = Math.Min(indices.Length, blendIndices.Length / boneWeightCount);
        var paint = new float[indices.Length];
        var painted = 0;

        for (var vertex = 0; vertex < vertexCount; vertex++)
        {
            var total = 0f;

            for (var slot = vertex * boneWeightCount; slot < (vertex + 1) * boneWeightCount; slot++)
            {
                var bone = blendIndices[slot];

                if (bone >= 0 && bone < clothBones.Length && clothBones[bone] && slot < blendWeights.Length)
                {
                    total += blendWeights[slot];
                }
            }

            paint[vertex] = Math.Clamp(total, 0f, 1f);

            if (paint[vertex] > 0f)
            {
                painted++;
            }
        }

        if (painted == 0)
        {
            return;
        }

        vertexData.AddIndexedStream("cloth_enable$0", paint, indices);
    }

    /// <summary>Marks the bones a compiled cloth proxy generated, by skeleton bone index.</summary>
    private static bool[]? BuildClothBoneMask(Skeleton? skeleton)
    {
        if (skeleton == null)
        {
            return null;
        }

        var mask = new bool[skeleton.Bones.Length];
        var any = false;

        foreach (var bone in skeleton.Bones)
        {
            if (bone.IsProceduralCloth && bone.Name.StartsWith('$'))
            {
                mask[bone.Index] = true;
                any = true;
            }
        }

        return any ? mask : null;
    }

    /// <summary>
    /// Gives a mesh the normal and texture coordinate streams the model compiler requires.
    /// </summary>
    private static void AddCompilerRequiredStreams(DmeVertexData vertexData, int elementCount)
    {
        var indices = Enumerable.Range(0, elementCount).ToArray();

        if (!HasStream(vertexData, "normal$0"))
        {
            vertexData.AddIndexedStream("normal$0", Enumerable.Repeat(Vector3.UnitZ, elementCount).ToArray(), indices);
        }

        if (!HasStream(vertexData, "texcoord$0"))
        {
            vertexData.AddIndexedStream("texcoord$0", Enumerable.Repeat(Vector2.Zero, elementCount).ToArray(), indices);
        }
    }

    /// <summary>
    /// Names the stream of a vertex attribute: an engine semantic in its engine spelling, anything else in lower case.
    /// </summary>
    private static string GetStreamName(VBIB.RenderInputLayoutField attribute)
    {
        var name = Array.Find(EngineSemantics, engineSemantic => engineSemantic.Equals(attribute.SemanticName, StringComparison.OrdinalIgnoreCase))
            ?? attribute.SemanticName.ToLowerInvariant();

        return name + "$" + attribute.SemanticIndex;
    }

    /// <summary>
    /// Determines whether a vertex data element already has a stream of the given name. The model compiler
    /// treats stream names that differ only in case as the same stream.
    /// </summary>
    private static bool HasStream(DmeVertexData vertexData, string name)
        => vertexData.VertexFormat.Contains(name, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Converts a mesh to a datamodel mesh representation.
    /// </summary>
    public static Datamodel.Datamodel Build(Mesh mesh, string name, DmxMeshBuildOptions options)
    {
        var mdat = mesh.Data;
        var mbuf = mesh.VBIB;
        var indexBuffers = mbuf.IndexBuffers.Select(ib => new Lazy<int[]>(() => GltfModelExporter.ReadIndices(ib, 0, (int)ib.ElementCount, 0))).ToArray();

        var datamodel = new Datamodel.Datamodel("model", 22);
        var dmeModel = new DmeModel() { Name = name };
        var dmeVertexBuffers = new Dictionary<(int, int), (DmeDag Dag, DmeVertexData VertexData)>(mbuf.VertexBuffers.Count);

        // Populate the joint list with bones up-front so DMX BLENDINDICES line up with Bone.Index.
        if (options.SkeletonRoot is { } skeletonRoot)
        {
            dmeModel = skeletonRoot;
            dmeModel.Name = name;
        }

        var inputSignatures = new Dictionary<(int, int), Material.VsInputSignature>(mbuf.VertexBuffers.Count);
        var clothBones = BuildClothBoneMask(options.Skeleton);
        var clothCompaction = options.Skeleton != null && clothBones != null
            ? ModelExtract.BuildClothBoneCompaction(options.Skeleton)
            : null;
        var clothTriangles = new Dictionary<(int, int), List<int>>();
        var drawCallIndex = 0;

        // Draw calls sitting in separate but identically laid out vertex buffers are one mesh in the
        // source art, concatenated back into one with the draw calls as its face sets.
        var toolsBuffers = new ToolsBufferMatcher(mbuf);
        var merged = TryMergeVertexBuffers(mdat, mbuf, toolsBuffers);

        var morphVertexOffsets = new Dictionary<(int, int), int>(mbuf.VertexBuffers.Count);
        var morphVertexOffset = 0;

        foreach (var sceneObject in mdat.GetArray("m_sceneObjects"))
        {
            foreach (var drawCall in sceneObject.GetArray("m_drawCalls"))
            {
                var vertexBuffers = drawCall.GetArray("m_vertexBuffers");

                Debug.Assert(vertexBuffers.Count <= 2); // Hello traveler, if you are here to update this code to support more than 2 buffers!

                var bufferIndex = vertexBuffers[0].GetInt32Property("m_hBuffer");

                var dmeVertexBufferKey = merged != null
                    ? (0, -1)
                    : (bufferIndex, vertexBuffers.Count > 1 ? vertexBuffers[1].GetInt32Property("m_hBuffer") : -1);

                if (!dmeVertexBuffers.TryGetValue(dmeVertexBufferKey, out var dmeVertexBuffer))
                {
                    dmeVertexBuffer = DmxScaffolding.CreateDagVertexData(dmeModel, name);
                    dmeVertexBuffers[dmeVertexBufferKey] = dmeVertexBuffer;
                    morphVertexOffsets[dmeVertexBufferKey] = morphVertexOffset;
                }

                var mergedVertexOffset = merged?.VertexOffsets[bufferIndex] ?? 0;
                morphVertexOffset += drawCall.GetInt32Property("m_nVertexCount");

                var indexBufferInfo = drawCall.GetSubCollection("m_indexBuffer");
                var indexBufferIndex = indexBufferInfo.GetInt32Property("m_hBuffer");
                ReadOnlySpan<int> indexBuffer = indexBuffers[indexBufferIndex].Value;

                var material = Mesh.GetMaterialName(drawCall);

                if (material != null && options.MaterialInputSignatures != null
                    && inputSignatures.GetValueOrDefault(dmeVertexBufferKey).Elements is not { Length: > 0 })
                {
                    inputSignatures[dmeVertexBufferKey] = options.MaterialInputSignatures.GetValueOrDefault(material, Material.VsInputSignature.Empty);
                }

                if (material == null && Mesh.IsOccluder(drawCall))
                {
                    material = "materials/tools/toolsoccluder.vmat";
                }

                material ??= "materials/default.vmat";

                var baseVertex = drawCall.GetInt32Property("m_nBaseVertex") + mergedVertexOffset;
                var startIndex = drawCall.GetInt32Property("m_nStartIndex");
                var indexCount = drawCall.GetInt32Property("m_nIndexCount");

                var dag = dmeVertexBuffer.Dag;

                if (options.SplitDrawCallsIntoSeparateSubmeshes)
                {
                    var subMeshName = "draw" + drawCallIndex;

                    if (drawCallIndex > 0)
                    {
                        // new submesh with same vertex buffer as first submesh
                        dag = DmxScaffolding.CreateDag(dmeModel, dmeVertexBuffer.VertexData, subMeshName);
                    }

                    dag.Shape!.Name = subMeshName;
                    options.SubmeshDrawCalls?.Add((dag, drawCall));
                }

                var drawCallIndices = indexBuffer[startIndex..(startIndex + indexCount)];

                DmxScaffolding.TriangleFaceSetFromIndexBuffer(
                    dag,
                    drawCallIndices,
                    baseVertex,
                    material,
                    $"{startIndex}..{startIndex + indexCount}"
                );

                if (clothBones != null)
                {
                    if (!clothTriangles.TryGetValue(dmeVertexBufferKey, out var triangles))
                    {
                        triangles = [];
                        clothTriangles[dmeVertexBufferKey] = triangles;
                    }

                    foreach (var index in drawCallIndices)
                    {
                        triangles.Add(baseVertex + index);
                    }
                }

                drawCallIndex++;
            }
        }

        foreach (var (vertexBufferIndices, dmeObjects) in dmeVertexBuffers)
        {
            var streams = new VertexStreams(inputSignatures.GetValueOrDefault(vertexBufferIndices, Material.VsInputSignature.Empty),
                mesh.BoneWeightCount, options.BoneRemapTable, clothBones, clothCompaction);

            clothTriangles.TryGetValue(vertexBufferIndices, out var triangles);

            if (merged != null)
            {
                FillDatamodelVertexData(merged.Value.Buffer, dmeObjects.VertexData, streams, clothTriangles: triangles);

                if (merged.Value.MergedToolsBuffer is { } mergedToolsBuffer)
                {
                    FillDatamodelVertexData(mergedToolsBuffer, dmeObjects.VertexData, streams, isToolsBuffer: true);
                }

                AddCompilerRequiredStreams(dmeObjects.VertexData, (int)merged.Value.Buffer.ElementCount);
                continue;
            }

            FillBufferAndItsToolsBuffers(mbuf.VertexBuffers[vertexBufferIndices.Item1], dmeObjects.VertexData, streams, toolsBuffers, triangles);

            if (vertexBufferIndices.Item2 != -1)
            {
                FillBufferAndItsToolsBuffers(mbuf.VertexBuffers[vertexBufferIndices.Item2], dmeObjects.VertexData, streams, toolsBuffers, triangles);
            }

            AddCompilerRequiredStreams(dmeObjects.VertexData, (int)mbuf.VertexBuffers[vertexBufferIndices.Item1].ElementCount);
        }

        DmxScaffolding.TieElementRoot(datamodel, dmeModel);

        if (mesh.MorphData != null)
        {
            var morphTargets = dmeVertexBuffers
                .Select(pair => ((DmeMesh)pair.Value.Dag.Shape!, morphVertexOffsets[pair.Key],
                    (int)(merged?.Buffer.ElementCount ?? mbuf.VertexBuffers[pair.Key.Item1].ElementCount)))
                .ToList();

            AddMorphData(datamodel, mesh.MorphData, morphTargets);
        }

        return datamodel;
    }

    /// <summary>
    /// Writes the morph targets of a mesh as delta states, and the flex controllers that drive them as
    /// a combination operator. ModelDoc derives the compiled flex rules from this.
    /// </summary>
    private static void AddMorphData(Datamodel.Datamodel datamodel, Morph morph,
        List<(DmeMesh Mesh, int BaseVertex, int VertexCount)> targets)
    {
        var flexNames = morph.GetFlexDescriptors();
        if (flexNames.Count == 0 || targets.Count == 0)
        {
            return;
        }

        var positionData = morph.GetFlexVertexData(MorphBundleType.PositionSpeed);
        var normalData = morph.GetFlexVertexData(MorphBundleType.NormalWrinkle);
        var coverage = morph.GetFlexVertexCoverage();
        var recovery = new FlexRecovery(morph);

        var combination = new DmeCombinationOperator { Name = "combinationOperator" };

        foreach (var (dmeMesh, baseVertex, vertexCount) in targets)
        {
            foreach (var flexName in flexNames)
            {
                // A morph target with no deltas at all still needs its delta state.
                positionData.TryGetValue(flexName, out var deltas);
                deltas ??= [];

                normalData.TryGetValue(flexName, out var normalDeltas);
                coverage.TryGetValue(flexName, out var covered);

                var positions = new List<Vector3>();
                var positionIndices = new List<int>();
                var normals = new List<Vector3>();
                var normalIndices = new List<int>();
                var wrinkles = new List<float>();
                var wrinkleIndices = new List<int>();

                for (var i = 0; i < vertexCount; i++)
                {
                    var vertexId = baseVertex + i;
                    if (vertexId >= deltas.Length)
                    {
                        break;
                    }

                    var inRect = covered == null || (vertexId < covered.Length && covered[vertexId]);
                    var delta = deltas[vertexId];

                    if (inRect || delta.X != 0f || delta.Y != 0f || delta.Z != 0f)
                    {
                        positions.Add(new Vector3(delta.X, delta.Y, delta.Z));
                        positionIndices.Add(i);
                    }

                    if (normalDeltas == null || vertexId >= normalDeltas.Length)
                    {
                        continue;
                    }

                    var normal = normalDeltas[vertexId];

                    if (inRect || normal.X != 0f || normal.Y != 0f || normal.Z != 0f)
                    {
                        normals.Add(new Vector3(normal.X, normal.Y, normal.Z));
                        normalIndices.Add(i);
                    }

                    if (normal.W != 0f)
                    {
                        wrinkles.Add(normal.W);
                        wrinkleIndices.Add(i);
                    }
                }

                // A morph target that carries no geometry still has to look like one.
                if (positions.Count == 0 && vertexCount > 0)
                {
                    positions.Add(Vector3.Zero);
                    positionIndices.Add(0);
                }

                var deltaState = new DmeVertexDeltaData { Name = FlexRecovery.Identifier(flexName) };
                deltaState.AddIndexedStream("position$0", positions.ToArray(), positionIndices.ToArray());

                if (normals.Count > 0)
                {
                    deltaState.AddIndexedStream("normal$0", normals.ToArray(), normalIndices.ToArray());
                }

                if (wrinkles.Count > 0)
                {
                    deltaState.AddIndexedStream("wrinkle$0", wrinkles.ToArray(), wrinkleIndices.ToArray());
                }

                dmeMesh.DeltaStates.Add(deltaState);
                dmeMesh.DeltaStateWeights.Add(Vector2.Zero);
                dmeMesh.DeltaStateWeightsLagged.Add(Vector2.Zero);
            }

            // The compiler takes its flex rules from the rule set rather than from the mesh.
            var flexRules = new DmeFlexRules { Name = dmeMesh.Name, Target = dmeMesh };

            foreach (var flexName in flexNames)
            {
                if (!recovery.Expressions.TryGetValue(flexName, out var expression))
                {
                    continue;
                }

                flexRules.DeltaStates.Add(new DmeFlexRuleExpression { Name = FlexRecovery.Identifier(flexName), Expression = expression });
                flexRules.DeltaStateWeights.Add(Vector2.Zero);
            }

            combination.Targets.Add(flexRules);
        }

        foreach (var control in recovery.Controls)
        {
            var inputControl = new DmeCombinationInputControl
            {
                // The compiler rewrites a name that is not a plain identifier.
                Name = FlexRecovery.Identifier(control.Name),
                FlexMin = control.Min,
                FlexMax = control.Max,
            };

            foreach (var rawControlName in control.RawControlNames)
            {
                inputControl.RawControlNames.Add(FlexRecovery.Identifier(rawControlName));
                inputControl.WrinkleScales.Add(0f);
            }

            combination.Controls.Add(inputControl);
            combination.ControlValues.Add(new Vector3(0f, 0f, 0.5f));
            combination.ControlValuesLagged.Add(new Vector3(0f, 0f, 0.5f));
        }

        datamodel.Root!["combinationOperator"] = combination;
    }
}
