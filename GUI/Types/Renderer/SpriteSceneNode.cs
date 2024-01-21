using System.Linq;
using GUI.Utils;
using OpenTK.Graphics.OpenGL;
using ValveResourceFormat;
using PrimitiveType = OpenTK.Graphics.OpenGL.PrimitiveType;

namespace GUI.Types.Renderer
{
    class SpriteSceneNode : SceneNode
    {
        private readonly int quadVao;

        private readonly RenderMaterial material;
        private readonly Vector3 position;
        private readonly float size;

        public SpriteSceneNode(Scene scene, VrfGuiContext vrfGuiContext, Resource resource, Vector3 position)
            : base(scene)
        {
            material = vrfGuiContext.MaterialLoader.LoadMaterial(resource);

            // Forcefully clamp sprites so they don't render extra pixels on edges
            foreach (var texture in material.Textures.Values)
            {
                using (texture.BindingContext())
                {
                    texture.SetWrapMode(TextureWrapMode.ClampToEdge);
                }
            }

            quadVao = SetupSquareQuadBuffer(material.Shader);
            size = material.Material.FloatParams.GetValueOrDefault("g_flUniformPointSize", 16);
            size /= 2f; // correct the scale to actually be 16x16

            this.position = position;
            var size3 = new Vector3(size);
            LocalBoundingBox = new AABB(position - size3, position + size3);
        }

        public static int SetupSquareQuadBuffer(Shader shader)
        {
            GL.UseProgram(shader.Program);

            // Create and bind VAO
            var vao = GL.GenVertexArray();
            GL.BindVertexArray(vao);

            var vbo = GL.GenBuffer();
            GL.BindBuffer(BufferTarget.ArrayBuffer, vbo);

            var vertices = new[]
            {
                // position          ; normal                  ; texcoord    ; tangent                 ; blendindices            ; blendweight
                -1.0f, -1.0f, 0.0f,  0.0f, 0.0f, 0.0f, 1.0f,   0.0f, 1.0f,   1.0f, 0.0f, 0.0f, 0.0f,   0.0f, 0.0f, 0.0f, 0.0f,   0.0f, 0.0f, 0.0f, 0.0f,
                -1.0f, 1.0f, 0.0f,   0.0f, 0.0f, 0.0f, 1.0f,   0.0f, 0.0f,   1.0f, 0.0f, 0.0f, 0.0f,   0.0f, 0.0f, 0.0f, 0.0f,   0.0f, 0.0f, 0.0f, 0.0f,
                1.0f, -1.0f, 0.0f,   0.0f, 0.0f, 0.0f, 1.0f,   1.0f, 1.0f,   1.0f, 0.0f, 0.0f, 0.0f,   0.0f, 0.0f, 0.0f, 0.0f,   0.0f, 0.0f, 0.0f, 0.0f,
                1.0f, 1.0f, 0.0f,    0.0f, 0.0f, 0.0f, 1.0f,   1.0f, 0.0f,   1.0f, 0.0f, 0.0f, 0.0f,   0.0f, 0.0f, 0.0f, 0.0f,   0.0f, 0.0f, 0.0f, 0.0f,
            };

            GL.BufferData(BufferTarget.ArrayBuffer, vertices.Length * sizeof(float), vertices, BufferUsageHint.StaticDraw);

            GL.EnableVertexAttribArray(0);

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

            foreach (var (Name, Size) in attributes)
            {
                var attributeLocation = GL.GetAttribLocation(shader.Program, Name);
                if (attributeLocation > -1)
                {
                    GL.EnableVertexAttribArray(attributeLocation);
                    GL.VertexAttribPointer(attributeLocation, Size, VertexAttribPointerType.Float, false, stride, offset);
                }
                offset += sizeof(float) * Size;
            }

            GL.BindVertexArray(0); // Unbind VAO

            return vao;
        }

        public override void Render(Scene.RenderContext context)
        {
            var renderShader = context.ReplacementShader ?? material.Shader;

            GL.UseProgram(renderShader.Program);
            GL.BindVertexArray(quadVao);

            // Create billboarding rotation (always facing camera)
            Matrix4x4.Decompose(context.Camera.CameraViewMatrix, out _, out var modelViewRotation, out _);
            modelViewRotation = Quaternion.Inverse(modelViewRotation);
            var billboardMatrix = Matrix4x4.CreateFromQuaternion(modelViewRotation);

            var scaleMatrix = Matrix4x4.CreateScale(size);
            var translationMatrix = Matrix4x4.CreateTranslation(position.X, position.Y, position.Z);

            var test = billboardMatrix * scaleMatrix * translationMatrix;
            var test2 = test.ToOpenTK();

            var transformTk = Transform.ToOpenTK();
            GL.UniformMatrix4(renderShader.GetUniformLocation("transform"), false, ref test2);

            renderShader.SetUniform1("bAnimated", 0.0f);
            renderShader.SetUniform1("sceneObjectId", Id);
            renderShader.SetUniform1("shaderId", (uint)material.Shader.NameHash);
            renderShader.SetUniform1("shaderProgramId", (uint)material.Shader.Program);

            material.Render(renderShader);

            GL.Disable(EnableCap.CullFace);

            GL.DrawArrays(PrimitiveType.TriangleStrip, 0, 4);

            material.PostRender();

            GL.Enable(EnableCap.CullFace);

            GL.BindVertexArray(0);
            GL.UseProgram(0);
        }

        public override void Update(Scene.UpdateContext context)
        {
            //
        }
    }
}
