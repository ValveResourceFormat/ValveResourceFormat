using OpenTK.Graphics.OpenGL;
using PrimitiveType = OpenTK.Graphics.OpenGL.PrimitiveType;

namespace GUI.Types.Renderer
{
    class SimpleBoxSceneNode : SceneNode
    {
        readonly Shader shader;
        readonly int vertexCount;
        readonly int vaoHandle;

        public SimpleBoxSceneNode(Scene scene, Vector3 color, Vector3 size)
            : base(scene)
        {
            var x = size.X / 2f;
            var y = size.Y / 2f;
            var z = size.Z / 2f;

            LocalBoundingBox = new AABB(new Vector3(-x, -y, -z), new Vector3(x, y, z));

            var r = color.X / 255f;
            var g = color.Y / 255f;
            var b = color.Z / 255f;
            var a = 1.0f;

            var vertices = new float[]
            {
                -x,-y,-z, r, g, b, a,
                -x,-y, z, r, g, b, a,
                -x, y, z, r, g, b, a,
                 x, y,-z, r, g, b, a,
                -x,-y,-z, r, g, b, a,
                -x, y,-z, r, g, b, a,
                 x,-y, z, r, g, b, a,
                -x,-y,-z, r, g, b, a,
                 x,-y,-z, r, g, b, a,
                 x, y,-z, r, g, b, a,
                 x,-y,-z, r, g, b, a,
                -x,-y,-z, r, g, b, a,
                -x,-y,-z, r, g, b, a,
                -x, y, z, r, g, b, a,
                -x, y,-z, r, g, b, a,
                 x,-y, z, r, g, b, a,
                -x,-y, z, r, g, b, a,
                -x,-y,-z, r, g, b, a,
                -x, y, z, r, g, b, a,
                -x,-y, z, r, g, b, a,
                 x,-y, z, r, g, b, a,
                 x, y, z, r, g, b, a,
                 x,-y,-z, r, g, b, a,
                 x, y,-z, r, g, b, a,
                 x,-y,-z, r, g, b, a,
                 x, y, z, r, g, b, a,
                 x,-y, z, r, g, b, a,
                 x, y, z, r, g, b, a,
                 x, y,-z, r, g, b, a,
                -x, y,-z, r, g, b, a,
                 x, y, z, r, g, b, a,
                -x, y,-z, r, g, b, a,
                -x, y, z, r, g, b, a,
                 x, y, z, r, g, b, a,
                -x, y, z, r, g, b, a,
                 x,-y, z, r, g, b, a,
            };

            vertexCount = vertices.Length / 7;

            shader = Scene.GuiContext.ShaderLoader.LoadShader("vrf.default");
            GL.UseProgram(shader.Program);

            vaoHandle = GL.GenVertexArray();
            GL.BindVertexArray(vaoHandle);

            var vboHandle = GL.GenBuffer();
            GL.BindBuffer(BufferTarget.ArrayBuffer, vboHandle);
            GL.BufferData(BufferTarget.ArrayBuffer, vertices.Length * sizeof(float), vertices, BufferUsageHint.StaticDraw);

            const int stride = sizeof(float) * 7;
            var positionAttributeLocation = GL.GetAttribLocation(shader.Program, "aVertexPosition");
            GL.EnableVertexAttribArray(positionAttributeLocation);
            GL.VertexAttribPointer(positionAttributeLocation, 3, VertexAttribPointerType.Float, false, stride, 0);

            var colorAttributeLocation = GL.GetAttribLocation(shader.Program, "aVertexColor");
            GL.EnableVertexAttribArray(colorAttributeLocation);
            GL.VertexAttribPointer(colorAttributeLocation, 4, VertexAttribPointerType.Float, false, stride, sizeof(float) * 3);

            GL.BindVertexArray(0);
        }

        public override void Render(Scene.RenderContext context)
        {
            if (context.RenderPass != RenderPass.AfterOpaque)
            {
                return;
            }

            var renderShader = context.ReplacementShader ?? shader;

            GL.UseProgram(renderShader.Program);

            renderShader.SetUniform4x4("transform", Transform);
            renderShader.SetUniform1("bAnimated", 0.0f);
            renderShader.SetUniform1("sceneObjectId", Id);

            GL.BindVertexArray(vaoHandle);
            GL.DrawArrays(PrimitiveType.Triangles, 0, vertexCount);

            GL.UseProgram(0);
            GL.BindVertexArray(0);
        }

        public override void Update(Scene.UpdateContext context)
        {
            //
        }
    }
}
