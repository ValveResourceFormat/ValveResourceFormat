using OpenTK.Graphics.OpenGL;

namespace ValveResourceFormat.Renderer
{
    /// <summary>
    /// Debug visualization renderer for occluded object bounds in occlusion culling.
    /// Renders bounding boxes directly from GPU buffer using procedural vertex generation.
    /// </summary>
    public class OcclusionDebugRenderer
    {
        private readonly Shader shader;
        private readonly Scene scene;
        private readonly RendererContext renderContext;

        public OcclusionDebugRenderer(Scene scene, RendererContext rendererContext)
        {
            this.scene = scene;
            renderContext = rendererContext;

            shader = rendererContext.ShaderLoader.LoadShader("vrf.occlusion_debug");
        }

        public void Render()
        {
            if (scene.OccludedBoundsDebugGpu == null || !scene.EnableIndirectDraws || !scene.EnableOcclusionCulling)
            {
                return;
            }

            // Read only the count from GPU
            var countArray = new uint[1];
            GL.BindBuffer(BufferTarget.ShaderStorageBuffer, scene.OccludedBoundsDebugGpu.Handle);
            GL.GetBufferSubData(BufferTarget.ShaderStorageBuffer, IntPtr.Zero, sizeof(uint), countArray);
            GL.BindBuffer(BufferTarget.ShaderStorageBuffer, 0);

            var occludedCount = (int)countArray[0];
            if (occludedCount == 0)
            {
                return;
            }

            GL.Enable(EnableCap.Blend);
            GL.Enable(EnableCap.DepthTest);
            GL.DepthMask(false);
            GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);

            shader.Use();

            // Bind the occluded bounds buffer to shader
            GL.BindBufferBase(BufferRangeTarget.ShaderStorageBuffer, 0, scene.OccludedBoundsDebugGpu.Handle);

            GL.BindVertexArray(renderContext.MeshBufferCache.EmptyVAO);

            // Each AABB = 24 vertices (12 lines * 2 vertices per line)
            var totalVertices = occludedCount * 24;

            // First pass: behind depth buffer (correctly occluded) - GREEN
            GL.DepthFunc(DepthFunction.Less);
            shader.SetUniform4("g_vColor", new Vector4(0.0f, 1.0f, 0.0f, 0.9f));
            GL.DrawArrays(PrimitiveType.Lines, 0, totalVertices);

            // Second pass: in front/at depth buffer (incorrectly visible) - RED
            GL.DepthFunc(DepthFunction.Gequal);
            shader.SetUniform4("g_vColor", new Vector4(1.0f, 0.0f, 0.0f, 0.9f));
            GL.DrawArrays(PrimitiveType.Lines, 0, totalVertices);

            // Restore defaults
            GL.DepthFunc(DepthFunction.Greater);
            GL.DepthMask(true);
            GL.Disable(EnableCap.Blend);
        }
    }
}
