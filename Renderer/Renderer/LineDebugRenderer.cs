using OpenTK.Graphics.OpenGL;
using ValveResourceFormat.CompiledShader;

namespace ValveResourceFormat.Renderer
{
    /// <summary>
    /// Base class for debug overlays that draw a line batch blended and without depth writes.
    /// </summary>
    public abstract class LineDebugRenderer
    {
        private readonly LineBuffer lineBuffer;
        private readonly RenderStateTracker renderState;

        /// <summary>Creates the GPU line buffer.</summary>
        protected LineDebugRenderer(RendererContext rendererContext, string label)
        {
            lineBuffer = new LineBuffer(rendererContext, label);
            renderState = rendererContext.RenderState;
        }

        /// <summary>Drops the uploaded vertices.</summary>
        protected void Clear() => lineBuffer.Clear();

        /// <summary>Uploads the line vertices, two per segment.</summary>
        protected void Upload(List<SimpleVertex> vertices, BufferUsageHint usageHint = BufferUsageHint.DynamicDraw)
            => lineBuffer.Upload(vertices, usageHint);

        /// <summary>Draws the uploaded lines, on top of everything when depth test is disabled.</summary>
        protected void RenderLines(bool disableDepthTest = false)
        {
            if (lineBuffer.VertexCount == 0)
            {
                return;
            }

            using var _ = renderState.Scope(depthTest: disableDepthTest ? false : null, depthWrite: false, blend: true);

            lineBuffer.Shader.Use();
            lineBuffer.Shader.SetUniform3x4("transform", Matrix4x4.Identity);

            lineBuffer.Draw();
        }

        /// <summary>Deletes the GL objects.</summary>
        public void Delete() => lineBuffer.Delete();
    }
}
