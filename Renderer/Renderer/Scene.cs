using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using ValveResourceFormat.Renderer.RHI;
using ValveResourceFormat.Renderer.SceneNodes;
using ValveResourceFormat.Renderer.World;
using Vortice.Vulkan;

namespace ValveResourceFormat.Renderer;

public class Scene
{
    /// <summary>Which pass of the frame an update belongs to.</summary>
    public enum UpdatePhase
    {
        /// <summary>One node at a time, in scene order.</summary>
        Place,

        /// <summary>Across the thread pool. Touch only what the node owns.</summary>
        Simulate,

        /// <summary>Finish work done on simulate. Back on the calling thread.</summary>
        Act,
    }

    /// <summary>
    /// Context data passed to scene nodes during per-frame update.
    /// </summary>
    public readonly struct UpdateContext
    {
        /// <summary>Gets which pass of the frame this update is.</summary>
        public UpdatePhase Phase { get; init; }

        /// <summary>Gets the camera used for view-dependent node updates.</summary>
        public required Camera Camera { get; init; }

        /// <summary>Gets the text renderer available for nodes that need to draw labels.</summary>
        //VKTODO: public required TextRenderer TextRenderer { get; init; }

        /// <summary>Gets the elapsed time in seconds since the last update.</summary>
        public required float Timestep { get; init; }

        /// <summary> Gets the renderer's total elapsed time in seconds.</summary>
        public float Uptime { get; init; }
    }

    //VKTEMP:
    public RendererContext RendererContext;

    /// <summary>Gets the octree used to spatially partition static scene nodes.</summary>
    public Octree StaticOctree { get; }

    /// <summary>Gets the flat spatial set holding dynamic scene nodes.</summary>
    public SpatialNodeSet DynamicOctree { get; } = new();

    /// <summary>Gets all static and dynamic scene nodes in the order they were added.</summary>
    public IEnumerable<SceneNode> AllNodes => staticNodes.Concat(dynamicNodes);
    private readonly List<SceneNode> staticNodes = [];
    private readonly List<SceneNode> dynamicNodes = [];

    /// <summary>Gets the render attribute overrides applied to all draw calls in this scene.</summary>
    public Dictionary<string, byte> RenderAttributes { get; } = [];

    /// <summary>Gets the world lighting information including light probes, environment maps, and dynamic lights.</summary>
    /// <remarks>Call <see cref="MarkLightingDirty"/> after changing its <see cref="WorldLightingInfo.LightingData"/>.</remarks>
    public WorldLightingInfo LightingInfo { get; }

    private RHI.Buffer? lightingBuffer;
    private RHI.Buffer? sceneBuffers;
    private readonly byte[] lightingUpload = new byte[Marshal.SizeOf<LightingConstants>()];
    private bool staticBuffersDirty = true;

    /// <summary>The byte offset of <c>sceneBuffers</c> in the shaders' <c>PushData</c>, after the parameter buffer handle.</summary>
    internal const uint SceneBuffersPushConstantOffset = 16;

    /// <summary>Gets the handle to the scene's <see cref="SceneBuffers"/>, pushed once before drawing the scene.</summary>
    internal DescriptorHandle<RHI.Buffer> SceneBuffersHandle => sceneBuffers!.DescriptorHandle;

    public Scene(RendererContext context, float sizeHint = 32768)
    {
        RendererContext = context;
        StaticOctree = new(sizeHint);

        LightingInfo = new();

        //VKTODO:
        //LightBinner = new(this);
        //EntitySystem = new(this);
    }

    /// <summary>
    /// Performs one-time GPU setup: builds acceleration structures, allocates buffers, computes light probe and environment map bindings, and loads internal shaders.
    /// </summary>
    public void Initialize()
    {
        //VKTODO:
        //UpdateOctrees();
        //UpdateNodeIndices();
        CreateBuffers();
        //VKTODO:
        //CalculateLightProbeBindings();
        //CalculateEnvironmentMaps();
        //CreateInstanceTransformBuffers(deletePrevious: true); // after calculating envmap and lpv

        //UpdateBuffers();

        //OutlineShader = RendererContext.ShaderLoader.LoadShader("outline");
        //FrustumCullShader = RendererContext.ShaderLoader.LoadShader("frustum_cull");
        //CompactionShader = RendererContext.ShaderLoader.LoadShader("compact_indirect_draws");
        //DepthPyramidShader = RendererContext.ShaderLoader.LoadShader("depth_pyramid");
        //DepthPyramidNpotShader = RendererContext.ShaderLoader.LoadShader("depth_pyramid", ("D_NPOT_DOWNSAMPLE", 1));
        //LightBinner.LoadShaders();

        //// set render lists to their max capacity
        //CollectSceneDrawCalls(new Camera(), Frustum.CreateEmpty());
        //SetupSceneShadows(new Camera(), -1);
    }

    /// <summary>Allocates the GPU buffers for the scene's lighting constants and the handles shaders use to reach them.</summary>
    public void CreateBuffers()
    {
        const VkBufferUsageFlags usage = VkBufferUsageFlags.UniformBuffer | VkBufferUsageFlags.TransferDst;
        lightingBuffer ??= new RHI.Buffer((ulong)lightingUpload.Length, usage, VmaMemoryUsage.AutoPreferDevice, name: nameof(LightingConstants));
        sceneBuffers ??= new RHI.Buffer((ulong)Unsafe.SizeOf<SceneBuffers>(), usage, VmaMemoryUsage.AutoPreferDevice, name: nameof(SceneBuffers));
    }

    public void Add(SceneNode node, bool dynamic)
    {
        if (dynamic)
        {
            dynamicNodes.Add(node);
            DynamicOctree.Dirty = true;
        }
        else
        {
            staticNodes.Add(node);
            StaticOctree.Dirty = true;
        }
    }

    /// <summary>Queues <see cref="WorldLightingInfo.LightingData"/> for upload before the next frame draws.</summary>
    public void MarkLightingDirty() => staticBuffersDirty = true;

    /// <summary>
    /// Uploads the scene's buffers if they changed. Must be recorded outside a render pass instance, before
    /// anything draws the scene.
    /// </summary>
    internal void UploadStaticBuffers(CommandList cmd)
    {
        Debug.Assert(lightingBuffer != null && sceneBuffers != null, "Scene.Initialize() must be called before rendering.");

        if (!staticBuffersDirty)
        {
            return;
        }

        staticBuffersDirty = false;

        unsafe
        {
            fixed (byte* pLighting = lightingUpload)
            {
                Marshal.StructureToPtr(LightingInfo.LightingData, (nint)pLighting, false);
            }
        }

        const VkPipelineStageFlags2 ShaderStages = VkPipelineStageFlags2.VertexShader | VkPipelineStageFlags2.FragmentShader | VkPipelineStageFlags2.ComputeShader;

        // Earlier frames may still be reading the previous contents
        cmd.BufferBarrier(ShaderStages, VkAccessFlags2.None, VkPipelineStageFlags2.Transfer, VkAccessFlags2.TransferWrite, lightingBuffer, sceneBuffers);

        cmd.UpdateBuffer(lightingBuffer, lightingUpload);
        cmd.UpdateBuffer(sceneBuffers, new SceneBuffers { Lighting = lightingBuffer.DescriptorHandle });

        cmd.BufferBarrier(VkPipelineStageFlags2.Transfer, VkAccessFlags2.TransferWrite, ShaderStages, VkAccessFlags2.UniformRead, lightingBuffer, sceneBuffers);
    }

    /// <summary>
    /// Renders the opaque pass, optionally with a depth prepass, followed by aggregate indirect draws and static overlay geometry.
    /// </summary>
    /// <param name="renderContext">The render context for this pass.</param>
    /// <param name="depthOnlyShader">Optional depth-only shader; <see langword="null"/> for a pass that replaces material shaders.</param>
    //VKTODO: The inputs need considering.
    public void RenderOpaqueLayer(ViewContext viewContext, CommandList cmd)
    {
        var camera = viewContext.Camera;

        cmd.PushConstants(SceneBuffersHandle, SceneBuffersPushConstantOffset);

        //using (new GLDebugGroup("Opaque Render"))
        //{
        //MeshBatchRenderer.Render(renderLists[renderContext.RenderPass], renderContext);
        //}

        foreach (var node in dynamicNodes)
        {
            if (node is ModelSceneNode modelNode)
            {
                foreach (var renderableMesh in modelNode.RenderableMeshes)
                {
                    foreach (var drawCall in renderableMesh.DrawCallsOpaque)
                    {
                        // Read once, so the bound pipeline and the layout the parameters are filled for cannot differ
                        var pipeline = drawCall.Material.Pipeline.Current;

                        cmd.BindGraphicsPipeline(pipeline);
                        cmd.PushConstants(drawCall.Material.PrepareParameterBuffer(pipeline), RenderMaterial.ParameterBufferPushConstantOffset);

                        cmd.BindVertexBuffer(drawCall.VertexBuffers[0]);

                        cmd.BindIndexBuffer(drawCall.IndexBuffer, VkIndexType.Uint16);

                        cmd.DrawIndexed((uint)drawCall.IndexCount, 1, (uint)drawCall.StartIndex / 2, drawCall.VertexOffset, 0);
                    }
                }
            }
        }
    }
}
