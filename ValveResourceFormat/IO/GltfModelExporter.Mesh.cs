using System.IO;
using System.Linq;
using SharpGLTF.Schema2;
using ValveResourceFormat.Serialization;
using VMesh = ValveResourceFormat.ResourceTypes.Mesh;
using VMaterial = ValveResourceFormat.ResourceTypes.Material;
using VMorph = ValveResourceFormat.ResourceTypes.Morph;
using ValveResourceFormat.Blocks;
using System.Runtime.InteropServices;
using System.Text.Json.Nodes;
using SharpGLTF.Memory;

namespace ValveResourceFormat.IO;

public partial class GltfModelExporter
{
    // https://github.com/KhronosGroup/glTF-Validator/blob/master/lib/src/errors.dart
    private const float UnitLengthThresholdVec3 = 0.00674f;

    private Mesh CreateGltfMesh(string meshName, VMesh vmesh, VBIB vbib, ModelRoot exportedModel, bool includeJoints, string skinMaterialPath)
    {
        ProgressReporter?.Report($"Creating mesh: {meshName}");

        var mesh = exportedModel.CreateMesh(meshName);
        mesh.Name = meshName;

        mesh.Extras = new JsonObject();

        vmesh.LoadExternalMorphData(FileLoader);

        var vertexBufferAccessors = vbib.VertexBuffers.Select((vertexBuffer, vertexBufferIndex) =>
        {
            var accessors = new Dictionary<string, Accessor>();

            if (vertexBuffer.ElementCount == 0)
            {
                return accessors;
            }

            // Avoid duplicate attribute names
            var attributeCounters = new Dictionary<string, int>();

            // Set vertex attributes
            var actualJointsCount = 0;
            foreach (var attribute in vertexBuffer.InputLayoutFields.OrderBy(i => i.SemanticIndex).ThenBy(i => i.Offset))
            {
                if (!includeJoints && attribute.SemanticName == "BLENDINDICES")
                {
                    continue;
                }

                var attributeFormat = VBIB.GetFormatInfo(attribute);
                var accessorName = attribute.SemanticName switch
                {
                    "TEXCOORD" when attributeFormat.ElementCount == 2 => "TEXCOORD",
                    "COLOR" => "COLOR",
                    "POSITION" => "POSITION",
                    "NORMAL" => "NORMAL",
                    "TANGENT" => "TANGENT",
                    "BLENDINDICES" => "JOINTS_0",
                    "BLENDWEIGHT" or "BLENDWEIGHTS" => "WEIGHTS_0",
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

                if (attribute.SemanticName == "NORMAL")
                {
                    var (normals, tangents) = VBIB.GetNormalTangentArray(vertexBuffer, attribute);

                    normals = FixZeroLengthVectors(normals);

                    if (tangents.Length > 0)
                    {
                        tangents = FixZeroLengthVectors(tangents);

                        accessors["NORMAL"] = CreateAccessor(exportedModel, normals);
                        accessors["TANGENT"] = CreateAccessor(exportedModel, tangents);
                    }
                    else
                    {
                        accessors[accessorName] = CreateAccessor(exportedModel, normals);
                    }
                }
                else if (attribute.SemanticName == "BLENDINDICES")
                {
                    actualJointsCount = attributeFormat.ElementCount;

                    var indices = VBIB.GetBlendIndicesArray(vertexBuffer, attribute);

                    var bufferView = exportedModel.CreateBufferView(2 * indices.Length, 0, BufferMode.ARRAY_BUFFER);
                    indices.CopyTo(MemoryMarshal.Cast<byte, ushort>(((Memory<byte>)bufferView.Content).Span));
                    var accessor = mesh.LogicalParent.CreateAccessor();
                    accessor.SetVertexData(bufferView, 0, indices.Length / 4, DimensionType.VEC4, EncodingType.UNSIGNED_SHORT);
                    accessors[accessorName] = accessor;
                }
                else if (attribute.SemanticName is "BLENDWEIGHT" or "BLENDWEIGHTS")
                {
                    var weights = VBIB.GetBlendWeightsArray(vertexBuffer, attribute);
                    accessors[accessorName] = CreateAccessor(exportedModel, weights);
                }
                else
                {
                    switch (attributeFormat.ElementCount)
                    {
                        case 1:
                            {
                                var buffer = VBIB.GetScalarAttributeArray(vertexBuffer, attribute);
                                var bufferView = exportedModel.CreateBufferView(4 * buffer.Length, 0, BufferMode.ARRAY_BUFFER);
                                new ScalarArray(bufferView.Content).Fill(buffer);
                                var accessor = exportedModel.CreateAccessor();
                                accessor.SetVertexData(bufferView, 0, buffer.Length, DimensionType.SCALAR);
                                accessors[accessorName] = accessor;
                                break;
                            }

                        case 2:
                            {
                                var vectors = VBIB.GetVector2AttributeArray(vertexBuffer, attribute);
                                accessors[accessorName] = CreateAccessor(exportedModel, vectors);
                                break;
                            }
                        case 3:
                            {
                                var vectors = VBIB.GetVector3AttributeArray(vertexBuffer, attribute);
                                accessors[accessorName] = CreateAccessor(exportedModel, vectors);
                                break;
                            }
                        case 4:
                            {
                                var vectors = VBIB.GetVector4AttributeArray(vertexBuffer, attribute);

                                if (accessorName == "TANGENT")
                                {
                                    vectors = FixZeroLengthVectors(vectors);
                                }

                                accessors[accessorName] = CreateAccessor(exportedModel, vectors);
                                break;
                            }

                        default:
                            throw new NotImplementedException($"Attribute \"{attribute.SemanticName}\" has {attributeFormat.ElementCount} components");
                    }
                }
            }

            if (accessors.TryGetValue("JOINTS_0", out var jointAccessor))
            {
                // For some reason models can have joints but no weights, check if that is the case
                if (!accessors.TryGetValue("WEIGHTS_0", out var weightsAccessor))
                {
                    // If this occurs, give default weights
                    var baseWeight = 1f / actualJointsCount;
                    var baseWeights = new Vector4(
                        actualJointsCount > 0 ? baseWeight : 0,
                        actualJointsCount > 1 ? baseWeight : 0,
                        actualJointsCount > 2 ? baseWeight : 0,
                        actualJointsCount > 3 ? baseWeight : 0
                    );
                    var defaultWeights = Enumerable.Repeat(baseWeights, jointAccessor.Count).ToList();

                    var bufferView = exportedModel.CreateBufferView(16 * defaultWeights.Count, 0, BufferMode.ARRAY_BUFFER);
                    new Vector4Array(bufferView.Content).Fill(defaultWeights);
                    weightsAccessor = exportedModel.CreateAccessor();
                    weightsAccessor.SetVertexData(bufferView, 0, defaultWeights.Count, DimensionType.VEC4);
                    accessors["WEIGHTS_0"] = weightsAccessor;
                }

                var joints = MemoryMarshal.Cast<byte, ushort>(((Memory<byte>)jointAccessor.SourceBufferView.Content).Span);
                var weights = MemoryMarshal.Cast<byte, float>(((Memory<byte>)weightsAccessor.SourceBufferView.Content).Span);

                for (var i = 0; i < joints.Length; i += 4)
                {
                    // remove joints without weights
                    for (var j = 0; j < 4; j++)
                    {
                        if (weights[i + j] == 0)
                        {
                            joints[i + j] = 0;
                        }
                    }

                    // remove duplicate joints
                    for (var j = 2; j >= 0; j--)
                    {
                        for (var k = 3; k > j; k--)
                        {
                            if (joints[i + j] == joints[i + k])
                            {
                                for (var l = k; l < 3; l++)
                                {
                                    joints[i + l] = joints[i + l + 1];
                                }
                                joints[i + 3] = 0;

                                weights[i + j] += weights[i + k];
                                for (var l = k; l < 3; l++)
                                {
                                    weights[i + l] = weights[i + l + 1];
                                }
                                weights[i + 3] = 0;
                            }
                        }
                    }
                }

                jointAccessor.UpdateBounds();
                weightsAccessor.UpdateBounds();
            }

            return accessors;
        }).ToArray();

        var vertexOffset = 0;

        foreach (var sceneObject in vmesh.Data.GetArray("m_sceneObjects"))
        {
            foreach (var drawCall in sceneObject.GetArray("m_drawCalls"))
            {
                CancellationToken.ThrowIfCancellationRequested();
                var vertexBufferInfo = drawCall.GetArray("m_vertexBuffers")[0]; // In what situation can we have more than 1 vertex buffer per draw call?
                var vertexBufferIndex = vertexBufferInfo.GetInt32Property("m_hBuffer");

                var indexBufferInfo = drawCall.GetSubCollection("m_indexBuffer");
                var indexBufferIndex = indexBufferInfo.GetInt32Property("m_hBuffer");
                var indexBuffer = vbib.IndexBuffers[indexBufferIndex];

                // Create one primitive per draw call
                var primitive = mesh.CreatePrimitive();

                foreach (var (attributeKey, accessor) in vertexBufferAccessors[vertexBufferIndex])
                {
                    primitive.SetVertexAccessor(attributeKey, accessor);

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
                        primitive.WithIndicesAccessor(PrimitiveType.TRIANGLES, indices);
                        break;
                    default:
                        throw new NotImplementedException($"Unknown PrimitiveType in drawCall! {primitiveType}");
                }

                if (vmesh.MorphData != null)
                {
                    var flexData = vmesh.MorphData.GetFlexVertexData();
                    if (flexData != null)
                    {
                        var vertexCount = drawCall.GetInt32Property("m_nVertexCount");
                        AddMorphTargetsToPrimitive(vmesh.MorphData, flexData, primitive, exportedModel, vertexOffset, vertexCount);
                        vertexOffset += vertexCount;
                    }
                }

                DebugValidateGLTF();

                // Add material
                if (!ExportMaterials)
                {
                    continue;
                }

                var materialPath = skinMaterialPath ?? drawCall.GetProperty<string>("m_material") ?? drawCall.GetProperty<string>("m_pMaterial");

                var materialNameTrimmed = Path.GetFileNameWithoutExtension(materialPath);

                // Check if material already exists - makes an assumption that if material has the same name it is a duplicate
                var existingMaterial = exportedModel.LogicalMaterials.SingleOrDefault(m => m.Name == materialNameTrimmed);
                if (existingMaterial != null)
                {
                    ProgressReporter?.Report($"Found existing material: {materialNameTrimmed}");
                    primitive.Material = existingMaterial;
                    continue;
                }

                ProgressReporter?.Report($"Loading material: {materialPath}");

                var materialResource = FileLoader.LoadFileCompiled(materialPath);

                if (materialResource == null)
                {
                    continue;
                }

                var material = exportedModel
                    .CreateMaterial(materialNameTrimmed)
                    .WithDefault();
                primitive.WithMaterial(material);

                var renderMaterial = (VMaterial)materialResource.DataBlock;

                var task = GenerateGLTFMaterialFromRenderMaterial(material, renderMaterial, exportedModel);
                MaterialGenerationTasks.Add(task);
            }
        }

        return mesh;
    }

    private static void AddMorphTargetsToPrimitive(VMorph morph, Dictionary<string, Vector3[]> flexData, MeshPrimitive primitive, ModelRoot model, int vertexOffset, int vertexCount)
    {
        var morphIndex = 0;
        var flexDesc = morph.GetFlexDescriptors();

        foreach (var morphName in flexDesc)
        {
            if (!flexData.TryGetValue(morphName, out var rectData))
            {
                continue;
            }

            var bufferView = model.CreateBufferView(3 * sizeof(float) * vertexCount, 0, BufferMode.ARRAY_BUFFER);
            new Vector3Array(bufferView.Content).Fill(rectData[vertexOffset..(vertexOffset + vertexCount)]);

            var acc = model.CreateAccessor();
            acc.Name = morphName;
            acc.SetData(bufferView, 0, vertexCount, DimensionType.VEC3, EncodingType.FLOAT, false);

            var dict = new Dictionary<string, Accessor>
                {
                    { "POSITION", acc }
                };

            primitive.SetMorphTargetAccessors(morphIndex++, dict);
        }

        DebugValidateGLTF();
    }

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
            var shortIndices = new ushort[count];
            System.Buffer.BlockCopy(indexBuffer.Data, byteStart, shortIndices, 0, byteCount);
            indices = Array.ConvertAll(shortIndices, i => baseVertex + i);
        }

        return indices;
    }

    private static Accessor CreateAccessor(ModelRoot exportedModel, Vector2[] vectors)
    {
        var bufferView = exportedModel.CreateBufferView(2 * sizeof(float) * vectors.Length, 0, BufferMode.ARRAY_BUFFER);
        new Vector2Array(bufferView.Content).Fill(vectors);

        var accessor = exportedModel.CreateAccessor();
        accessor.SetVertexData(bufferView, 0, vectors.Length, DimensionType.VEC2);

        return accessor;
    }

    private static Accessor CreateAccessor(ModelRoot exportedModel, Vector3[] vectors)
    {
        var bufferView = exportedModel.CreateBufferView(3 * sizeof(float) * vectors.Length, 0, BufferMode.ARRAY_BUFFER);
        new Vector3Array(bufferView.Content).Fill(vectors);

        var accessor = exportedModel.CreateAccessor();
        accessor.SetVertexData(bufferView, 0, vectors.Length, DimensionType.VEC3);

        return accessor;
    }

    private static Accessor CreateAccessor(ModelRoot exportedModel, Vector4[] vectors)
    {
        var bufferView = exportedModel.CreateBufferView(4 * sizeof(float) * vectors.Length, 0, BufferMode.ARRAY_BUFFER);
        new Vector4Array(bufferView.Content).Fill(vectors);

        var accessor = exportedModel.CreateAccessor();
        accessor.SetVertexData(bufferView, 0, vectors.Length, DimensionType.VEC4);

        return accessor;
    }

    private static Vector4[] FixZeroLengthVectors(Vector4[] vectorArray)
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

        return vectorArray;
    }

    private static Vector3[] FixZeroLengthVectors(Vector3[] vectorArray)
    {
        for (var i = 0; i < vectorArray.Length; i++)
        {
            if (Math.Abs(vectorArray[i].Length() - 1.0f) > UnitLengthThresholdVec3)
            {
                vectorArray[i] = -Vector3.UnitZ;
            }
        }

        return vectorArray;
    }
}
