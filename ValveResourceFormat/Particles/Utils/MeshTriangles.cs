using System.Runtime.InteropServices;
using ValveResourceFormat.Blocks;
using ValveResourceFormat.IO;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.Particles.Utils
{
    /// <summary>
    /// The triangles of a model's render meshes at its most detailed level of detail, in model space,
    /// with the bones that skin each vertex, for placing particles on the model's surface.
    /// </summary>
    internal sealed class MeshTriangles
    {
        /// <summary>A vertex and the bone slots that move it.</summary>
        /// <param name="Position">The position in model space.</param>
        /// <param name="Slots">Four bone slots, 16 bits each from the low bits up. A slot indexes the model's whole bone remap table.</param>
        /// <param name="Weights">The weights of the four bones. All zero for a rigid vertex.</param>
        /// <param name="Remapped">Whether the vertex's mesh has a slice of the bone remap table, which its slots index.</param>
        public readonly record struct Vertex(Vector3 Position, ulong Slots, Vector4 Weights, bool Remapped);

        private static readonly ModelCache<MeshTriangles> Cache = new(static (model, fileLoader) => new MeshTriangles(model, fileLoader));

        private readonly List<Vertex> vertices = [];
        private readonly List<int> indices = [];
        private readonly List<float> areas = [];

        /// <summary>How many bones the model's skeleton has.</summary>
        public int BoneCount { get; }

        /// <summary>The model bone every bone slot is posed by, all meshes concatenated.</summary>
        public ReadOnlyMemory<int> RemapTable { get; }

        /// <summary>The inverse bind pose of every bone slot, as its mesh is skinned with it.</summary>
        public Matrix4x4[] InverseBindPose { get; }

        /// <summary>How many triangles the meshes have.</summary>
        public int TriangleCount => areas.Count;

        private MeshTriangles(Model model, IFileLoader? fileLoader)
        {
            BoneCount = model.Skeleton.Bones.Length;
            RemapTable = model.BoneRemapTable.Table;
            InverseBindPose = new Matrix4x4[RemapTable.Length];

            var modelInverseBindPose = BindPoseModelObject.GetInverseBindPose(model);

            foreach (var (mesh, meshIndex) in ModelElements.RenderMeshes(model, fileLoader))
            {
                var slotStart = model.BoneRemapTable.GetMeshStart(meshIndex);
                var slotCount = model.BoneRemapTable.GetMeshBoneCount(meshIndex);

                model.GetSkinningInverseBindPose(mesh, meshIndex, modelInverseBindPose).CopyTo(InverseBindPose, slotStart);
                AddMesh(mesh, meshIndex < model.BoneRemapTable.MeshCount, slotStart, slotCount);
            }
        }

        /// <summary>The triangles of <paramref name="model"/>, read once per model.</summary>
        public static MeshTriangles Get(Model model, IFileLoader? fileLoader)
            => Cache.Get(model, fileLoader);

        /// <summary>The area of triangle <paramref name="triangle"/> in model space.</summary>
        public float Area(int triangle) => areas[triangle];

        /// <summary>Corner <paramref name="corner"/> of triangle <paramref name="triangle"/>.</summary>
        public Vertex Corner(int triangle, int corner) => vertices[indices[(triangle * 3) + corner]];

        /// <summary>The model bones of a vertex's slots, packed like the slots.</summary>
        public ulong GetModelBones(Vertex vertex)
        {
            var remapTable = RemapTable.Span;
            var bones = 0UL;

            for (var k = 0; k < PackedBones.Count; k++)
            {
                var slot = PackedBones.Get(vertex.Slots, k);
                var bone = slot < remapTable.Length ? remapTable[slot] : PackedBones.None;

                bones |= PackedBones.Pack(bone, k);
            }

            return bones;
        }

        private void AddMesh(Mesh mesh, bool remapped, int slotStart, int slotCount)
        {
            var vbib = mesh.VBIB;
            var bufferStarts = new Dictionary<int, int>();

            foreach (var sceneObject in mesh.Data.GetArray("m_sceneObjects") ?? [])
            {
                foreach (var drawCall in sceneObject.GetArray("m_drawCalls") ?? [])
                {
                    if (drawCall.GetEnumValue<RenderPrimitiveType>("m_nPrimitiveType") != RenderPrimitiveType.RENDER_PRIM_TRIANGLES)
                    {
                        continue;
                    }

                    var vertexBuffer = drawCall.GetArray("m_vertexBuffers")[0].GetInt32Property("m_hBuffer");

                    if (!bufferStarts.TryGetValue(vertexBuffer, out var start))
                    {
                        start = vertices.Count;
                        bufferStarts.Add(vertexBuffer, start);
                        AddVertices(vbib.VertexBuffers[vertexBuffer], remapped, slotStart, slotCount);
                    }

                    var indexBuffer = vbib.IndexBuffers[drawCall.GetSubCollection("m_indexBuffer").GetInt32Property("m_hBuffer")];
                    AddTriangles(indexBuffer, drawCall.GetInt32Property("m_nStartIndex"), drawCall.GetInt32Property("m_nIndexCount"),
                        start + drawCall.GetInt32Property("m_nBaseVertex"));
                }
            }
        }

        private void AddVertices(VBIB.OnDiskBufferData buffer, bool remapped, int slotStart, int slotCount)
        {
            var count = (int)buffer.ElementCount;
            Vector3[]? positions = null;
            ushort[]? blendIndices = null;
            Vector4[]? blendWeights = null;

            foreach (var attribute in buffer.InputLayoutFields)
            {
                switch (attribute.SemanticName)
                {
                    case "POSITION" when positions == null:
                        positions = VBIB.GetVector3AttributeArray(buffer, attribute);
                        break;
                    case "BLENDINDICES" when blendIndices == null:
                        blendIndices = VBIB.GetBlendIndicesArray(buffer, attribute);
                        break;
                    case "BLENDWEIGHT" or "BLENDWEIGHTS" when blendWeights == null:
                        blendWeights = VBIB.GetBlendWeightsArray(buffer, attribute);
                        break;
                }
            }

            var jointStride = blendIndices != null && count > 0 ? blendIndices.Length / count : 0;
            var weightStride = blendWeights != null && count > 0 ? blendWeights.Length / count : 0;

            for (var i = 0; i < count; i++)
            {
                var position = positions != null ? positions[i] : Vector3.Zero;
                var weights = jointStride == 0 ? Vector4.Zero
                    : weightStride == 0 ? Vector4.UnitX
                    : blendWeights![i * weightStride];
                var slots = 0UL;

                for (var k = 0; k < PackedBones.Count; k++)
                {
                    var slot = PackedBones.None;

                    if (remapped && k < jointStride && weights[k] > 0f)
                    {
                        var meshBone = blendIndices![(i * jointStride) + k];

                        if (meshBone < slotCount)
                        {
                            slot = slotStart + meshBone;
                        }
                    }

                    slots |= PackedBones.Pack(slot, k);
                }

                vertices.Add(new Vertex(position, slots, weights, remapped));
            }
        }

        private void AddTriangles(VBIB.OnDiskBufferData indexBuffer, int startIndex, int indexCount, int baseVertex)
        {
            var data = indexBuffer.Data.AsSpan();
            var vertexCount = vertices.Count;
            Span<int> corners = stackalloc int[3];

            for (var i = 0; i + 2 < indexCount; i += 3)
            {
                for (var k = 0; k < 3; k++)
                {
                    var index = startIndex + i + k;
                    corners[k] = baseVertex + (indexBuffer.ElementSizeInBytes == 4
                        ? (int)MemoryMarshal.Read<uint>(data[(index * 4)..])
                        : MemoryMarshal.Read<ushort>(data[(index * 2)..]));
                }

                if ((uint)corners[0] >= (uint)vertexCount || (uint)corners[1] >= (uint)vertexCount || (uint)corners[2] >= (uint)vertexCount)
                {
                    continue;
                }

                indices.Add(corners[0]);
                indices.Add(corners[1]);
                indices.Add(corners[2]);
                areas.Add(MathUtils.TriangleArea(vertices[corners[0]].Position, vertices[corners[1]].Position, vertices[corners[2]].Position));
            }
        }
    }
}
