using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Numerics.Tensors;
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

    private const float OverlayNormalOffsetDistance = 0.01f;

    // TODO: Using floats as hash key is kind of unhinged
    private readonly record struct ExportedMaterial(string Name, Vector4 Tint);
    private readonly record struct ExportedMaterialData(Material Material, bool IsOverlay, bool VertexColorIsData);
    private readonly Dictionary<ExportedMaterial, ExportedMaterialData> ExportedMaterials = [];

    private readonly Dictionary<string, VMaterial.VsInputSignature> MaterialInputSignatures = [];

    /// <summary>
    /// Decoded data of one vertex buffer attribute. Its accessor is only created once a draw call reads
    /// the data unchanged, so data that every draw call replaces with a derived copy (scaled lightmap UVs,
    /// offset overlay positions) is not written. Draw calls sharing the vertex buffer share the accessor
    /// and the derived copies.
    /// </summary>
    private sealed class VertexAttribute(Array data, (string Name, int Index)? semantic = null, bool isUnorm8 = false)
    {
        private Accessor? CreatedAccessor;

        // One element per vertex, except joints which hold four indices per vertex.
        public Array Data { get; } = data;

        // The Source 2 semantic the data was read from, null for synthesized data.
        public (string Name, int Index)? Semantic { get; } = semantic;

        // The data was decoded from 8-bit normalized values, so writing it back as bytes loses nothing.
        public bool IsUnorm8 { get; } = isUnorm8;

        public Accessor? ScaledLightmapUvAccessor { get; set; }

        // Overlay positions offset from this position data, keyed by the normals they are offset along.
        public Dictionary<VertexAttribute, Accessor> OverlayPositionAccessors { get; } = [];

        public Accessor GetAccessor(ModelRoot exportedModel)
        {
            CreatedAccessor ??= Data switch
            {
                float[] scalars => CreateAccessor(exportedModel, scalars),
                Vector2[] vectors => CreateAccessor(exportedModel, vectors),
                Vector3[] vectors => CreateAccessor(exportedModel, vectors),
                Vector4[] vectors when IsUnorm8 => CreateUnorm8Accessor(exportedModel, vectors),
                Vector4[] vectors => CreateAccessor(exportedModel, vectors),
                ushort[] joints => CreateJointsAccessor(exportedModel, joints),
                _ => throw new NotImplementedException($"Vertex attribute data of type {Data.GetType()} is not supported"),
            };

            return CreatedAccessor;
        }
    }

    private Mesh CreateGltfMesh(string meshName, VMesh vmesh, VBIB vbib, ModelRoot exportedModel, int[]? boneRemapTable, Dictionary<KVObject, string>? drawCallMaterials, Vector4 tintColor)
    {
        ProgressReporter?.Report($"Creating mesh: {meshName}");

        var mesh = exportedModel.CreateMesh(meshName);
        mesh.Extras = new JsonObject();

        var boneWeightCount = vmesh.BoneWeightCount;

        var vertexBufferAttributes = CreateVertexBufferAttributes(vbib, boneRemapTable != null ? boneWeightCount : 0, boneRemapTable);
        var vertexOffset = 0;

        // Decoding a bundle rasterises the whole delta atlas, so both are read once for the mesh
        // rather than once per draw call.
        var flexData = vmesh.MorphData?.GetFlexVertexData();
        var normalData = vmesh.MorphData?.GetFlexVertexData(MorphBundleType.NormalWrinkle);
        var hasMorphData = flexData != null && normalData != null;
        var morphTargetNames = hasMorphData ? GetMorphTargetNames(vmesh.MorphData!, flexData!) : [];

        foreach (var sceneObject in vmesh.Data.GetArray("m_sceneObjects"))
        {
            foreach (var drawCall in sceneObject.GetArray("m_drawCalls"))
            {
                var vertexCount = drawCall.GetInt32Property("m_nVertexCount");

                if (!IsDrawCallSkipped(drawCall, drawCallMaterials))
                {
                    var primitive = CreateMeshFromDrawCall(drawCall, mesh, vbib, vertexBufferAttributes, exportedModel, drawCallMaterials, tintColor);

                    if (hasMorphData)
                    {
                        AddMorphTargetsToPrimitive(morphTargetNames, flexData!, normalData!, primitive, exportedModel, vertexOffset, vertexCount);
                    }
                }

                vertexOffset += vertexCount;
            }
        }

        if (morphTargetNames.Count > 0)
        {
            AddMorphTargetNames(morphTargetNames, mesh);
        }

        return mesh;
    }

    private static Dictionary<string, VertexAttribute>[] CreateVertexBufferAttributes(VBIB vbib, int boneWeightCount, int[]? boneRemapTable = null)
    {
        return vbib.VertexBuffers.Select((vertexBuffer, vertexBufferIndex) =>
        {
            var attributes = new Dictionary<string, VertexAttribute>();

            if (vertexBuffer.ElementCount == 0)
            {
                return attributes;
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

                VertexAttribute WithSemantic(Array data) => new(data, (attribute.SemanticName, attribute.SemanticIndex));

                if (attribute.SemanticName == "NORMAL")
                {
                    var (normals, tangents) = VBIB.GetNormalTangentArray(vertexBuffer, attribute);
                    FixZeroLengthVectors(normals);
                    BakeDirections(normals);

                    if (tangents.Length > 0)
                    {
                        FixZeroLengthVectors(tangents);
                        BakeTangents(tangents);
                        attributes["NORMAL"] = WithSemantic(normals);
                        attributes["TANGENT"] = WithSemantic(tangents);
                    }
                    else
                    {
                        attributes[accessorName] = WithSemantic(normals);
                    }
                }
                else
                {
                    switch (attributeFormat.ElementCount)
                    {
                        case 1:
                        {
                            var buffer = VBIB.GetScalarAttributeArray(vertexBuffer, attribute);
                            attributes[accessorName] = WithSemantic(buffer);
                            break;
                        }

                        case 2:
                        {
                            var vectors = VBIB.GetVector2AttributeArray(vertexBuffer, attribute);
                            attributes[accessorName] = WithSemantic(vectors);
                            break;
                        }
                        case 3:
                        {
                            var vectors = VBIB.GetVector3AttributeArray(vertexBuffer, attribute);
                            if (accessorName == "POSITION")
                            {
                                BakePositions(vectors);
                            }
                            attributes[accessorName] = WithSemantic(vectors);
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

                            var isUnorm8Color = attribute.SemanticName == "COLOR" && attribute.Format == DXGI_FORMAT.R8G8B8A8_UNORM;
                            attributes[accessorName] = new(vectors, (attribute.SemanticName, attribute.SemanticIndex), isUnorm8Color);
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

                // Weights are stored as bytes; merging duplicate joints only sums them, which keeps them on that grid
                var weightsAreUnorm8 = weights != null;

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
                if (isEightBonePackedFormat)
                {
                    Debug.Assert(joints.Length == 8 * vertexBuffer.ElementCount);
                    Debug.Assert(weights.Length == 2 * vertexBuffer.ElementCount);

                    var splitJoints = new ushort[joints.Length];
                    SplitEightBoneJoints(joints, splitJoints);

                    var half = joints.Length / 2;
                    attributes["JOINTS_0"] = new(splitJoints[..half]);
                    attributes["JOINTS_1"] = new(splitJoints[half..]);
                }
                else
                {
                    attributes["JOINTS_0"] = new(joints);
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

                    attributes["WEIGHTS_0"] = new(weights0, isUnorm8: weightsAreUnorm8);
                    attributes["WEIGHTS_1"] = new(weights1, isUnorm8: weightsAreUnorm8);
                }
                else
                {
                    attributes["WEIGHTS_0"] = new(weights, isUnorm8: weightsAreUnorm8);
                }
            }

            return attributes;
        }).ToArray();
    }

    private MeshPrimitive CreateMeshFromDrawCall(KVObject drawCall, Mesh mesh, VBIB vbib, Dictionary<string,
        VertexAttribute>[] vertexBufferAttributes, ModelRoot exportedModel, Dictionary<KVObject, string>? drawCallMaterials, Vector4 parentTintColor)
    {
        CancellationToken.ThrowIfCancellationRequested();

        var indexBufferInfo = drawCall.GetSubCollection("m_indexBuffer");
        var indexBufferIndex = indexBufferInfo.GetInt32Property("m_hBuffer");
        var indexBuffer = vbib.IndexBuffers[indexBufferIndex];

        var materialPath = GetDrawCallMaterial(drawCall, drawCallMaterials);

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

        // The material is resolved first, as overlays replace the positions the primitive reads
        ExportedMaterialData? material = null;
        if (ExportMaterials)
        {
            material = GetOrCreateMaterial(drawCall, materialPath, materialResource, exportedModel, parentTintColor);
        }

        var vertexBuffers = drawCall.GetArray("m_vertexBuffers");
        var attributes = new Dictionary<string, VertexAttribute>();

        // Vertex buffers each number TEXCOORDs/COLORs from 0, so count globally or later buffers
        // overwrite earlier ones.
        var texcoordCounter = 0;
        var colorCounter = 0;

        foreach (var vertexBufferInfo in vertexBuffers)
        {
            var vertexBufferIndex = vertexBufferInfo.GetInt32Property("m_hBuffer");

            foreach (var (attributeKey, attribute) in vertexBufferAttributes[vertexBufferIndex])
            {
                string key;
                if (attributeKey.StartsWith("TEXCOORD_", StringComparison.Ordinal))
                {
                    key = $"TEXCOORD_{texcoordCounter++}";
                }
                else if (attributeKey.StartsWith("COLOR_", StringComparison.Ordinal))
                {
                    key = material?.VertexColorIsData == true ? $"_COLOR_{colorCounter++}" : $"COLOR_{colorCounter++}";
                }
                else
                {
                    key = attributeKey;
                }

                attributes[key] = attribute;
            }
        }

        var isOverlay = material?.IsOverlay ?? false;

        foreach (var (key, attribute) in attributes)
        {
            Accessor accessor;

            if (key == "POSITION" && isOverlay && attributes.TryGetValue("NORMAL", out var normalAttribute))
            {
                accessor = GetOverlayPositionAccessor(exportedModel, attribute, normalAttribute);
            }
            else if (IsLightmapUv(attribute, materialInputSignature))
            {
                accessor = GetScaledLightmapUvAccessor(exportedModel, attribute);
            }
            else
            {
                accessor = attribute.GetAccessor(exportedModel);
            }

            primitive.SetVertexAccessor(key, accessor);

            DebugValidateGLTF();
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
                SetIndices(primitive, PrimitiveType.TRIANGLES, indices);
                break;
            default:
                throw new NotImplementedException($"Unknown PrimitiveType in drawCall! {primitiveType}");
        }

        DebugValidateGLTF();

        if (material != null)
        {
            primitive.Material = material.Value.Material;
        }

        return primitive;
    }

    private static bool IsLightmapUv(VertexAttribute attribute, VMaterial.VsInputSignature materialInputSignature)
    {
        if (attribute.Semantic is not { } semantic || attribute.Data is not Vector2[])
        {
            return false;
        }

        var element = VMaterial.FindD3DInputSignatureElement(materialInputSignature, semantic.Name, semantic.Index);
        return element.Name is "vLightmapUV" or "vLightmapUVW";
    }

    private static string GetDrawCallMaterial(KVObject drawCall, Dictionary<KVObject, string>? drawCallMaterials)
    {
        return drawCallMaterials?.GetValueOrDefault(drawCall) ?? VMesh.GetMaterialName(drawCall)!;
    }

    private bool IsDrawCallSkipped(KVObject drawCall, Dictionary<KVObject, string>? drawCallMaterials)
    {
        if (!SkipToolsMaterials)
        {
            return false;
        }

        var materialPath = GetDrawCallMaterial(drawCall, drawCallMaterials);

        if (!ToolsMaterials.TryGetValue(materialPath, out var isToolsMaterial))
        {
            using var materialResource = FileLoader.LoadFileCompiled(materialPath);
            isToolsMaterial = materialResource?.DataBlock is VMaterial vMaterial && vMaterial.IntAttributes.ContainsKey("tools.toolsmaterial");
            ToolsMaterials.Add(materialPath, isToolsMaterial);
        }

        return isToolsMaterial;
    }

    private bool HasExportedDrawCalls(VMesh mesh, Dictionary<KVObject, string>? drawCallMaterials)
    {
        return mesh.Data.GetArray("m_sceneObjects")
            .Any(sceneObject => sceneObject.GetArray("m_drawCalls").Any(drawCall => !IsDrawCallSkipped(drawCall, drawCallMaterials)));
    }

    private ExportedMaterialData? GetOrCreateMaterial(KVObject drawCall, string materialPath, Resource? materialResource, ModelRoot exportedModel, Vector4 parentTintColor)
    {
        var modelTintColor = parentTintColor;

        if (drawCall.ContainsKey("m_vTintColor"))
        {
            var drawCallTintColor = ColorSpace.SrgbLinearToGamma(drawCall.GetSubCollection("m_vTintColor").ToVector3());
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
            return existingMaterial;
        }

        ProgressReporter?.Report($"Loading material: {materialPath}");

        materialResource ??= FileLoader.LoadFileCompiled(materialPath);

        if (materialResource == null)
        {
            return null;
        }

        var renderMaterial = (VMaterial)materialResource.DataBlock!;
        var material = exportedModel.CreateMaterial(materialNameTrimmed);
        material.InitializePBRMetallicRoughness();

        // csgo_foliage holds wind parameters in its vertex color
        var exportedMaterial = new ExportedMaterialData(material, IsMaterialOverlay(renderMaterial), renderMaterial.ShaderName == "csgo_foliage.vfx");
        ExportedMaterials.Add(materialHashKey, exportedMaterial);

        // TODO: Realistically it should export a material without a tint, and then if it needs a model tint,
        // copy the existing untinted material, and just change the pbr BaseColor to include the tint.
        GenerateGLTFMaterialFromRenderMaterial(material, renderMaterial, exportedModel, modelTintColor);

        return exportedMaterial;
    }

    // Copied from ValveResourceFormat.Renderer.SceneAggregate.CreateFragments
    private bool AggregateCreateFragments(ModelRoot exportedModel, Scene scene, VModel model, KVObject aggregateSceneObject, string name)
    {
        var embeddedMeshes = model.GetEmbeddedMeshes().ToList();
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
        var vertexBufferAttributes = CreateVertexBufferAttributes(vbib, boneWeightCount: 0);

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

            if (fragmentData.GetBooleanProperty("m_bHasTransform"))
            {
                transform *= fragmentTransforms[transformIndex++].ToMatrix4x4();

                // A zero determinant collapses the fragment to nothing. Testing the diagonal
                // would also drop honest axis-permuting rotations.
                if (transform.GetDeterminant() == 0f)
                {
                    ProgressReporter?.Report($"Skipping mesh: {meshName} because it has a scale of zero.");
                    continue;
                }
            }

            var lodGroupMask = fragmentData.GetUInt32Property("m_nLODGroupMask");
            var setupIndex = fragmentData.GetInt32Property("m_nLODSetupIndex", -1);
            if (!ResourceTypes.ModelData.ModelLodInfo.IsInLowestSetLevel(lodGroupMask, combinedLodMaskPerSetup[setupIndex]))
            {
                continue;
            }

            if (IsDrawCallSkipped(drawCall, drawCallMaterials: null))
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

            CreateMeshFromDrawCall(drawCall, mesh, vbib, vertexBufferAttributes, exportedModel, drawCallMaterials: null, tintColor);

            var newNode = scene.CreateNode(meshName);
            newNode.Mesh = mesh;
            // The conversion is baked into the geometry (CreateVertexBufferAttributes), so the placement
            // transform is conjugated by it rather than multiplied on top - otherwise it applies twice.
            newNode.WorldMatrix = GetPlacementTransform(transform);
        }

        return true;
    }

    private static void AddMorphTargetsToPrimitive(List<string> morphTargetNames, Dictionary<string, Vector3[]> flexData,
        Dictionary<string, Vector4[]> normalData, MeshPrimitive primitive, ModelRoot model, int vertexOffset, int vertexCount)
    {
        // Morph deltas are mostly zero, so targets are sparse accessors over one shared zero run.
        // A buffer view read by several accessors must declare its stride.
        var zeroes = model.CreateBufferView(3 * sizeof(float) * vertexCount, 3 * sizeof(float), BufferMode.ARRAY_BUFFER);

        for (var morphIndex = 0; morphIndex < morphTargetNames.Count; morphIndex++)
        {
            var morphName = morphTargetNames[morphIndex];
            var rectData = flexData[morphName];

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

            // The normal bundle is optional and only fills targets that move normals.
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
                    normals[i] = normal.AsVector3();
                    anyNormal |= normals[i] != Vector3.Zero;
                }

                if (anyNormal)
                {
                    BakeDirections(normals);
                    dict.Add("NORMAL", CreateMorphAccessor(model, zeroes, normals, morphName + "_normal"));
                }
            }

            primitive.SetMorphTargetAccessors(morphIndex, dict);
        }

        DebugValidateGLTF();
    }

    /// <summary>
    /// Gets the mesh's morph targets: the flex descriptors that have flex data, in descriptor order.
    /// </summary>
    internal static List<string> GetMorphTargetNames(VMorph morph, Dictionary<string, Vector3[]> flexData)
    {
        var targetNames = new List<string>();

        foreach (var morphName in morph.GetFlexDescriptors())
        {
            if (flexData.ContainsKey(morphName))
            {
                targetNames.Add(morphName);
            }
        }

        return targetNames;
    }

    /// <summary>
    /// Names the mesh's morph targets in extras.targetNames, the convention importers such as Blender and three.js
    /// read shape key names from.
    /// </summary>
    internal static void AddMorphTargetNames(List<string> morphTargetNames, Mesh mesh)
    {
        var targetNames = new JsonArray();

        foreach (var morphName in morphTargetNames)
        {
            targetNames.Add((JsonNode)morphName);
        }

        mesh.Extras["targetNames"] = targetNames;
        mesh.SetMorphWeights(new float[morphTargetNames.Count]);
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
            TensorPrimitives.Add(indices, baseVertex, indices);
        }
        else if (indexBuffer.ElementSizeInBytes == 2)
        {
            var shortIndices = MemoryMarshal.Cast<byte, ushort>(indexBuffer.Data).Slice(start, count);
            TensorPrimitives.ConvertTruncating(shortIndices, indices.AsSpan());
            TensorPrimitives.Add(indices, baseVertex, indices);
        }

        return indices;
    }

    private Accessor GetScaledLightmapUvAccessor(ModelRoot exportedModel, VertexAttribute attribute)
    {
        if (attribute.ScaledLightmapUvAccessor != null)
        {
            return attribute.ScaledLightmapUvAccessor;
        }

        var source = (Vector2[])attribute.Data;
        var scaled = new Vector2[source.Length];

        for (var i = 0; i < source.Length; i++)
        {
            scaled[i] = source[i] * LightmapUvScale;
        }

        attribute.ScaledLightmapUvAccessor = CreateAccessor(exportedModel, scaled);
        return attribute.ScaledLightmapUvAccessor;
    }

    private static Accessor CreateAccessor(ModelRoot exportedModel, float[] values)
    {
        SanitizeNonFinite(values);

        var bufferView = exportedModel.CreateBufferView(sizeof(float) * values.Length, 0, BufferMode.ARRAY_BUFFER);
        new ScalarArray(bufferView.Content).Fill(values);

        var accessor = exportedModel.CreateAccessor();
        accessor.SetVertexData(bufferView, 0, values.Length, AttributeFormat.Float1);

        return accessor;
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

    /// <summary>
    /// Creates a normalized unsigned byte accessor from values in the [0, 1] range.
    /// </summary>
    private static Accessor CreateUnorm8Accessor(ModelRoot exportedModel, Vector4[] vectors)
    {
        var floats = MemoryMarshal.Cast<Vector4, float>(vectors.AsSpan());
        var bufferView = exportedModel.CreateBufferView(floats.Length, 4, BufferMode.ARRAY_BUFFER);
        var bytes = ((Memory<byte>)bufferView.Content).Span;

        for (var i = 0; i < floats.Length; i++)
        {
            bytes[i] = (byte)Math.Clamp(MathF.Round(floats[i] * 255f), 0f, 255f);
        }

        var accessor = exportedModel.CreateAccessor();
        accessor.SetVertexData(bufferView, 0, vectors.Length, new AttributeFormat(DimensionType.VEC4, EncodingType.UNSIGNED_BYTE, true));

        return accessor;
    }

    /// <summary>
    /// Creates a JOINTS accessor from four joint indices per vertex, as unsigned bytes when they all fit.
    /// </summary>
    private static Accessor CreateJointsAccessor(ModelRoot exportedModel, ushort[] joints)
    {
        var maxJoint = joints.Length > 0 ? TensorPrimitives.Max<ushort>(joints) : 0;

        var accessor = exportedModel.CreateAccessor();
        var count = joints.Length / 4;

        if (maxJoint <= byte.MaxValue)
        {
            var bytesView = exportedModel.CreateBufferView(joints.Length, 4 * sizeof(byte), BufferMode.ARRAY_BUFFER);
            TensorPrimitives.ConvertTruncating<ushort, byte>(joints, ((Memory<byte>)bytesView.Content).Span);

            accessor.SetVertexData(bytesView, 0, count, new AttributeFormat(DimensionType.VEC4, EncodingType.UNSIGNED_BYTE));

            return accessor;
        }

        var bufferView = exportedModel.CreateBufferView(sizeof(ushort) * joints.Length, 4 * sizeof(ushort), BufferMode.ARRAY_BUFFER);
        joints.CopyTo(MemoryMarshal.Cast<byte, ushort>(((Memory<byte>)bufferView.Content).Span));

        accessor.SetVertexData(bufferView, 0, count, new AttributeFormat(DimensionType.VEC4, EncodingType.UNSIGNED_SHORT));

        return accessor;
    }

    /// <summary>
    /// Sets the index accessor, using unsigned shorts when every index fits. 65535 is left out because
    /// some APIs treat it as the primitive restart value.
    /// </summary>
    private static void SetIndices(MeshPrimitive primitive, PrimitiveType primitiveType, int[] indices)
    {
        var maxIndex = indices.Length > 0 ? TensorPrimitives.Max<int>(indices) : 0;

        var model = primitive.LogicalParent.LogicalParent;
        var accessor = model.CreateAccessor();

        if (maxIndex > ushort.MaxValue - 1)
        {
            var intView = model.CreateBufferView(sizeof(int) * indices.Length, 0, BufferMode.ELEMENT_ARRAY_BUFFER);
            indices.CopyTo(MemoryMarshal.Cast<byte, int>(((Memory<byte>)intView.Content).Span));
            accessor.SetIndexData(intView, 0, indices.Length, IndexEncodingType.UNSIGNED_INT);
        }
        else
        {
            var shortView = model.CreateBufferView(sizeof(ushort) * indices.Length, 0, BufferMode.ELEMENT_ARRAY_BUFFER);
            var shorts = MemoryMarshal.Cast<byte, ushort>(((Memory<byte>)shortView.Content).Span);

            TensorPrimitives.ConvertToUInt16(indices, shorts);

            accessor.SetIndexData(shortView, 0, indices.Length, IndexEncodingType.UNSIGNED_SHORT);
        }

        primitive.SetIndexAccessor(accessor);
        primitive.DrawPrimitiveType = primitiveType;
    }

    private static void FixZeroLengthVectors(Span<Vector4> vectorArray)
    {
        for (var i = 0; i < vectorArray.Length; i++)
        {
            var vec = vectorArray[i];

            if (Math.Abs(vec.AsVector3().Length() - 1.0f) > UnitLengthThresholdVec3)
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

    /// <summary>
    /// Gets the positions pushed out along the normals, so overlays do not z-fight with the surface under them.
    /// </summary>
    private static Accessor GetOverlayPositionAccessor(ModelRoot exportedModel, VertexAttribute positionAttribute, VertexAttribute normalAttribute)
    {
        if (positionAttribute.OverlayPositionAccessors.TryGetValue(normalAttribute, out var offsetAccessor))
        {
            return offsetAccessor;
        }

        var positions = (Vector3[])positionAttribute.Data;
        var normals = (Vector3[])normalAttribute.Data;
        var updatedPositions = new Vector3[positions.Length];

        var positionFloats = MemoryMarshal.Cast<Vector3, float>(positions);
        var normalFloats = MemoryMarshal.Cast<Vector3, float>(normals.AsSpan(0, positions.Length));
        var updatedFloats = MemoryMarshal.Cast<Vector3, float>(updatedPositions.AsSpan());

        TensorPrimitives.MultiplyAddEstimate(normalFloats, OverlayNormalOffsetDistance, positionFloats, updatedFloats);

        offsetAccessor = CreateAccessor(exportedModel, updatedPositions);
        positionAttribute.OverlayPositionAccessors.Add(normalAttribute, offsetAccessor);
        return offsetAccessor;
    }
}
