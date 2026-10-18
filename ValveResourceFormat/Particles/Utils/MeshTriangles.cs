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
        /// <summary>A vertex and the model bones that move it.</summary>
        /// <param name="Position">The position in model space.</param>
        /// <param name="Bones">Four model bone indices, 16 bits each from the low bits up.</param>
        /// <param name="Weights">The weights of the four bones. All zero for a rigid vertex.</param>
        /// <param name="Remapped">Whether the bones were mapped from the mesh's own bone list to the model's.</param>
        public readonly record struct Vertex(Vector3 Position, ulong Bones, Vector4 Weights, bool Remapped);

        private static readonly ModelCache<MeshTriangles> Cache = new(static (model, fileLoader) => new MeshTriangles(model, fileLoader));

        private readonly List<Vertex> vertices = [];
        private readonly List<int> indices = [];
        private readonly List<float> areas = [];

        /// <summary>The inverse of every bone's bind pose in model space.</summary>
        public Matrix4x4[] InverseBindPose { get; }

        /// <summary>How many triangles the meshes have.</summary>
        public int TriangleCount => areas.Count;

        private MeshTriangles(Model model, IFileLoader? fileLoader)
        {
            InverseBindPose = BindPoseModelObject.GetInverseBindPose(model);

            foreach (var (mesh, meshIndex) in ModelElements.RenderMeshes(model, fileLoader))
            {
                AddMesh(mesh, model.GetRemapTable(meshIndex));
            }
        }

        /// <summary>The triangles of <paramref name="model"/>, read once per model.</summary>
        public static MeshTriangles Get(Model model, IFileLoader? fileLoader)
            => Cache.Get(model, fileLoader);

        /// <summary>The area of triangle <paramref name="triangle"/> in model space.</summary>
        public float Area(int triangle) => areas[triangle];

        /// <summary>Corner <paramref name="corner"/> of triangle <paramref name="triangle"/>.</summary>
        public Vertex Corner(int triangle, int corner) => vertices[indices[(triangle * 3) + corner]];

        private void AddMesh(Mesh mesh, int[]? remapTable)
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
                        AddVertices(vbib.VertexBuffers[vertexBuffer], remapTable);
                    }

                    var indexBuffer = vbib.IndexBuffers[drawCall.GetSubCollection("m_indexBuffer").GetInt32Property("m_hBuffer")];
                    AddTriangles(indexBuffer, drawCall.GetInt32Property("m_nStartIndex"), drawCall.GetInt32Property("m_nIndexCount"),
                        start + drawCall.GetInt32Property("m_nBaseVertex"));
                }
            }
        }

        private void AddVertices(VBIB.OnDiskBufferData buffer, int[]? remapTable)
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
                        blendIndices = VBIB.GetBlendIndicesArray(buffer, attribute, remapTable);
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
                var bones = 0UL;

                for (var k = 0; k < PackedBones.Count; k++)
                {
                    var bone = k < jointStride && weights[k] > 0f ? blendIndices![(i * jointStride) + k] : PackedBones.None;
                    bones |= PackedBones.Pack(bone, k);
                }

                vertices.Add(new Vertex(position, bones, weights, remapTable != null));
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
