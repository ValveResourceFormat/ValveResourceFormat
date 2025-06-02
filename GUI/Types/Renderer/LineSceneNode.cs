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

            GL.CreateVertexArrays(1, out vaoHandle);
            GL.CreateBuffers(1, out int vboHandle);
            GL.VertexArrayVertexBuffer(vaoHandle, 0, vboHandle, 0, SimpleVertex.SizeInBytes);
            SimpleVertex.BindDefaultShaderLayout(vaoHandle, shader.Program);

            GL.NamedBufferData(vboHandle, 2 * SimpleVertex.SizeInBytes, vertices, BufferUsageHint.StaticDraw);

#if DEBUG
            var vaoLabel = nameof(LineSceneNode);
            GL.ObjectLabel(ObjectLabelIdentifier.VertexArray, vaoHandle, vaoLabel.Length, vaoLabel);
#endif
        }

        public override void Render(Scene.RenderContext context)
        {
            if (context.RenderPass != RenderPass.AfterOpaque)
            {
                return;
            }

            var renderShader = context.ReplacementShader ?? shader;
            renderShader.Use();
            renderShader.SetUniform4x4("transform", Transform);
            renderShader.SetBoneAnimationData(false);
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
