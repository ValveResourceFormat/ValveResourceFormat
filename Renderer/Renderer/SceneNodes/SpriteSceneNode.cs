using System.Runtime.InteropServices;
using OpenTK.Graphics.OpenGL;
using PrimitiveType = OpenTK.Graphics.OpenGL.PrimitiveType;

namespace ValveResourceFormat.Renderer.SceneNodes
{
    class SpriteSceneNode : SceneNode
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct Vertex
        {
            [VertexAttribute(VertexSlot.Position)] public Vector3 Position;
            [VertexAttribute(VertexSlot.TexCoord)] public Vector2 TexCoord;
            [VertexAttribute(VertexSlot.Color)] public Color32 Color;

            /// <summary>The layout of this vertex, for creating vertex array objects.</summary>
            public static readonly VertexInputLayout InputLayout = VertexInputLayout.FromStruct<Vertex>();
        }

        private static readonly Vertex[] Vertices =
        [
            NewVertex(new(1.0f, -1.0f, 0.0f), new(1.0f, 1.0f)),
            NewVertex(new(1.0f, 1.0f, 0.0f), new(1.0f, 0.0f)),
            NewVertex(new(-1.0f, -1.0f, 0.0f), new(0.0f, 1.0f)),
            NewVertex(new(-1.0f, 1.0f, 0.0f), new(0.0f, 0.0f)),
        ];

        // The color is white. If a material shader reads vCOLOR, it gets the default of the engine.
        // GPUMeshBufferCache binds the same value for a mesh with no color stream.
        private static Vertex NewVertex(Vector3 position, Vector2 texCoord) => new()
        {
            Position = position,
            TexCoord = texCoord,
            Color = Color32.White,
        };

        private readonly int vao;
        private readonly RenderMaterial material;
        private readonly float spriteSize;

        public SpriteSceneNode(Scene scene, RendererContext renderContext, Resource resource, Vector3 position)
            : base(scene)
        {
            material = renderContext.MaterialLoader.LoadMaterial(resource, scene.LightingInfo.CreateShaderArguments());

            // Forcefully clamp sprites so they don't render extra pixels on edges
            foreach (var texture in material.Textures.Values)
            {
                texture.SetWrapMode(RsTextureAddressMode.Clamp);
            }

            var label = $"{nameof(SpriteSceneNode)}: {System.IO.Path.GetFileName(resource.FileName)}";
            var vboHandle = GraphicsDevice.CreateBuffer<Vertex>(label, Vertices, BufferUsage.Static);

            vao = Vertex.InputLayout.CreateVertexArray(label, vboHandle);

            spriteSize = material.FloatParams.GetValueOrDefault("g_flUniformPointSize", 16);
            spriteSize /= 2f; // correct the scale to actually be 16x16

            LocalBoundingBox = new AABB(-Vector3.One, Vector3.One);
            Transform = Matrix4x4.CreateTranslation(position.X, position.Y, position.Z);

            RenderPasses |= CustomRenderPasses.DepthOnly;
        }

        public override void Update(Scene.UpdateContext context)
        {
            Transform = Matrix4x4.CreateScale(spriteSize * PlacementScale)
                * context.Camera.BillboardMatrix
                * Matrix4x4.CreateTranslation(Transform.Translation);
        }

        public override void Render(Scene.RenderContext context)
        {
            if (context.RenderPass is not RenderPass.Opaque and not RenderPass.Outline and not RenderPass.DepthOnly)
            {
                return;
            }

            var renderShader = context.DepthOnlyShader?.WithAlphaTest(material.IsAlphaTest)
                ?? context.ReplacementShader
                ?? material.Shader;

            renderShader.Use();

            VertexArray.Bind(vao, renderShader);

            renderShader.SetUniform1("shaderId", material.Shader.NameHash);
            renderShader.SetUniform1("shaderProgramId", (uint)material.Shader.Program);

            material.Render(renderShader);

            GL.DrawArraysInstancedBaseInstance(PrimitiveType.TriangleStrip, 0, 4, 1, Id);

            material.PostRender();
        }
    }
}
