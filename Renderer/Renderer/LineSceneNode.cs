using OpenTK.Graphics.OpenGL;
using PrimitiveType = OpenTK.Graphics.OpenGL.PrimitiveType;

namespace ValveResourceFormat.Renderer
{
    /// <summary>
    /// Scene node that renders a single line segment between two points.
    /// </summary>
    public class LineSceneNode : SceneNode
    {
        readonly Shader shader;
        readonly int vaoHandle;

        /// <summary>
        /// Initializes a new instance of the <see cref="LineSceneNode"/> class.
        /// </summary>
        /// <param name="scene">The scene this node belongs to.</param>
        /// <param name="start">The start position of the line.</param>
        /// <param name="end">The end position of the line.</param>
        /// <param name="startColor">The color at the start of the line.</param>
        /// <param name="endColor">The color at the end of the line.</param>
        public LineSceneNode(Scene scene, Vector3 start, Vector3 end, Color32 startColor, Color32 endColor)
            : base(scene)
        {
            LocalBoundingBox = new AABB(start, end);

            SimpleVertex[] vertices = [new(start, startColor), new(end, endColor)];

            shader = Scene.RendererContext.ShaderLoader.LoadShader("vrf.default");

            GL.CreateVertexArrays(1, out vaoHandle);
            GL.CreateBuffers(1, out int vboHandle);
            GL.VertexArrayVertexBuffer(vaoHandle, 0, vboHandle, 0, SimpleVertex.SizeInBytes);
            SimpleVertex.BindDefaultShaderLayout(vaoHandle, shader.Program);

            GL.NamedBufferData(vboHandle, 2 * SimpleVertex.SizeInBytes, vertices, BufferUsageHint.StaticDraw);

#if DEBUG
            var vaoLabel = nameof(LineSceneNode);
            GL.ObjectLabel(ObjectLabelIdentifier.VertexArray, vaoHandle, vaoLabel.Length, vaoLabel);
            GL.ObjectLabel(ObjectLabelIdentifier.Buffer, vboHandle, vaoLabel.Length, vaoLabel);
#endif
        }

        public override void Render(Scene.RenderContext context)
        {
            if (context.RenderPass is not RenderPass.Opaque and not RenderPass.Outline)
            {
                return;
            }

            var renderShader = context.ReplacementShader ?? shader;
            renderShader.Use();
            renderShader.SetUniform3x4("transform", Transform);
            renderShader.SetBoneAnimationData(false);

            GL.BindVertexArray(vaoHandle);
            GL.DrawArraysInstancedBaseInstance(PrimitiveType.Lines, 0, 2, 1, Id);

            GL.UseProgram(0);
            GL.BindVertexArray(0);
        }
    }
}
