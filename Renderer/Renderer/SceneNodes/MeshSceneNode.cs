using System.Runtime.InteropServices;
using ValveResourceFormat.Blocks;
using ValveResourceFormat.ResourceTypes;

namespace ValveResourceFormat.Renderer.SceneNodes
{
    /// <summary>
    /// Scene node that renders a single mesh.
    /// </summary>
    public class MeshSceneNode : MeshCollectionNode
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="MeshSceneNode"/> class from a raw mesh resource.
        /// </summary>
        /// <param name="scene">The scene this node belongs to.</param>
        /// <param name="mesh">The mesh resource to render.</param>
        /// <param name="meshIndex">The index of this mesh within its parent model.</param>
        public MeshSceneNode(Scene scene, Mesh mesh, int meshIndex)
            : base(scene)
        {
            var meshRenderer = new RenderableMesh(mesh, meshIndex, Scene);
            RenderableMeshes = [meshRenderer];
            LocalBoundingBox = meshRenderer.BoundingBox;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="MeshSceneNode"/> class from an already-constructed renderable mesh.
        /// </summary>
        /// <param name="scene">The scene this node belongs to.</param>
        /// <param name="renderableMesh">The pre-built renderable mesh to use.</param>
        public MeshSceneNode(Scene scene, RenderableMesh renderableMesh)
            : base(scene)
        {
            RenderableMeshes = [renderableMesh];
            LocalBoundingBox = renderableMesh.BoundingBox;
        }

        /// <inheritdoc/>
        public override IEnumerable<string> GetSupportedRenderModes() => RenderableMeshes[0].GetSupportedRenderModes();

#if DEBUG
        /// <inheritdoc/>
        public override void UpdateVertexArrayObjects() => RenderableMeshes[0].UpdateVertexArrayObjects();
#endif

        [StructLayout(LayoutKind.Sequential)]
        private readonly struct Vertex
        {
            [VertexAttribute(VertexSlot.Position)] public readonly Vector3 Position;
            [VertexAttribute(VertexSlot.Normal)] public readonly Vector3 Normal;
            [VertexAttribute(VertexSlot.Tangent)] public readonly Vector4 TangentU_SignV;
            [VertexAttribute(VertexSlot.TexCoord)] public readonly Vector2 UV;
            [VertexAttribute(VertexSlot.TexCoord4)] public readonly Color32 VertexPaintBlendParams;

            /// <summary>The layout of this vertex, for creating vertex array objects.</summary>
            public static readonly VertexInputLayout InputLayout = VertexInputLayout.FromStruct<Vertex>();

            public Vertex(Vector3 position, Vector2 uv, Color32 vertexPaint, Vector3? normal = null, Vector4? tangentU_SignV = null)
            {
                Position = position;
                UV = uv;
                VertexPaintBlendParams = vertexPaint;
                Normal = normal ?? new Vector3(0.0f, 0.0f, 1.0f);
                TangentU_SignV = tangentU_SignV ?? new Vector4(1.0f, 0.0f, 0.0f, 1.0f);
            }
        }

        /// <summary>
        /// Creates a flat quad mesh node suitable for previewing a material, with vertex paint gradient strips.
        /// </summary>
        /// <param name="scene">The scene to add the node to.</param>
        /// <param name="material">The material to display on the quad.</param>
        /// <param name="size">The width and height of the quad in world units.</param>
        public static MeshSceneNode CreateMaterialPreviewQuad(Scene scene, RenderMaterial material, Vector2 size)
        {
            var half = size / 2.0f;

            Span<Vertex> vertices =
            [
                new(new(-half.X, half.Y, 0f), new(0f, 0f), Color32.Black with { A = 0 }),
                new(new(half.X, half.Y, 0f), new(1f, 0f), Color32.Black),
                new(new(-half.X, half.Y / 2f, 0f), new(0f, 0.25f), Color32.Green with { A = 0 }),
                new(new(half.X, half.Y / 2f, 0f), new(1f, 0.25f), Color32.Green),
                new(new(-half.X, 0f, 0f), new(0f, 0.5f), Color32.White with { A = 0 }),
                new(new(half.X, 0f, 0f), new(1f, 0.5f),  Color32.White ),
                new(new(-half.X, -half.Y / 2f, 0f), new(0f, 0.75f), Color32.Red with { A = 0 }),
                new(new(half.X, -half.Y / 2f, 0f), new(1f, 0.75f), Color32.Red),
                new(new(-half.X, -half.Y, 0f), new(0f, 1f), Color32.Blue with { A = 0}),
                new(new(half.X, -half.Y, 0f), new(1f, 1f), Color32.Blue),
            ];

            Span<uint> indices =
            [
                2, 3, 1,
                2, 1, 0,
                4, 5, 3,
                4, 3, 2,
                6, 7, 5,
                6, 5, 4,
                8, 9, 7,
                8, 7, 6,
            ];

            return CreateMesh(scene, "MaterialPreviewQuad", material, vertices, indices);
        }

        /// <summary>
        /// Creates a sphere mesh node suitable for previewing a material, with texture coordinates wrapping
        /// once around it and running from pole to pole.
        /// </summary>
        /// <param name="scene">The scene to add the node to.</param>
        /// <param name="material">The material to display on the sphere.</param>
        /// <param name="radius">The radius of the sphere in world units.</param>
        public static MeshSceneNode CreateMaterialPreviewSphere(Scene scene, RenderMaterial material, float radius)
        {
            const int Segments = 64;
            const int Rings = 32;

            // The seam and poles repeat vertices so each can carry its own texture coordinate
            var vertices = new Vertex[(Rings + 1) * (Segments + 1)];
            var indices = new uint[Rings * Segments * 6];

            for (var ring = 0; ring <= Rings; ring++)
            {
                var v = (float)ring / Rings;
                var (sinTheta, cosTheta) = MathF.SinCos(MathF.PI * v);

                for (var segment = 0; segment <= Segments; segment++)
                {
                    var u = (float)segment / Segments;
                    var (sinPhi, cosPhi) = MathF.SinCos(MathF.Tau * u);

                    var normal = new Vector3(sinTheta * cosPhi, sinTheta * sinPhi, cosTheta);

                    // Along increasing u; the bitangent then points towards the top pole, as on the quad
                    var tangent = new Vector4(-sinPhi, cosPhi, 0f, 1f);

                    vertices[ring * (Segments + 1) + segment] = new Vertex(normal * radius, new Vector2(u, v), Color32.Black, normal, tangent);
                }
            }

            var index = 0;

            for (var ring = 0; ring < Rings; ring++)
            {
                for (var segment = 0; segment < Segments; segment++)
                {
                    var current = (uint)(ring * (Segments + 1) + segment);
                    var below = current + Segments + 1;

                    indices[index++] = current;
                    indices[index++] = below;
                    indices[index++] = current + 1;

                    indices[index++] = current + 1;
                    indices[index++] = below;
                    indices[index++] = below + 1;
                }
            }

            return CreateMesh(scene, "MaterialPreviewSphere", material, vertices, indices);
        }

        private static MeshSceneNode CreateMesh(Scene scene, string name, RenderMaterial material, ReadOnlySpan<Vertex> vertices, ReadOnlySpan<uint> indices)
        {
            var vbib = new VBIB() { Resource = null! };

            var bounds = new AABB();
            foreach (var vertex in vertices)
            {
                bounds = bounds.Encapsulate(vertex.Position);
            }

            // Vertex buffer with interleaved data
            vbib.VertexBuffers.Add(new VBIB.OnDiskBufferData
            {
                ElementCount = (uint)vertices.Length,
                ElementSizeInBytes = (uint)Vertex.InputLayout.Stride,
                Data = MemoryMarshal.Cast<Vertex, byte>(vertices).ToArray(),
                InputLayoutFields = Vertex.InputLayout.Fields(),
            });

            vbib.IndexBuffers.Add(new VBIB.OnDiskBufferData
            {
                ElementCount = (uint)indices.Length,
                ElementSizeInBytes = sizeof(uint),
                Data = MemoryMarshal.Cast<uint, byte>(indices).ToArray(),
                InputLayoutFields = []
            });

            var renderableMesh = RenderableMesh.CreateMesh(name, material, vbib, bounds, scene.RendererContext);
            return new MeshSceneNode(scene, renderableMesh);
        }
    }
}
