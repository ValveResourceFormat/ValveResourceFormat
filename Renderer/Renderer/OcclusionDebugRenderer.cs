using System.Diagnostics;
using System.Runtime.InteropServices;
using OpenTK.Graphics.OpenGL;
using ValveResourceFormat.Renderer.Buffers;

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

        internal StorageBuffer? OccludedBoundsDebugGpu;

        public OcclusionDebugRenderer(Scene scene, RendererContext rendererContext)
        {
            this.scene = scene;
            renderContext = rendererContext;

            shader = rendererContext.ShaderLoader.LoadShader("vrf.occlusion_debug");
        }

        public void BindAndClearBuffer()
        {
            if (OccludedBoundsDebugGpu == null)
            {
                // Create debug buffer for occluded bounds (4 uints for header + bounds array)
                var headerSize = 4; // uint count + 3 padding uints
                var totalSize = (headerSize + scene.SceneMeshletCount) * Marshal.SizeOf<Vector4>();
                OccludedBoundsDebugGpu = new StorageBuffer(ReservedBufferSlots.OccludedBoundsDebug);
                GL.NamedBufferData(OccludedBoundsDebugGpu.Handle, totalSize, IntPtr.Zero, BufferUsageHint.StreamRead);
            }

            // Clear the atomic counter before dispatching
            var zero = 0u;
            GL.ClearNamedBufferSubData(OccludedBoundsDebugGpu.Handle, PixelInternalFormat.R32ui, IntPtr.Zero, sizeof(uint), PixelFormat.RedInteger, PixelType.UnsignedInt, ref zero);
            OccludedBoundsDebugGpu.BindBufferBase();
        }

        public void Render()
        {
            if (!scene.DrawMeshletsIndirect || !scene.EnableOcclusionCulling || OccludedBoundsDebugGpu == null)
            {
                return;
            }

            // Read only the count from GPU
            var countArray = new uint[1];
            GL.BindBuffer(BufferTarget.ShaderStorageBuffer, OccludedBoundsDebugGpu.Handle);
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
            OccludedBoundsDebugGpu.BindBufferBase();

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
