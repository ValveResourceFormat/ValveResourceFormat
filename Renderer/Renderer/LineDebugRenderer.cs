using OpenTK.Graphics.OpenGL;

namespace ValveResourceFormat.Renderer
{
    /// <summary>
    /// Base class for debug overlays that draw a line batch blended and without depth writes.
    /// </summary>
    public abstract class LineDebugRenderer
    {
        private readonly LineBuffer lineBuffer;

        /// <summary>Creates the GPU line buffer.</summary>
        protected LineDebugRenderer(RendererContext rendererContext, string label)
        {
            lineBuffer = new LineBuffer(rendererContext, label);
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

            GL.Enable(EnableCap.Blend);

            if (disableDepthTest)
            {
                GL.Disable(EnableCap.DepthTest);
            }

            GL.DepthMask(false);
            GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);

            lineBuffer.Shader.Use();
            lineBuffer.Shader.SetUniform3x4("transform", Matrix4x4.Identity);

            lineBuffer.Draw();
            GL.UseProgram(0);

            GL.DepthMask(true);
            GL.Disable(EnableCap.Blend);

            if (disableDepthTest)
            {
                GL.Enable(EnableCap.DepthTest);
            }
        }

        /// <summary>Deletes the GL objects.</summary>
        public void Delete() => lineBuffer.Delete();
    }
}
