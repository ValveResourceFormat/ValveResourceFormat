using OpenTK.Graphics.OpenGL;
using PrimitiveType = OpenTK.Graphics.OpenGL.PrimitiveType;

namespace ValveResourceFormat.Renderer.SceneNodes
{
    class SpriteSceneNode : SceneNode
    {
        private static readonly float[] Vertices =
        [
#pragma warning disable format
            // position          ; normal                  ; texcoord    ; tangent                 ; blendindices            ; blendweight
            1.0f, -1.0f, 0.0f,   0.0f, 0.0f, 0.0f, 1.0f,   1.0f, 1.0f,   1.0f, 0.0f, 0.0f, 0.0f,   0.0f, 0.0f, 0.0f, 0.0f,   0.0f, 0.0f, 0.0f, 0.0f,
            1.0f, 1.0f, 0.0f,    0.0f, 0.0f, 0.0f, 1.0f,   1.0f, 0.0f,   1.0f, 0.0f, 0.0f, 0.0f,   0.0f, 0.0f, 0.0f, 0.0f,   0.0f, 0.0f, 0.0f, 0.0f,
            -1.0f, -1.0f, 0.0f,  0.0f, 0.0f, 0.0f, 1.0f,   0.0f, 1.0f,   1.0f, 0.0f, 0.0f, 0.0f,   0.0f, 0.0f, 0.0f, 0.0f,   0.0f, 0.0f, 0.0f, 0.0f,
            -1.0f, 1.0f, 0.0f,   0.0f, 0.0f, 0.0f, 1.0f,   0.0f, 0.0f,   1.0f, 0.0f, 0.0f, 0.0f,   0.0f, 0.0f, 0.0f, 0.0f,   0.0f, 0.0f, 0.0f, 0.0f,
#pragma warning restore format
        ];

        private static readonly ValveResourceFormat.Blocks.VBIB.RenderInputLayoutField[] InputLayout =
        [
            new("POSITION", DXGI_FORMAT.R32G32B32_FLOAT, offset: 0),
            new("NORMAL", DXGI_FORMAT.R32G32B32A32_FLOAT, offset: 12),
            new("TEXCOORD", DXGI_FORMAT.R32G32_FLOAT, offset: 28),
            new("TANGENT", DXGI_FORMAT.R32G32B32A32_FLOAT, offset: 36),
            new("BLENDINDICES", DXGI_FORMAT.R32G32B32A32_FLOAT, offset: 52),
            new("BLENDWEIGHT", DXGI_FORMAT.R32G32B32A32_FLOAT, offset: 68),
        ];

        private readonly RenderVao vao;
        private readonly RenderMaterial material;

        public SpriteSceneNode(Scene scene, RendererContext renderContext, Resource resource, Vector3 position)
            : base(scene)
        {
            material = renderContext.MaterialLoader.LoadMaterial(resource);

            // Forcefully clamp sprites so they don't render extra pixels on edges
            foreach (var texture in material.Textures.Values)
            {
                texture.SetWrapMode(TextureWrapMode.ClampToEdge);
            }

            const int stride = sizeof(float) * 21;

            GL.CreateBuffers(1, out int vboHandle);
            GL.NamedBufferData(vboHandle, Vertices.Length * sizeof(float), Vertices, BufferUsageHint.StaticDraw);

            vao = new RenderVao(renderContext.MeshBufferCache, nameof(SpriteSceneNode), vboHandle, stride, InputLayout, inputSignature: material.Material.InputSignature);

#if DEBUG
            var vaoLabel = $"{nameof(SpriteSceneNode)}: {System.IO.Path.GetFileName(resource.FileName)}";
            GL.ObjectLabel(ObjectLabelIdentifier.Buffer, vboHandle, Math.Min(GLEnvironment.MaxLabelLength, vaoLabel.Length), vaoLabel);
#endif

            var spriteSize = material.Material.FloatParams.GetValueOrDefault("g_flUniformPointSize", 16);
            spriteSize /= 2f; // correct the scale to actually be 16x16

            LocalBoundingBox = new AABB(-Vector3.One, Vector3.One);
            Transform = Matrix4x4.CreateScale(spriteSize) * Matrix4x4.CreateTranslation(position.X, position.Y, position.Z);
        }

        public override void Render(Scene.RenderContext context)
        {
            if (context.RenderPass is not RenderPass.Opaque and not RenderPass.Outline)
            {
                return;
            }

            var renderShader = context.ReplacementShader ?? material.Shader;
            renderShader.Use();

            GL.BindVertexArray(vao.Get(renderShader));

            // Create billboarding rotation (always facing camera)
            if (!Matrix4x4.Decompose(context.Camera.CameraViewMatrix, out _, out var modelViewRotation, out _))
            {
                throw new InvalidOperationException("Matrix decompose failed");
            }

            modelViewRotation = Quaternion.Inverse(modelViewRotation);
            var billboardMatrix = Matrix4x4.CreateFromQuaternion(modelViewRotation);

            var transform = billboardMatrix * Transform;
            renderShader.SetUniform3x4("transform", transform);

            renderShader.SetBoneAnimationData(false);
            renderShader.SetUniform1("shaderId", material.Shader.NameHash);
            renderShader.SetUniform1("shaderProgramId", (uint)material.Shader.Program);

            material.Render(renderShader);

            GL.DrawArraysInstancedBaseInstance(PrimitiveType.TriangleStrip, 0, 4, 1, Id);

            material.PostRender();

            GL.BindVertexArray(0);
            GL.UseProgram(0);
        }
    }
}
