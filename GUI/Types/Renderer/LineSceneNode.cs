using System.Numerics;
using OpenTK.Graphics.OpenGL;
using PrimitiveType = OpenTK.Graphics.OpenGL.PrimitiveType;

namespace GUI.Types.Renderer
{
    class LineSceneNode : SceneNode
    {
        readonly Shader shader;
        readonly int vaoHandle;

        public LineSceneNode(Scene scene, Vector3 color, Vector3 start, Vector3 end)
            : base(scene)
        {
            LocalBoundingBox = new AABB(start, end);

            var r = color.X / 255f;
            var g = color.Y / 255f;
            var b = color.Z / 255f;
            var a = 1.0f;

            var vertices = new float[]
            {
                start.X, start.Y, start.Z, r, g, b, a,
                end.X, end.Y, end.Z, r, g, b, a,
            };

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
            var renderShader = context.ReplacementShader ?? shader;

            GL.UseProgram(renderShader.Program);

            renderShader.SetUniform4x4("transform", Transform);
            renderShader.SetUniform1("bAnimated", 0.0f);
            renderShader.SetUniform1("sceneObjectId", Id);

            GL.BindVertexArray(vaoHandle);
            GL.DrawArrays(PrimitiveType.Lines, 0, 2);

            GL.UseProgram(0);
            GL.BindVertexArray(0);
        }

        public override void Update(Scene.UpdateContext context)
        {
            //
        }
    }
}
