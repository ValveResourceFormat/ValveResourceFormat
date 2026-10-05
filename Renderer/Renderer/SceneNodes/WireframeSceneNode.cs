using System.IO.Hashing;
using System.Runtime.InteropServices;

namespace ValveResourceFormat.Renderer.SceneNodes
{
    /// <summary>
    /// Base for scene nodes that draw a static colored line list, such as visibility clusters, navigation volumes
    /// or entity connection lines.
    /// </summary>
    public abstract class WireframeSceneNode : SceneNode
    {
        private readonly LineBuffer lineBuffer;

        /// <summary>Gets the number of line vertices uploaded to the GPU.</summary>
        protected int VertexCount => lineBuffer.VertexCount;

        /// <summary>
        /// Gets whether the lines draw in the translucent pass without depth writes, or in the opaque pass with the
        /// pass depth state.
        /// </summary>
        protected virtual bool IsTranslucent => true;

        /// <summary>
        /// Initializes a new <see cref="WireframeSceneNode"/> drawing the given line list, two vertices per line.
        /// </summary>
        protected WireframeSceneNode(Scene scene, List<SimpleVertex> vertices, string debugName) : base(scene)
        {
            lineBuffer = new LineBuffer(Scene.RendererContext, debugName);
            lineBuffer.Upload(vertices);
        }

        /// <inheritdoc/>
        public override void Delete()
        {
            lineBuffer.Delete();
        }

        /// <inheritdoc/>
        public override void Render(Scene.RenderContext context)
        {
            var pass = IsTranslucent ? RenderPass.Translucent : RenderPass.Opaque;

            if (VertexCount == 0 || (context.RenderPass != pass && context.RenderPass != RenderPass.Outline))
            {
                return;
            }

            var renderShader = context.ReplacementShader ?? lineBuffer.Shader;
            renderShader.Use();

            using var _ = GraphicsContext.RenderState.Scope(depthWrite: IsTranslucent ? false : null);

            lineBuffer.Bind();

            DrawLines(context);
        }

        /// <summary>
        /// Issues the line draws after the buffer is bound. Draws every line by default.
        /// </summary>
        protected virtual void DrawLines(Scene.RenderContext context) => DrawLines(0, VertexCount);

        /// <summary>
        /// Draws a range of the uploaded line vertices.
        /// </summary>
        protected void DrawLines(int start, int count) => LineBuffer.DrawRange(start, count, Id);

        /// <summary>
        /// Gets a stable, mid-brightness color for an id, so neighbouring clusters are told apart.
        /// </summary>
        public static Color32 GetIdColor(uint id)
        {
            var h = XxHash32.HashToUInt32(MemoryMarshal.AsBytes(new ReadOnlySpan<uint>(in id)));

            var r = (byte)(((h & 0x3FF) / 1023.0f * 0.6f + 0.2f) * 255);
            var g = (byte)((((h >> 10) & 0x3FF) / 1023.0f * 0.6f + 0.2f) * 255);
            var b = (byte)((((h >> 20) & 0x3FF) / 1023.0f * 0.6f + 0.2f) * 255);

            return new Color32(r, g, b, 255);
        }
    }
}
