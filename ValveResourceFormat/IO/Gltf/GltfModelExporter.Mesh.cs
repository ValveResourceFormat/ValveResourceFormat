using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json.Nodes;
using SharpGLTF.Memory;
using SharpGLTF.Schema2;
using ValveKeyValue;
using ValveResourceFormat.Blocks;
using ValveResourceFormat.Serialization.KeyValues;
using VMaterial = ValveResourceFormat.ResourceTypes.Material;
using VMesh = ValveResourceFormat.ResourceTypes.Mesh;
using VModel = ValveResourceFormat.ResourceTypes.Model;
using VMorph = ValveResourceFormat.ResourceTypes.Morph;

namespace ValveResourceFormat.IO;

public partial class GltfModelExporter
{
    // https://github.com/KhronosGroup/glTF-Validator/blob/master/lib/src/errors.dart
    private const float UnitLengthThresholdVec3 = 0.00674f;

    private const float OverlayNormalOffsetDistance = 0.01f / 0.0254f;

    // TODO: Using floats as hash key is kind of unhinged
    private readonly record struct ExportedMaterial(string Name, Vector4 Tint);
    private readonly record struct ExportedMaterialData(Material Material, bool IsOverlay);
    private readonly Dictionary<ExportedMaterial, ExportedMaterialData> ExportedMaterials = [];

    // Accessor paired with the Source 2 vertex attribute semantic it was created from, null for synthesized data.
    private readonly record struct AttributeAccessor(Accessor Accessor, (string Name, int Index)? Semantic = null);
    private readonly Dictionary<string, VMaterial.VsInputSignature> MaterialInputSignatures = [];
    // Scaled copies keyed by source accessor, so draw calls sharing a vertex buffer reuse one copy.
    private readonly Dictionary<Accessor, Accessor> ScaledLightmapUvAccessors = [];

    private Mesh CreateGltfMesh(string meshName, VMesh vmesh, VBIB vbib, ModelRoot exportedModel, int[]? boneRemapTable, string? skinMaterialPath, Vector4 tintColor)
    {
        ProgressReporter?.Report($"Creating mesh: {meshName}");

        var mesh = exportedModel.CreateMesh(meshName);
        mesh.Extras = new JsonObject();

        vmesh.LoadExternalMorphData(FileLoader);

        var boneWeightCount = vmesh.BoneWeightCount;

        var vertexBufferAccessors = CreateVertexBufferAccessors(exportedModel, vbib, boneRemapTable != null ? boneWeightCount : 0, boneRemapTable);
        var vertexOffset = 0;

        // Decoding a bundle rasterises the whole delta atlas, so both are read once for the mesh
        // rather than once per draw call.
        var flexData = vmesh.MorphData?.GetFlexVertexData();
        var normalData = vmesh.MorphData?.GetFlexVertexData(MorphBundleType.NormalWrinkle);

        foreach (var sceneObject in vmesh.Data.GetArray("m_sceneObjects"))
        {
            foreach (var drawCall in sceneObject.GetArray("m_drawCalls"))
            {
                var primitive = CreateMeshFromDrawCall(drawCall, mesh, vbib, vertexBufferAccessors, exportedModel, skinMaterialPath, tintColor);

                if (flexData != null && normalData != null)
                {
                    var vertexCount = drawCall.GetInt32Property("m_nVertexCount");
                    AddMorphTargetsToPrimitive(vmesh.MorphData!, flexData, normalData, primitive, exportedModel, vertexOffset, vertexCount);
                    vertexOffset += vertexCount;
                }
            }
        }

        return mesh;
    }

    private static Dictionary<string, AttributeAccessor>[] CreateVertexBufferAccessors(ModelRoot exportedModel, VBIB vbib, int boneWeightCount, int[]? boneRemapTable = null)
    {
        return vbib.VertexBuffers.Select((vertexBuffer, vertexBufferIndex) =>
        {
            var accessors = new Dictionary<string, AttributeAccessor>();

            if (vertexBuffer.ElementCount == 0)
            {
                return accessors;
            }

            // Avoid duplicate attribute names
            var attributeCounters = new Dictionary<string, int>();

            // Set vertex attributes
            ushort[]? joints = null;
            Vector4[]? weights = null;

            foreach (var attribute in vertexBuffer.InputLayoutFields.OrderBy(i => i.SemanticIndex).ThenBy(i => i.Offset))
            {
                if (attribute.SemanticName is "BLENDINDICES" or "BLENDWEIGHT" or "BLENDWEIGHTS")
                {
                    if (boneWeightCount > 0)
                    {
                        if (attribute.SemanticName is "BLENDINDICES")
                        {
                            Debug.Assert(joints == null);

                            joints = VBIB.GetBlendIndicesArray(vertexBuffer, attribute, boneRemapTable);
                        }
                        else if (attribute.SemanticName is "BLENDWEIGHT" or "BLENDWEIGHTS")
                        {
                            Debug.Assert(weights == null);

                            weights = VBIB.GetBlendWeightsArray(vertexBuffer, attribute);
                        }
                    }

                    continue;
                }

                var attributeFormat = VBIB.GetFormatInfo(attribute);
                var accessorName = attribute.SemanticName switch
                {
                    "TEXCOORD" when attributeFormat.ElementCount == 2 => "TEXCOORD",
                    "COLOR" => attribute.SemanticIndex == 0 ? "COLOR" : "_COLOR",
                    "POSITION" => "POSITION",
                    "NORMAL" => "NORMAL",
                    "TANGENT" => "TANGENT",
                    _ => $"_{attribute.SemanticName}",
                };

                // None of the glTF accessors expect scalar type
                if (attributeFormat.ElementCount == 1 && accessorName[0] != '_')
                {
                    accessorName = $"_{accessorName}";
                }

                attributeCounters.TryGetValue(accessorName, out var attributeCounter);
                attributeCounters[accessorName] = attributeCounter + 1;

                if (attribute.SemanticIndex > 0 && accessorName[0] == '_')
                {
                    // Application-specific attributes can use the original semantic index
                    accessorName = $"{accessorName}_{attribute.SemanticIndex}";
                }
                else if (attribute.SemanticName is "TEXCOORD" or "COLOR")
                {
                    // All indices for indexed attribute semantics MUST start with 0 and be consecutive positive integers
                    accessorName = $"{accessorName}_{attributeCounter}";
                }
                else if (attributeCounter > 0)
                {
                    throw new NotImplementedException($"Got attribute \"{attribute.SemanticName}\" more than once, but that is not supported.");
                }

                AttributeAccessor WithSemantic(Accessor a) => new(a, (attribute.SemanticName, attribute.SemanticIndex));

                if (attribute.SemanticName == "NORMAL")
                {
                    var (normals, tangents) = VBIB.GetNormalTangentArray(vertexBuffer, attribute);
                    FixZeroLengthVectors(normals);
                    BakeDirections(normals);

                    if (tangents.Length > 0)
                    {
                        FixZeroLengthVectors(tangents);
                        BakeTangents(tangents);
                        accessors["NORMAL"] = WithSemantic(CreateAccessor(exportedModel, normals));
                        accessors["TANGENT"] = WithSemantic(CreateAccessor(exportedModel, tangents));
                    }
                    else
                    {
                        accessors[accessorName] = WithSemantic(CreateAccessor(exportedModel, normals));
                    }
                }
                else
                {
                    switch (attributeFormat.ElementCount)
                    {
                        case 1:
                        {
                            var buffer = VBIB.GetScalarAttributeArray(vertexBuffer, attribute);
                            SanitizeNonFinite(buffer);
                            var bufferView = exportedModel.CreateBufferView(4 * buffer.Length, 0, BufferMode.ARRAY_BUFFER);
                            new ScalarArray(bufferView.Content).Fill(buffer);
                            var accessor = exportedModel.CreateAccessor();
                            accessor.SetVertexData(bufferView, 0, buffer.Length, AttributeFormat.Float1);
                            accessors[accessorName] = WithSemantic(accessor);
                            break;
                        }

                        case 2:
                        {
                            var vectors = VBIB.GetVector2AttributeArray(vertexBuffer, attribute);
                            accessors[accessorName] = WithSemantic(CreateAccessor(exportedModel, vectors));
                            break;
                        }
                        case 3:
                        {
                            var vectors = VBIB.GetVector3AttributeArray(vertexBuffer, attribute);
                            if (accessorName == "POSITION")
                            {
                                BakePositions(vectors);
                            }
                            accessors[accessorName] = WithSemantic(CreateAccessor(exportedModel, vectors));
                            break;
                        }
                        case 4:
                        {
                            var vectors = VBIB.GetVector4AttributeArray(vertexBuffer, attribute);

                            if (accessorName == "TANGENT")
                            {
                                FixZeroLengthVectors(vectors);
                                BakeTangents(vectors);
                            }

                            accessors[accessorName] = WithSemantic(CreateAccessor(exportedModel, vectors));
                            break;
                        }

                        default:
                            throw new NotImplementedException($"Attribute \"{attribute.SemanticName}\" has {attributeFormat.ElementCount} components");
                    }
                }
            }

            if (joints != null)
            {
                var isEightBonePackedFormat = boneWeightCount > 4;
                var actualJointCount = isEightBonePackedFormat ? 8 : 4;

                // For some reason models can have joints but no weights, check if that is the case
                if (weights == null)
                {
                    // If this occurs, give default weights
                    var baseWeight = 1f / boneWeightCount;
                    var baseWeights0 = new Vector4(
                        boneWeightCount > 0 ? baseWeight : 0,
                        boneWeightCount > 1 ? baseWeight : 0,
                        boneWeightCount > 2 ? baseWeight : 0,
                        boneWeightCount > 3 ? baseWeight : 0
                    );

                    if (isEightBonePackedFormat)
                    {
                        var baseWeights1 = new Vector4(
                            boneWeightCount > 4 ? baseWeight : 0,
                            boneWeightCount > 5 ? baseWeight : 0,
                            boneWeightCount > 6 ? baseWeight : 0,
                            boneWeightCount > 7 ? baseWeight : 0
                        );
                        weights = new Vector4[(int)vertexBuffer.ElementCount * 2];
                        for (var i = 0; i < weights.Length; i += 2)
                        {
                            weights[i] = baseWeights0;
                            weights[i + 1] = baseWeights1;
                        }
                    }
                    else
                    {
                        weights = [.. Enumerable.Repeat(baseWeights0, (int)vertexBuffer.ElementCount)];
                    }
                }

                var weightsFloats = MemoryMarshal.Cast<Vector4, float>(weights.AsSpan());

                FixDuplicateJoints(joints, weightsFloats, actualJointCount);

                // joints
                var bufferView = exportedModel.CreateBufferView(2 * joints.Length, 8, BufferMode.ARRAY_BUFFER);
                var bufferViewShorts = MemoryMarshal.Cast<byte, ushort>(((Memory<byte>)bufferView.Content).Span);

                if (isEightBonePackedFormat)
                {
                    Debug.Assert(joints.Length == 8 * vertexBuffer.ElementCount);
                    Debug.Assert(weights.Length == 2 * vertexBuffer.ElementCount);

                    SplitEightBoneJoints(joints, bufferViewShorts);

                    var accessor0 = exportedModel.CreateAccessor();
                    var accessor1 = exportedModel.CreateAccessor();

                    accessor0.SetVertexData(bufferView, 0, joints.Length / 8, new AttributeFormat(DimensionType.VEC4, EncodingType.UNSIGNED_SHORT));
                    accessor1.SetVertexData(bufferView, joints.Length, joints.Length / 8, new AttributeFormat(DimensionType.VEC4, EncodingType.UNSIGNED_SHORT));

                    accessors["JOINTS_0"] = new(accessor0);
                    accessors["JOINTS_1"] = new(accessor1);
                }
                else
                {
                    joints.CopyTo(bufferViewShorts);

                    var accessor = exportedModel.CreateAccessor();
                    accessor.SetVertexData(bufferView, 0, joints.Length / 4, new AttributeFormat(DimensionType.VEC4, EncodingType.UNSIGNED_SHORT));
                    accessors["JOINTS_0"] = new(accessor);
                }

                // weights
                if (isEightBonePackedFormat)
                {
                    var weights0 = new Vector4[weights.Length / 2];
                    var weights1 = new Vector4[weights.Length / 2];
                    var w = 0;

                    for (var i = 0; i < weights.Length; i += 2)
                    {
                        weights0[w] = weights[i];
                        weights1[w] = weights[i + 1];
                        w++;
                    }

                    accessors["WEIGHTS_0"] = new(CreateAccessor(exportedModel, weights0));
                    accessors["WEIGHTS_1"] = new(CreateAccessor(exportedModel, weights1));
                }
                else
                {
                    accessors["WEIGHTS_0"] = new(CreateAccessor(exportedModel, weights));
                }
            }

            return accessors;
        }).ToArray();
    }

    private MeshPrimitive CreateMeshFromDrawCall(KVObject drawCall, Mesh mesh, VBIB vbib, Dictionary<string,
        AttributeAccessor>[] vertexBufferAccessors, ModelRoot exportedModel, string? skinMaterialPath, Vector4 parentTintColor)
    {
        CancellationToken.ThrowIfCancellationRequested();

        var indexBufferInfo = drawCall.GetSubCollection("m_indexBuffer");
        var indexBufferIndex = indexBufferInfo.GetInt32Property("m_hBuffer");
        var indexBuffer = vbib.IndexBuffers[indexBufferIndex];

        // A draw call always names a material here; ModelExtract is the path that has to cope with one
        // that does not.
        var materialPath = skinMaterialPath ?? VMesh.GetMaterialName(drawCall)!;
        Resource? materialResource = null;

        // Bake g_vLightmapUvScale into lightmap UVs. Like the renderer, only draw calls with baked
        // lightmapping are affected, and the UV channel comes from the material's input signature.
        var materialInputSignature = VMaterial.VsInputSignature.Empty;
        if (LightmapUvScale != Vector2.One && VMesh.HasBakedLightingFromLightMap(drawCall))
        {
            if (!MaterialInputSignatures.TryGetValue(materialPath, out materialInputSignature))
            {
                materialResource = FileLoader.LoadFileCompiled(materialPath);

                materialInputSignature = materialResource?.DataBlock is VMaterial loadedMaterial
                    ? loadedMaterial.InputSignature
                    : VMaterial.VsInputSignature.Empty;

                MaterialInputSignatures.Add(materialPath, materialInputSignature);
            }
        }

        // Create one primitive per draw call
        var primitive = mesh.CreatePrimitive();

        var vertexBuffers = drawCall.GetArray("m_vertexBuffers");

        // Each vertex buffer names its TEXCOORDs/COLORs from 0 independently, so remap to a
        // global counter here to avoid later buffers overwriting earlier ones on the primitive.
        var texcoordCounter = 0;
        var colorCounter = 0;

        foreach (var vertexBufferInfo in vertexBuffers)
        {
            var vertexBufferIndex = vertexBufferInfo.GetInt32Property("m_hBuffer");

            foreach (var (attributeKey, attributeAccessor) in vertexBufferAccessors[vertexBufferIndex])
            {
                string key;
                if (attributeKey.StartsWith("TEXCOORD_", StringComparison.Ordinal))
                {
                    key = $"TEXCOORD_{texcoordCounter++}";
                }
                else if (attributeKey.StartsWith("COLOR_", StringComparison.Ordinal))
                {
                    key = $"COLOR_{colorCounter++}";
                }
                else
                {
                    key = attributeKey;
                }

                var accessor = attributeAccessor.Accessor;

                if (attributeAccessor.Semantic is { } semantic
                    && accessor.Dimensions == DimensionType.VEC2
                    && VMaterial.FindD3DInputSignatureElement(materialInputSignature, semantic.Name, semantic.Index).Name is "vLightmapUV" or "vLightmapUVW")
                {
                    accessor = GetScaledLightmapUvAccessor(exportedModel, accessor);
                }

                primitive.SetVertexAccessor(key, accessor);

                DebugValidateGLTF();
            }
        }

        // Set index buffer
        var baseVertex = drawCall.GetInt32Property("m_nBaseVertex");
        var startIndex = drawCall.GetInt32Property("m_nStartIndex");
        var indexCount = drawCall.GetInt32Property("m_nIndexCount");
        var indices = ReadIndices(indexBuffer, startIndex, indexCount, baseVertex);

        var primitiveType = drawCall.GetEnumValue<RenderPrimitiveType>("m_nPrimitiveType");

        switch (primitiveType)
        {
            case RenderPrimitiveType.RENDER_PRIM_TRIANGLES:
                primitive.WithIndicesAccessor(PrimitiveType.TRIANGLES, indices);
                break;
            default:
                throw new NotImplementedException($"Unknown PrimitiveType in drawCall! {primitiveType}");
        }

        DebugValidateGLTF();

        // Add material
        if (!ExportMaterials)
        {
            return primitive;
        }

        var modelTintColor = parentTintColor;

        if (drawCall.ContainsKey("m_vTintColor"))
        {
            var drawCallTintColor = drawCall.GetSubCollection("m_vTintColor").ToVector3();
            var dcTintColorWithAlpha = new Vector4(drawCallTintColor, 1.0f);

            if (drawCall.ContainsKey("m_flAlpha"))
            {
                dcTintColorWithAlpha.W = drawCall.GetFloatProperty("m_flAlpha");
            }

            modelTintColor *= dcTintColorWithAlpha;
        }

        var materialNameTrimmed = Path.GetFileNameWithoutExtension(materialPath);
        var materialHashKey = new ExportedMaterial(materialPath, modelTintColor);

        if (ExportedMaterials.TryGetValue(materialHashKey, out var existingMaterial))
        {
            primitive.WithMaterial(existingMaterial.Material);

            if (existingMaterial.IsOverlay)
            {
                OffsetMeshPositionsByNormals(primitive);
            }

            return primitive;
        }

        ProgressReporter?.Report($"Loading material: {materialPath}");

        materialResource ??= FileLoader.LoadFileCompiled(materialPath);

        if (materialResource == null)
        {
            return primitive;
        }

        var renderMaterial = (VMaterial)materialResource.DataBlock!;
        var isOverlay = IsMaterialOverlay(renderMaterial);
        var material = exportedModel
            .CreateMaterial(materialNameTrimmed)
            .WithDefault();
        primitive.WithMaterial(material);

        ExportedMaterials.Add(materialHashKey, new(material, isOverlay));

        // TODO: Realistically it should export a material without a tint, and then if it needs a model tint,
        // copy the existing untinted material, and just change the pbr BaseColor to include the tint.
        GenerateGLTFMaterialFromRenderMaterial(material, renderMaterial, exportedModel, modelTintColor);

        if (isOverlay)
        {
            OffsetMeshPositionsByNormals(primitive);
        }

        return primitive;
    }

    // Copied from ValveResourceFormat.Renderer.SceneAggregate.CreateFragments
    private bool AggregateCreateFragments(ModelRoot exportedModel, Scene scene, VModel model, KVObject aggregateSceneObject, string name)
    {
        var embeddedMeshes = model.GetEmbeddedMeshesAndLoD().ToList();
        VMesh vmesh;

        // TODO: Perhaps use <see cref="ModelSceneNode.LoadMeshes" />
        if (embeddedMeshes.Count > 0)
        {
            if (embeddedMeshes.Count > 1)
            {
                throw new NotImplementedException("More than one embedded mesh");
            }

            vmesh = embeddedMeshes.First().Mesh;
        }
        else
        {
            var refMeshes = model.GetReferenceMeshNamesForLod(model.LodInfo.LowestLevel).ToList();
            var refMesh = refMeshes.First();

            if (refMeshes.Count > 1)
            {
                throw new NotImplementedException("More than one referenced mesh");
            }

            var newResource = FileLoader.LoadFileCompiled(refMesh.MeshName);
            if (newResource == null)
            {
                return false;
            }

            vmesh = (VMesh)newResource.DataBlock!;
        }

        var aggregateMeshes = aggregateSceneObject.GetArray("m_aggregateMeshes");

        // Aperture Desk Job goes from draw call -> aggregate mesh
        if (aggregateMeshes.Count > 0 && !aggregateMeshes[0].ContainsKey("m_nDrawCallIndex"))
        {
            return false;
        }

        var vbib = vmesh.VBIB;
        var vertexBufferAccessors = CreateVertexBufferAccessors(exportedModel, vbib, boneWeightCount: 0);

        var transformIndex = 0;
        var fragmentTransforms = aggregateSceneObject.GetArray("m_fragmentTransforms");

        var meshSceneObjects = vmesh.Data.GetArray("m_sceneObjects");
        var drawCalls = new List<KVObject>(meshSceneObjects.Count);

        foreach (var meshSceneObject in meshSceneObjects)
        {
            var objectDrawCalls = meshSceneObject.GetArray("m_drawCalls");
            drawCalls.AddRange(objectDrawCalls);
        }

        // LoD levels are indexed within each m_lodSetups entry, so the highest detail tier is the
        // lowest level present per setup, not across the whole aggregate.
        var combinedLodMaskPerSetup = new Dictionary<int, uint>();
        foreach (var fragmentData in aggregateMeshes)
        {
            var setupIndex = fragmentData.GetInt32Property("m_nLODSetupIndex", -1);
            combinedLodMaskPerSetup.TryGetValue(setupIndex, out var combinedLodMask);
            combinedLodMaskPerSetup[setupIndex] = combinedLodMask | fragmentData.GetUInt32Property("m_nLODGroupMask");
        }

        var id = 0;

        foreach (var fragmentData in aggregateMeshes)
        {
            var meshName = $"{name}_fragment{++id}";
            var drawCallIndex = fragmentData.GetInt32Property("m_nDrawCallIndex");
            var drawCall = drawCalls[drawCallIndex];
            var transform = Matrix4x4.Identity;

            if (fragmentData.GetBooleanProperty("m_bHasTransform") == true)
            {
                transform *= fragmentTransforms[transformIndex++].ToMatrix4x4();

                // A zero determinant means the transform collapses the fragment to nothing. Testing the
                // diagonal instead would also match an honest rotation that maps every axis onto a
                // different one, and throw the fragment away.
                if (transform.GetDeterminant() == 0f)
                {
                    ProgressReporter?.Report($"Skipping mesh: {meshName} because it has a scale of zero.");
                    continue;
                }
            }

            var lodGroupMask = fragmentData.GetUInt32Property("m_nLODGroupMask");
            var setupIndex = fragmentData.GetInt32Property("m_nLODSetupIndex", -1);
            if (!ResourceTypes.ModelLodInfo.IsInLowestSetLevel(lodGroupMask, combinedLodMaskPerSetup[setupIndex]))
            {
                continue;
            }

            var tintColor = Vector4.One;

            if (fragmentData.ContainsKey("m_vTintColor"))
            {
                var fragmentTintColor = fragmentData.GetSubCollection("m_vTintColor").ToVector3();
                tintColor = new Vector4(fragmentTintColor / 255f, 1.0f);
            }

            ProgressReporter?.Report($"Creating mesh: {meshName}");

            var mesh = exportedModel.CreateMesh(meshName);
            mesh.Extras = new JsonObject();

            CreateMeshFromDrawCall(drawCall, mesh, vbib, vertexBufferAccessors, exportedModel, skinMaterialPath: null, tintColor);

            var newNode = scene.CreateNode(name).WithMesh(mesh);
            // The conversion is baked into the geometry (CreateVertexBufferAccessors), so the placement
            // transform is conjugated by it rather than multiplied on top - otherwise it applies twice.
            newNode.WorldMatrix = GetPlacementTransform(transform);
        }

        return true;
    }

    private static void AddMorphTargetsToPrimitive(VMorph morph, Dictionary<string, Vector3[]> flexData,
        Dictionary<string, Vector4[]> normalData, MeshPrimitive primitive, ModelRoot model, int vertexOffset, int vertexCount)
    {
        var morphIndex = 0;
        var flexDesc = morph.GetFlexDescriptors();

        // Morph deltas are mostly zero, so each target is written as a sparse accessor over one shared
        // run of zeroes rather than a full copy of the mesh. A buffer view that several accessors read
        // has to declare its stride.
        var zeroes = model.CreateBufferView(3 * sizeof(float) * vertexCount, 3 * sizeof(float), BufferMode.ARRAY_BUFFER);

        foreach (var morphName in flexDesc)
        {
            if (!flexData.TryGetValue(morphName, out var rectData))
            {
                continue;
            }

            // Morph deltas share the base mesh's vertex space, which is baked into glTF units, so bake them too.
            // The delta grid follows a vertex count the compiler is free to change, so it can end short.
            var deltas = new Vector3[vertexCount];
            var available = Math.Clamp(rectData.Length - vertexOffset, 0, vertexCount);

            if (available > 0)
            {
                Array.Copy(rectData, vertexOffset, deltas, 0, available);
            }

            BakePositions(deltas);

            var dict = new Dictionary<string, Accessor>
                {
                    { "POSITION", CreateMorphAccessor(model, zeroes, deltas, morphName) }
                };

            // The normal bundle is optional, and a morph set that carries one only fills it for the
            // targets that actually move normals.
            if (normalData.TryGetValue(morphName, out var normalDeltas))
            {
                var normals = new Vector3[vertexCount];
                var anyNormal = false;

                for (var i = 0; i < vertexCount; i++)
                {
                    var vertexId = vertexOffset + i;

                    if (vertexId >= normalDeltas.Length)
                    {
                        break;
                    }

                    var normal = normalDeltas[vertexId];
                    normals[i] = new Vector3(normal.X, normal.Y, normal.Z);
                    anyNormal |= normals[i] != Vector3.Zero;
                }

                if (anyNormal)
                {
                    BakeDirections(normals);
                    dict.Add("NORMAL", CreateMorphAccessor(model, zeroes, normals, morphName + "_normal"));
                }
            }

            primitive.SetMorphTargetAccessors(morphIndex++, dict);
        }

        DebugValidateGLTF();
    }

    /// <summary>
    /// Writes one morph target stream, sparsely when that is smaller. A sparse entry costs an index on
    /// top of its value, so it only pays off while under three quarters of the vertices move.
    /// </summary>
    private static Accessor CreateMorphAccessor(ModelRoot model, BufferView zeroes, Vector3[] deltas, string name)
    {
        var moved = new Dictionary<int, Vector3>();

        for (var i = 0; i < deltas.Length; i++)
        {
            if (deltas[i] != Vector3.Zero)
            {
                moved.Add(i, deltas[i]);
            }
        }

        var accessor = model.CreateAccessor();
        accessor.Name = name;

        if (moved.Count * 4 > deltas.Length * 3)
        {
            var bufferView = model.CreateBufferView(3 * sizeof(float) * deltas.Length, 0, BufferMode.ARRAY_BUFFER);
            new Vector3Array(bufferView.Content).Fill(deltas);
            accessor.SetData(bufferView, 0, deltas.Length, AttributeFormat.Float3);

            return accessor;
        }

        accessor.SetData(zeroes, 0, deltas.Length, AttributeFormat.Float3);

        // A sparse block has to hold at least one entry, so an all-zero target stays as the shared zeroes.
        if (moved.Count > 0)
        {
            accessor.CreateSparseData(moved);
        }

        return accessor;
    }

    /// <summary>
    /// Reads indices from an index buffer and applies a base vertex offset.
    /// </summary>
    public static int[] ReadIndices(VBIB.OnDiskBufferData indexBuffer, int start, int count, int baseVertex)
    {
        var indices = new int[count];

        var byteCount = count * (int)indexBuffer.ElementSizeInBytes;
        var byteStart = start * (int)indexBuffer.ElementSizeInBytes;

        if (indexBuffer.ElementSizeInBytes == 4)
        {
            System.Buffer.BlockCopy(indexBuffer.Data, byteStart, indices, 0, byteCount);
            for (var i = 0; i < count; i++)
            {
                indices[i] += baseVertex;
            }
        }
        else if (indexBuffer.ElementSizeInBytes == 2)
        {
            var shortIndices = MemoryMarshal.Cast<byte, ushort>(indexBuffer.Data).Slice(start, count);
            for (var i = 0; i < count; i++)
            {
                indices[i] = baseVertex + shortIndices[i];
            }
        }

        return indices;
    }

    private Accessor GetScaledLightmapUvAccessor(ModelRoot exportedModel, Accessor accessor)
    {
        if (ScaledLightmapUvAccessors.TryGetValue(accessor, out var scaledAccessor))
        {
            return scaledAccessor;
        }

        Debug.Assert(accessor.Format.Encoding == EncodingType.FLOAT);

        var bufferView = exportedModel.CreateBufferView(2 * sizeof(float) * accessor.Count, 0, BufferMode.ARRAY_BUFFER);
        var source = MemoryMarshal.Cast<byte, Vector2>(((Memory<byte>)accessor.SourceBufferView.Content).Span);
        var target = MemoryMarshal.Cast<byte, Vector2>(((Memory<byte>)bufferView.Content).Span);

        for (var i = 0; i < source.Length; i++)
        {
            target[i] = source[i] * LightmapUvScale;
        }

        scaledAccessor = exportedModel.CreateAccessor();
        scaledAccessor.SetVertexData(bufferView, 0, accessor.Count, AttributeFormat.Float2);

        ScaledLightmapUvAccessors.Add(accessor, scaledAccessor);
        return scaledAccessor;
    }

    private static Accessor CreateAccessor(ModelRoot exportedModel, Vector2[] vectors)
    {
        SanitizeNonFinite(MemoryMarshal.Cast<Vector2, float>(vectors.AsSpan()));

        var bufferView = exportedModel.CreateBufferView(2 * sizeof(float) * vectors.Length, 0, BufferMode.ARRAY_BUFFER);
        new Vector2Array(bufferView.Content).Fill(vectors);

        var accessor = exportedModel.CreateAccessor();
        accessor.SetVertexData(bufferView, 0, vectors.Length, AttributeFormat.Float2);

        return accessor;
    }

    private static Accessor CreateAccessor(ModelRoot exportedModel, Vector3[] vectors)
    {
        SanitizeNonFinite(MemoryMarshal.Cast<Vector3, float>(vectors.AsSpan()));

        var bufferView = exportedModel.CreateBufferView(3 * sizeof(float) * vectors.Length, 0, BufferMode.ARRAY_BUFFER);
        new Vector3Array(bufferView.Content).Fill(vectors);

        var accessor = exportedModel.CreateAccessor();
        accessor.SetVertexData(bufferView, 0, vectors.Length, AttributeFormat.Float3);

        return accessor;
    }

    private static Accessor CreateAccessor(ModelRoot exportedModel, Vector4[] vectors)
    {
        SanitizeNonFinite(MemoryMarshal.Cast<Vector4, float>(vectors.AsSpan()));

        var bufferView = exportedModel.CreateBufferView(4 * sizeof(float) * vectors.Length, 0, BufferMode.ARRAY_BUFFER);
        new Vector4Array(bufferView.Content).Fill(vectors);

        var accessor = exportedModel.CreateAccessor();
        accessor.SetVertexData(bufferView, 0, vectors.Length, AttributeFormat.Float4);

        return accessor;
    }

    private static void FixZeroLengthVectors(Span<Vector4> vectorArray)
    {
        for (var i = 0; i < vectorArray.Length; i++)
        {
            var vec = vectorArray[i];

            if (Math.Abs(new Vector3(vec.X, vec.Y, vec.Z).Length() - 1.0f) > UnitLengthThresholdVec3)
            {
                vectorArray[i] = -Vector4.UnitZ;
                vectorArray[i].W = vec.W;
            }
        }
    }

    private static void FixZeroLengthVectors(Span<Vector3> vectorArray)
    {
        for (var i = 0; i < vectorArray.Length; i++)
        {
            if (Math.Abs(vectorArray[i].Length() - 1.0f) > UnitLengthThresholdVec3)
            {
                vectorArray[i] = -Vector3.UnitZ;
            }
        }
    }

    /// <summary>
    /// Splits an interleaved 8-bone-per-vertex joint array into two VEC4 halves
    /// laid out back-to-back (JOINTS_0 followed by JOINTS_1) for glTF export.
    /// </summary>
    internal static void SplitEightBoneJoints(ReadOnlySpan<ushort> joints, Span<ushort> output)
    {
        Debug.Assert(joints.Length % 8 == 0);
        Debug.Assert(output.Length == joints.Length);

        var joints0 = 0;
        var joints1 = joints.Length / 2;

        for (var i = 0; i < joints.Length; i += 8)
        {
            output[joints0++] = joints[i];
            output[joints0++] = joints[i + 1];
            output[joints0++] = joints[i + 2];
            output[joints0++] = joints[i + 3];

            output[joints1++] = joints[i + 4];
            output[joints1++] = joints[i + 5];
            output[joints1++] = joints[i + 6];
            output[joints1++] = joints[i + 7];
        }
    }

    /// <summary>
    /// Processes joint and weight data to ensure consistency by:
    /// 1. Setting joints with zero weights to zero (no influence)
    /// 2. Merging weights of duplicate joint references
    /// 3. Ensuring valid data is packed into consecutive positions
    /// </summary>
    /// <param name="joints">Array of joint indices (ushort), organized in groups of size <paramref name="jointCount"/></param>
    /// <param name="weights">Array of weight values (float), corresponding to each joint</param>
    /// <param name="jointCount">Number of joints per vertex (typically 4 or 8)</param>
    internal static void FixDuplicateJoints(Span<ushort> joints, Span<float> weights, int jointCount)
    {
        // Process each group of joints (each group corresponds to one vertex)
        for (var i = 0; i < joints.Length; i += jointCount)
        {
            // Step 1: Clean up joints with zero weights
            // If a weight is zero, set its corresponding joint to zero (no influence)
            for (var j = 0; j < jointCount; j++)
            {
                if (weights[i + j] == 0)
                {
                    joints[i + j] = 0;
                }
            }

            // Step 2: Handle duplicate joint references within each group
            // Start from second-to-last joint and work backwards (j decreases)
            for (var j = jointCount - 2; j >= 0; j--)
            {
                // For each joint at position j, check all joints after it for duplicates
                // Start from the last joint and work backwards (k decreases)
                for (var k = jointCount - 1; k > j; k--)
                {
                    // If we found a duplicate joint reference
                    if (joints[i + j] == joints[i + k])
                    {
                        // Step 3: Shift all joints after position k one position left
                        // This effectively removes the duplicate at position k
                        for (var l = k; l < jointCount - 1; l++)
                        {
                            joints[i + l] = joints[i + l + 1];
                        }

                        // Zero out the last position which is now unused
                        joints[i + jointCount - 1] = 0;

                        // Step 4: Combine the weights - add the duplicate's weight to the original
                        weights[i + j] += weights[i + k];

                        // Step 5: Shift all weights after position k one position left
                        // Just like we did for the joints
                        for (var l = k; l < jointCount - 1; l++)
                        {
                            weights[i + l] = weights[i + l + 1];
                        }

                        // Zero out the last weight position
                        weights[i + jointCount - 1] = 0;
                    }
                }
            }
        }
    }

    private static void OffsetMeshPositionsByNormals(MeshPrimitive primitive)
    {
        var positionAccessor = primitive.GetVertexAccessor("POSITION");
        var normalAccessor = primitive.GetVertexAccessor("NORMAL");

        if (positionAccessor == null || normalAccessor == null)
        {
            return;
        }

        var positions = positionAccessor.AsVector3Array();
        var normals = normalAccessor.AsVector3Array();

        var updatedPositions = new Vector3[positions.Count];

        for (var i = 0; i < positions.Count; i++)
        {
            updatedPositions[i] = positions[i] + normals[i] * OverlayNormalOffsetDistance;
        }

        primitive.SetVertexAccessor("POSITION", CreateAccessor(primitive.LogicalParent.LogicalParent, updatedPositions));
    }
}
