using System.Runtime.InteropServices;
using ValveResourceFormat.Renderer.RHI;
using ValveResourceFormat.ResourceTypes;
using Vortice.Vulkan;

namespace ValveResourceFormat.Renderer;

public class Renderer2
{

    /// <param name="Start">The starting depth value from the viewers perspective. Note: 1.0 = closest.</param>
    /// <param name="End">The ending depth value from the viewers perspective. Note: 0.0 = furthest.</param>
    public record DepthRange(float Start, float End)
    {
        /// <summary>The window-space near value.</summary>
        public float Near { get; } = End;

        /// <summary>The window-space far value.</summary>
        public float Far { get; } = Start;

        /// <summary>The whole window range, for render targets that are not part of the scene.</summary>
        public static readonly DepthRange Full = new(1f, 0f);

        /// <summary>The main scene.</summary>
        public static readonly DepthRange Scene = new(0.95f, 0.05f);

        /// <summary>Reserved for the first-person viewmodel, always in front of the main scene.</summary>
        public static readonly DepthRange Viewmodel = new(1.0f, Scene.Start);

        /// <summary>Reserved for the 3D sky, always behind the main scene.</summary>
        public static readonly DepthRange Sky = new(Scene.End, 0f);
    }

    /// <summary>
    /// The main scene to render.
    /// </summary>
    public Scene Scene { get; set; }

    private InFlightFrames InFlightFrames = null!;

    private RenderTarget HdrRenderTarget { get; set; }

    private Swapchain? WindowSwapchain { get; set; }

    public bool HasSwapchain => WindowSwapchain != null;

    /// <summary>
    /// Draws the infinite reference grid on the XY plane after the scene.
    /// </summary>
    public bool ShowGrid { get; set; }

    private ResourceHandle<PipelineGraphics>? GridPipeline;

    // VKTODO: Two once the 3D sky is ported
    private readonly SceneView[] frameViews = new SceneView[1];

    private readonly Dictionary<Scene, SceneViewState> mainViewStates = [];
    private readonly List<SceneViewState> mainViewSceneStates = [];

    private readonly ViewConstants viewConstants = new();
    private readonly byte[] viewConstantsUpload = new byte[Marshal.SizeOf<ViewConstants>()];

    /// <summary>
    /// Total time elapsed since the renderer was started, in seconds.
    /// </summary>
    public float Uptime { get; set; }

    /// <summary>
    /// Time elapsed since the last frame, in seconds.
    /// </summary>
    public float DeltaTime { get; set; }

    /// <summary>
    /// Shared renderer context containing loaders and caches.
    /// </summary>
    public RendererContext RendererContext { get; }

    /// <summary>
    /// Initializes a new renderer around a scene
    /// </summary>
    public Renderer2(RendererContext rendererContext)
    {
        RendererContext = rendererContext;
        //VKTODO:
        //PerfStats = new PerfStats();
        //Postprocess = new(rendererContext);
        //LightTilesOverlay = new(rendererContext);
        //Camera = new Camera(rendererContext.FieldOfView);
        //ViewmodelCamera = new Camera();
        Scene = new Scene(rendererContext);

        // VKTODO: Replace with the world's own lighting once WorldLightingInfo is ported
        LoadDefaultLighting(Scene);
    }

    /// <summary>
    /// Default sun angles (pitch, yaw) for lighting used by viewers without lighting information
    /// </summary>
    public static Vector2 DefaultSunAngles { get; } = new(80f, 170f);

    /// <summary>
    /// Default sun color for lighting used by viewers without lighting information
    /// </summary>
    public static Vector4 DefaultSunColor { get; } = new(new Vector3(255, 247, 235) / 255.0f, 2.5f);

    /// <summary>
    /// Load default lighting, used by viewers without lighting information
    /// </summary>
    public static void LoadDefaultLighting(Scene scene)
    {
        // VKTODO: Also take the default IBL cubemap and add it as an environment map, once env maps are ported
        var sunForward = EntityTransformHelper.EulerAnglesToForwardDirection(new Vector3(DefaultSunAngles.X, DefaultSunAngles.Y, 0f));
        scene.LightingInfo.LightingData.SunDirection = new Vector4(-sunForward, 0f);
        scene.LightingInfo.LightingData.SunColor = new Vector4(DefaultSunColor.AsVector3() * DefaultSunColor.W, 1f);
        scene.MarkLightingDirty();
    }

    public void SetTargetWindow(uint width, uint height, nint windowHandle)
    {
        WindowSwapchain = new Swapchain(width, height, RenderDevice!.CreateSurfaceFromWindowHandle(windowHandle));
        HdrRenderTarget = new RenderTarget(width, height, VkFormat.R16G16B16A16Sfloat, VkFormat.D32Sfloat);
    }
    public void ResizeTargets(uint width, uint height)
    {
        //We should wait on a semaphore/fence here, so we don't resize on active resources.
        HdrRenderTarget.Resize(width, height);
        WindowSwapchain!.Recreate(width, height);
    }

    public void Update(float deltaTime)
    {
        Uptime += deltaTime;
        viewConstants.Time = Uptime;
    }

    public void Render(ViewContext viewContext)
    {
        var Frame = InFlightFrames.StartNextFrame();
        RendererContext.CurrentFrame = InFlightFrames.FramesSubmitted;
        RendererContext.DestroyQueue.ProcessDestroys(InFlightFrames.FramesCompleted);

        var cmd = Frame.CommandList;

        int currIndex = WindowSwapchain!.AcquireNextImage(Frame.PresentComplete);

        if (currIndex < 0)
            return;

        viewContext.Camera.RecalculateMatrices();

        var views = CollectViews(viewContext.Camera);
        var mainView = views[0];
        var mainState = mainView.States[0];

        cmd.Begin();

        var texturesChanged = RendererContext.TextureStreaming.FinishPendingQueueTransfers(cmd);

        Scene.UploadStaticBuffers(cmd);

        if (texturesChanged)
        {
            RendererContext.MaterialLoader.PatchTextureHandles(cmd);
        }

        SetViewportConstants((int)WindowSwapchain.Extent.width, (int)WindowSwapchain.Extent.height);

        foreach (var view in views)
        {
            foreach (var state in view.States)
            {
                SetViewConstants(view, state);
                UploadViewConstants(view, state, cmd);
            }
        }

        // Reverse Z, so the far plane is at 0
        cmd.ClearRenderTarget(HdrRenderTarget, new VkClearColorValue(0.1f, 0.1f, 0.2f, 1.0f), 0);

        cmd.ClearSwapchainImage(WindowSwapchain.Images[currIndex], new VkClearColorValue(0.15f, 0.1f, 0.13f, 1.0f));

        cmd.BeginDebugLabel("Opaque Pass");
        cmd.BeginRendering(new RenderingInfo(HdrRenderTarget));

        // Negative height flips y to match the projection matrices, which are shared with the CPU side as is
        VkViewport viewport = new()
        {
            x = 0,
            y = WindowSwapchain.Extent.height,
            width = WindowSwapchain.Extent.width,
            height = -(float)WindowSwapchain.Extent.height,
            minDepth = DepthRange.Scene.Near,
            maxDepth = DepthRange.Scene.Far,
        };
        VkRect2D scissor = new() { extent = WindowSwapchain.Extent, offset = new() };

        cmd.SetViewport(viewport);
        cmd.SetScissor(scissor);

        RenderOpaqueLayer(mainView, cmd);

        // VKTODO: Draw the +X/-X/+Y/-Y axis labels once world space text is ported
        if (ShowGrid && GridPipeline != null)
        {
            cmd.BeginDebugLabel("Grid");
            DrawThrough(mainView, mainState, cmd);
            cmd.BindGraphicsPipeline(GridPipeline.Current);
            cmd.Draw(3, 1, 0, 0);
            cmd.EndDebugLabel();
        }

        cmd.EndRendering();
        cmd.EndDebugLabel();

        cmd.BeginDebugLabel("Blit to Swapchain");

        cmd.BlitImageToImage(HdrRenderTarget.ColorTarget, WindowSwapchain.Images[currIndex]);

        cmd.EndDebugLabel();

        cmd.ImageTransitionBarrier(WindowSwapchain.Images[currIndex], VkImageLayout.PresentSrcKHR);
        cmd.End();

        RenderDevice!.SubmitGraphics(cmd, Frame.PresentComplete, WindowSwapchain.WriteToImageFinishedSemaphores[currIndex], InFlightFrames.timelineSemaphore, InFlightFrames.FramesSubmitted);

        InFlightFrames.EndFrame();

        WindowSwapchain.Present(WindowSwapchain.WriteToImageFinishedSemaphores[currIndex]);

    }

    /// <summary>
    /// The views this frame draws, main view first, valid until the next call.
    /// </summary>
    private ReadOnlySpan<SceneView> CollectViews(Camera camera)
    {
        // VKTODO: Add the spawn group scenes, and the 3D sky as a second view
        mainViewSceneStates.Clear();
        mainViewSceneStates.Add(ViewStateFor(mainViewStates, Scene));

        frameViews[0] = new SceneView
        {
            States = mainViewSceneStates,
            Camera = camera,
        };

        return frameViews;
    }

    private static SceneViewState ViewStateFor(Dictionary<Scene, SceneViewState> states, Scene scene)
    {
        if (states.TryGetValue(scene, out var state))
        {
            return state;
        }

        state = new SceneViewState(scene);
        states.Add(scene, state);

        return state;
    }

    /// <summary>
    /// Switches drawing to a view: its view constants and its scene's buffers. These must always change together.
    /// </summary>
    private static void DrawThrough(in SceneView view, SceneViewState state, CommandList cmd)
    {
        cmd.PushConstants(state.ViewBuffer.DescriptorHandle);
        state.Scene.SetSceneBuffers(cmd);
    }

    private void SetViewConstants(in SceneView view, SceneViewState state)
    {
        view.Camera.SetViewConstants(viewConstants);
    }

    /// <summary>
    /// Uploads the view constants of one scene drawn through a view. Must be recorded outside a render pass
    /// instance, which is why every view is uploaded up front rather than as it is drawn.
    /// </summary>
    private unsafe void UploadViewConstants(in SceneView view, SceneViewState state, CommandList cmd)
    {
        fixed (byte* pUpload = viewConstantsUpload)
        {
            Marshal.StructureToPtr(viewConstants, (nint)pUpload, false);
        }

        const VkPipelineStageFlags2 ShaderStages = VkPipelineStageFlags2.VertexShader | VkPipelineStageFlags2.FragmentShader | VkPipelineStageFlags2.ComputeShader;

        // The previous frame may still be reading the previous contents
        cmd.BufferBarrier(ShaderStages, VkAccessFlags2.None, VkPipelineStageFlags2.Transfer, VkAccessFlags2.TransferWrite, state.ViewBuffer);
        cmd.UpdateBuffer(state.ViewBuffer, viewConstantsUpload);
        cmd.BufferBarrier(VkPipelineStageFlags2.Transfer, VkAccessFlags2.TransferWrite, ShaderStages, VkAccessFlags2.UniformRead, state.ViewBuffer);
    }

    private void SetViewportConstants(int width, int height)
    {
        viewConstants.ViewportSize = new Vector2(width, height);
        viewConstants.InvViewportSize = Vector2.One / viewConstants.ViewportSize;
    }

    private static void RenderOpaqueLayer(in SceneView view, CommandList cmd)
    {
        foreach (var state in view.States)
        {
            DrawThrough(view, state, cmd);
            state.RenderOpaqueLayer(cmd);
        }
    }

    /// <summary>
    /// Creates the renderer's own GPU resources. Must be called once the render device exists, before rendering.
    /// </summary>
    public void Initialize()
    {
        InFlightFrames ??= new InFlightFrames(2);
        GridPipeline ??= RendererContext.ShaderLoader.GetPipelineGraphics("grid", new BlendStateDescription { BlendEnable = true });
    }

    public void LoadRendererResources()
    {

    }
}
