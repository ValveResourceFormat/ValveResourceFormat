using System.Diagnostics;
using System.Runtime.InteropServices;
using OpenTK.Graphics.OpenGL;
using ValveResourceFormat.CompiledShader;
using ValveResourceFormat.Renderer.Buffers;

namespace ValveResourceFormat.Renderer
{
    /// <summary>
    /// Debug visualization renderer for occluded object bounds in occlusion culling.
    /// Renders bounding boxes directly from GPU buffer using procedural vertex generation.
    /// </summary>
    public class OcclusionDebugRenderer
    {
        // Header layout shared with frustum_cull.comp.slang / occlusion_debug.vert.slang:
        //   uint occludedCount; uint padding[3];                                  (16 bytes)
        //   uint indirectVertexCount/InstanceCount/First/BaseInstance;            (16 bytes)
        // followed by the OccludedBoundDebug[] array.
        private const int HeaderSizeBytes = 32;
        private const int IndirectArgsByteOffset = 16;

        private readonly Shader shader;
        private readonly Shader finalizeShader;
        private readonly Scene scene;
        private readonly RendererContext renderContext;

        internal StorageBuffer? OccludedBoundsDebugGpu;

        /// <summary>Initializes the occlusion debug renderer and loads the debug shaders.</summary>
        /// <param name="scene">Scene to visualize occlusion for.</param>
        /// <param name="rendererContext">Renderer context for loading shaders and GPU resources.</param>
        public OcclusionDebugRenderer(Scene scene, RendererContext rendererContext)
        {
            this.scene = scene;
            renderContext = rendererContext;

            shader = rendererContext.ShaderLoader.LoadShader("occlusion_debug");
            finalizeShader = rendererContext.ShaderLoader.LoadShader("occlusion_debug_finalize");
        }

        /// <summary>Allocates (if needed) and clears the GPU buffer that receives occluded bounds from the culling shader.</summary>
        public void BindAndClearBuffer()
        {
            if (OccludedBoundsDebugGpu == null)
            {
                var totalSize = HeaderSizeBytes + (scene.SceneMeshletCount * Marshal.SizeOf<OccludedBoundDebug>());
                OccludedBoundsDebugGpu = StorageBuffer.Allocate<byte>(ReservedBufferSlots.OccludedBoundsDebug, nameof(ReservedBufferSlots.OccludedBoundsDebug), totalSize, BufferUsageHint.DynamicCopy);
            }

            // Clear the atomic counter before dispatching
            var zero = 0u;
            GL.ClearNamedBufferSubData(OccludedBoundsDebugGpu.Handle, PixelInternalFormat.R32ui, IntPtr.Zero, sizeof(uint), PixelFormat.RedInteger, PixelType.UnsignedInt, ref zero);
            OccludedBoundsDebugGpu.BindBufferBase();
        }

        /// <summary>
        /// Dispatches a single-invocation compute shader that turns the occluded-object
        /// atomic counter into a <c>DrawArraysIndirectCommand</c>, entirely on the GPU.
        /// </summary>
        public void DispatchFinalize()
        {
            Debug.Assert(OccludedBoundsDebugGpu is not null);

            finalizeShader.Use();
            OccludedBoundsDebugGpu.BindBufferBase();
            GL.DispatchCompute(1, 1, 1);
        }

        /// <summary>Renders wireframe bounding boxes for all occluded meshlets, color-coded by whether occlusion was correct.</summary>
        public void Render()
        {
            if (!scene.DrawMeshletsIndirect || !scene.EnableOcclusionCulling || OccludedBoundsDebugGpu == null)
            {
                return;
            }

            var renderState = renderContext.RenderState;
            var state = renderState.CurrentPass;
            state.BlendEnable = true;
            state.SetBlend(RsBlendMode.SrcAlpha, RsBlendMode.InvSrcAlpha);
            state.DepthStencil.DepthTestEnable = true;
            state.DepthStencil.DepthWriteEnable = false;
            using var _ = renderState.Scope(in state);

            shader.Use();

            OccludedBoundsDebugGpu.BindBufferBase();

            GL.BindVertexArray(renderContext.MeshBufferCache.EmptyVAO);
            GL.BindBuffer(BufferTarget.DrawIndirectBuffer, OccludedBoundsDebugGpu.Handle);

            var indirectArgs = (IntPtr)IndirectArgsByteOffset;

            // First pass: behind depth buffer (correctly occluded) - GREEN
            state.DepthStencil.DepthFunc = RsComparison.Farther;
            renderState.Apply(in state);
            shader.SetUniform("g_vColor", new Vector4(0.0f, 1.0f, 0.0f, 0.9f));
            GL.DrawArraysIndirect(PrimitiveType.Lines, indirectArgs);

            // Second pass: in front/at depth buffer (incorrectly visible) - RED
            state.DepthStencil.DepthFunc = RsComparison.CloserEqual;
            renderState.Apply(in state);
            shader.SetUniform("g_vColor", new Vector4(1.0f, 0.0f, 0.0f, 0.9f));
            GL.DrawArraysIndirect(PrimitiveType.Lines, indirectArgs);

            GL.BindBuffer(BufferTarget.DrawIndirectBuffer, 0);
        }
    }
}
