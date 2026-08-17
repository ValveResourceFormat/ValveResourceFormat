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

        /// <summary>Color multiplier applied to the sprite, in gamma space.</summary>
        public Vector4 Tint { get; set; } = Vector4.One;

        public SpriteSceneNode(Scene scene, RendererContext renderContext, Resource resource, Vector3 position)
            : base(scene)
        {
            material = renderContext.MaterialLoader.LoadMaterial(resource);

            // Forcefully clamp sprites so they don't render extra pixels on edges
            foreach (var texture in material.Textures.Values)
            {
                texture.SetWrapMode(TextureWrapMode.ClampToEdge);
            }

            GL.CreateBuffers(1, out int vboHandle);

#if DEBUG
            var vaoLabel = $"{nameof(SpriteSceneNode)}: {System.IO.Path.GetFileName(resource.FileName)}";
            GL.ObjectLabel(ObjectLabelIdentifier.Buffer, vboHandle, Math.Min(GLEnvironment.MaxLabelLength, vaoLabel.Length), vaoLabel);
#endif

            GL.NamedBufferData(vboHandle, Vertices.Length * Vertex.InputLayout.Stride, Vertices, BufferUsageHint.StaticDraw);

            vao = Vertex.InputLayout.CreateVertexArray(nameof(SpriteSceneNode), vboHandle);

            spriteSize = material.FloatParams.GetValueOrDefault("g_flUniformPointSize", 16);
            spriteSize /= 2f; // correct the scale to actually be 16x16

            LocalBoundingBox = new AABB(-Vector3.One * spriteSize, Vector3.One * spriteSize);
            Transform = Matrix4x4.CreateTranslation(position.X, position.Y, position.Z);
        }

        public override void Render(Scene.RenderContext context)
        {
            if (context.RenderPass is not RenderPass.Opaque and not RenderPass.Outline)
            {
                return;
            }

            var renderShader = context.ReplacementShader ?? material.Shader;
            renderShader.Use();

            VertexArray.Bind(vao, renderShader);

            // Create billboarding rotation (always facing camera)
            if (!Matrix4x4.Decompose(context.Camera.CameraViewMatrix, out _, out var modelViewRotation, out _))
            {
                throw new InvalidOperationException("Matrix decompose failed");
            }

            modelViewRotation = Quaternion.Inverse(modelViewRotation);
            var billboardMatrix = Matrix4x4.CreateFromQuaternion(modelViewRotation);

            var transform = Matrix4x4.CreateScale(spriteSize)
                * billboardMatrix
                * Matrix4x4.CreateTranslation(Transform.Translation);
            renderShader.SetUniform3x4("transform", transform);

            renderShader.SetBoneAnimationData(false);
            renderShader.SetUniform1("vTint", Color32.FromVector4Clamped(Tint).PackedValue);
            renderShader.SetUniform1("shaderId", material.Shader.NameHash);
            renderShader.SetUniform1("shaderProgramId", (uint)material.Shader.Program);

            material.Render(renderShader);

            GL.DrawArraysInstancedBaseInstance(PrimitiveType.TriangleStrip, 0, 4, 1, Id);

            material.PostRender();
        }
    }
}
