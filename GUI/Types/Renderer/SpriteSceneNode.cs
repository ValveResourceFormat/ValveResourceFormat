using System.Linq;
using GUI.Utils;
using OpenTK.Graphics.OpenGL;
using ValveResourceFormat;
using PrimitiveType = OpenTK.Graphics.OpenGL.PrimitiveType;

namespace GUI.Types.Renderer
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

        private readonly int vaoHandle;
        private readonly RenderMaterial material;

        public SpriteSceneNode(Scene scene, VrfGuiContext vrfGuiContext, Resource resource, Vector3 position)
            : base(scene)
        {
            material = vrfGuiContext.MaterialLoader.LoadMaterial(resource);

            // Forcefully clamp sprites so they don't render extra pixels on edges
            foreach (var texture in material.Textures.Values)
            {
                texture.SetWrapMode(TextureWrapMode.ClampToEdge);
            }

            var attributes = new List<(string Name, int Size)>
            {
                ("vPOSITION", 3),
                ("vNORMAL", 4),
                ("vTEXCOORD", 2),
                ("vTANGENT", 4),
                ("vBLENDINDICES", 4),
                ("vBLENDWEIGHT", 4),
            };
            var stride = sizeof(float) * attributes.Sum(x => x.Size);
            var offset = 0;

            GL.CreateVertexArrays(1, out vaoHandle);
            GL.CreateBuffers(1, out int vboHandle);
            GL.VertexArrayVertexBuffer(vaoHandle, 0, vboHandle, 0, stride);
            GL.NamedBufferData(vboHandle, Vertices.Length * sizeof(float), Vertices, BufferUsageHint.StaticDraw);

            foreach (var (name, size) in attributes)
            {
                var attributeLocation = GL.GetAttribLocation(material.Shader.Program, name);
                if (attributeLocation > -1)
                {
                    GL.EnableVertexArrayAttrib(vaoHandle, attributeLocation);
                    GL.VertexArrayAttribFormat(vaoHandle, attributeLocation, size, VertexAttribType.Float, false, offset);
                    GL.VertexArrayAttribBinding(vaoHandle, attributeLocation, 0);
                }
                offset += sizeof(float) * size;
            }

#if DEBUG
            var vaoLabel = $"{nameof(SpriteSceneNode)}: {System.IO.Path.GetFileName(resource.FileName)}";
            GL.ObjectLabel(ObjectLabelIdentifier.VertexArray, vaoHandle, vaoLabel.Length, vaoLabel);
#endif

            var spriteSize = material.Material.FloatParams.GetValueOrDefault("g_flUniformPointSize", 16);
            spriteSize /= 2f; // correct the scale to actually be 16x16

            LocalBoundingBox = new AABB(-Vector3.One, Vector3.One);
            Transform = Matrix4x4.CreateScale(spriteSize) * Matrix4x4.CreateTranslation(position.X, position.Y, position.Z);
        }

        public override void Render(Scene.RenderContext context)
        {
            if (context.RenderPass != RenderPass.Translucent)
            {
                return;
            }

            var renderShader = context.ReplacementShader ?? material.Shader;

            GL.UseProgram(renderShader.Program);
            GL.BindVertexArray(vaoHandle);

            // Create billboarding rotation (always facing camera)
            Matrix4x4.Decompose(context.Camera.CameraViewMatrix, out _, out var modelViewRotation, out _);
            modelViewRotation = Quaternion.Inverse(modelViewRotation);
            var billboardMatrix = Matrix4x4.CreateFromQuaternion(modelViewRotation);

            var transform = billboardMatrix * Transform;
            renderShader.SetUniform4x4("transform", transform);

            renderShader.SetAnimationData(false);
            renderShader.SetUniform1("sceneObjectId", Id);
            renderShader.SetUniform1("shaderId", (uint)material.Shader.NameHash);
            renderShader.SetUniform1("shaderProgramId", (uint)material.Shader.Program);

            material.Render(renderShader);

            GL.DrawArrays(PrimitiveType.TriangleStrip, 0, 4);

            material.PostRender();

            GL.BindVertexArray(0);
            GL.UseProgram(0);
        }

        public override void Update(Scene.UpdateContext context)
        {
            //
        }
    }
}
