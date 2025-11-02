using System.Runtime.InteropServices;
using GUI.Utils;
using ValveResourceFormat;
using ValveResourceFormat.Blocks;
using ValveResourceFormat.ResourceTypes;

namespace GUI.Types.Renderer
{
    class MeshSceneNode : MeshCollectionNode
    {
        public Vector4 Tint
        {
            get => RenderableMeshes[0].Tint;
            set => RenderableMeshes[0].Tint = value;
        }

        public MeshSceneNode(Scene scene, Mesh mesh, int meshIndex)
            : base(scene)
        {
            var meshRenderer = new RenderableMesh(mesh, meshIndex, Scene);
            RenderableMeshes = [meshRenderer];
            LocalBoundingBox = meshRenderer.BoundingBox;
        }

        public MeshSceneNode(Scene scene, RenderableMesh renderableMesh)
            : base(scene)
        {
            RenderableMeshes = [renderableMesh];
            LocalBoundingBox = renderableMesh.BoundingBox;
        }

        public override IEnumerable<string> GetSupportedRenderModes() => RenderableMeshes[0].GetSupportedRenderModes();

#if DEBUG
        public override void UpdateVertexArrayObjects() => RenderableMeshes[0].UpdateVertexArrayObjects();
#endif


        [StructLayout(LayoutKind.Sequential)]
        private readonly struct Vertex
        {
            public readonly Vector3 Position;
            public readonly Vector3 Normal;
            public readonly Vector4 TangentU_SignV;
            public readonly Vector2 UV;
            public readonly Color32 VertexPaintBlendParams;

            public Vertex(Vector3 position, Vector2 uv, Color32 vertexPaint, Vector3? normal = null, Vector4? tangentU_SignV = null)
            {
                Position = position;
                UV = uv;
                VertexPaintBlendParams = vertexPaint;
                Normal = normal ?? new Vector3(0.0f, 0.0f, 1.0f);
                TangentU_SignV = tangentU_SignV ?? new Vector4(1.0f, 0.0f, 0.0f, 1.0f);
            }
        }

        public static MeshSceneNode CreateMaterialPreviewQuad(Scene scene, RenderMaterial material, Vector2 size)
        {
            var vbib = new VBIB();
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

            var bounds = new AABB();
            foreach (var vertex in vertices)
            {
                bounds = bounds.Encapsulate(vertex.Position);
            }

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

            // Vertex buffer with interleaved data
            vbib.VertexBuffers.Add(new VBIB.OnDiskBufferData
            {
                ElementCount = (uint)vertices.Length,
                ElementSizeInBytes = (uint)Marshal.SizeOf<Vertex>(),
                Data = MemoryMarshal.Cast<Vertex, byte>(vertices).ToArray(),
                InputLayoutFields =
                [
                    new()
                    {
                        SemanticName = "POSITION",
                        Format = DXGI_FORMAT.R32G32B32_FLOAT,
                        Offset = (uint)Marshal.OffsetOf<Vertex>(nameof(Vertex.Position)),
                    },
                    new()
                    {
                        SemanticName = "NORMAL",
                        Format = DXGI_FORMAT.R32G32B32_FLOAT,
                        Offset = (uint)Marshal.OffsetOf<Vertex>(nameof(Vertex.Normal)),
                    },
                    new()
                    {
                        SemanticName = "TANGENT",
                        Format = DXGI_FORMAT.R32G32B32A32_FLOAT,
                        Offset = (uint)Marshal.OffsetOf<Vertex>(nameof(Vertex.TangentU_SignV)),
                    },
                    new()
                    {
                        SemanticName = "TEXCOORD",
                        Format = DXGI_FORMAT.R32G32_FLOAT,
                        Offset = (uint)Marshal.OffsetOf<Vertex>(nameof(Vertex.UV)),
                    },
                    new()
                    {
                        SemanticName = "TEXCOORD",
                        SemanticIndex = 4,
                        ShaderSemantic = "vColorBlendValues",//nameof(Vertex.VertexPaintBlendParams),
                        Format = DXGI_FORMAT.R8G8B8A8_UNORM,
                        Offset = (uint)Marshal.OffsetOf<Vertex>(nameof(Vertex.VertexPaintBlendParams)),
                    },
                ]
            });

            // Index buffer
            vbib.IndexBuffers.Add(new VBIB.OnDiskBufferData
            {
                ElementCount = (uint)indices.Length,
                ElementSizeInBytes = sizeof(uint),
                Data = MemoryMarshal.Cast<uint, byte>(indices).ToArray(),
                InputLayoutFields = []
            });


            var renderableMesh = RenderableMesh.CreateMesh("MaterialPreviewQuad", material, vbib, bounds, scene.GuiContext);
            return new MeshSceneNode(scene, renderableMesh);
        }
    }
}
