using GUI.Utils;
using OpenTK.Graphics.OpenGL;
using PrimitiveType = OpenTK.Graphics.OpenGL.PrimitiveType;

namespace GUI.Types.Renderer
{
    class LineSceneNode : SceneNode
    {
        readonly Shader shader;
        readonly int vaoHandle;

        public LineSceneNode(Scene scene, Color32 color, Vector3 start, Vector3 end)
            : base(scene)
        {
            LocalBoundingBox = new AABB(start, end);

            SimpleVertex[] vertices = [new(start, color), new(end, color)];

            shader = Scene.GuiContext.ShaderLoader.LoadShader("vrf.default");
            GL.UseProgram(shader.Program);

            vaoHandle = GL.GenVertexArray();
            GL.BindVertexArray(vaoHandle);

            var vboHandle = GL.GenBuffer();
            GL.BindBuffer(BufferTarget.ArrayBuffer, vboHandle);
            GL.BufferData(BufferTarget.ArrayBuffer, 2 * SimpleVertex.SizeInBytes, vertices, BufferUsageHint.StaticDraw);

            SimpleVertex.BindDefaultShaderLayout(shader.Program);

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
