using System.Diagnostics;
using System.IO;
using System.Reflection;
using OpenTK.Graphics.OpenGL;
using ValveResourceFormat.Renderer.Buffers;
using ValveResourceFormat.Renderer.PostProcess;
using ValveResourceFormat.Renderer.SceneEnvironment;
using ValveResourceFormat.ResourceTypes;
using Color4 = OpenTK.Mathematics.Color4;

namespace ValveResourceFormat.Renderer;

/// <summary>
/// Main renderer for Source 2 scenes with support for shadows, post-processing, and multiple render passes.
/// </summary>
public class Renderer
{
    /// <summary>
    /// Depth range for a single layer of the scene.
    /// </summary>
    /// <param name="Start">The starting depth value from the viewers perspective. Note: 1.0 = closest.</param>
    /// <param name="End">The ending depth value from the viewers perspective. Note: 0.0 = furthest.</param>
    public record DepthRange(float Start, float End)
    {
        /// <summary>The window-space near value.</summary>
        public float Near { get; } = End;

        /// <summary>The window-space far value.</summary>
        public float Far { get; } = Start;

        /// <summary>Applies the depth range to the current render state.</summary>
        public void Apply()
        {
            GL.DepthRange(Near, Far);
        }

        /// <summary>The main scene.</summary>
        public static readonly DepthRange Scene = new(0.95f, 0.05f);

        /// <summary>Reserved for the first-person viewmodel, always in front of the main scene.</summary>
        public static readonly DepthRange Viewmodel = new(1.0f, Scene.Start);

        /// <summary>Reserved for the 3D sky, always behind the main scene.</summary>
        public static readonly DepthRange Sky = new(Scene.End, 0f);
    }

    /// <summary>
    /// Occlusion culling is held off until <see cref="Uptime"/> passes this, since the geometry, shader
    /// specialization, and camera position are all still settling right after load; culling against a
    /// depth pyramid from those first frames risks hiding things that should be visible.
    /// </summary>
    private const float OcclusionCullWarmupSeconds = 1f;

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
    /// Active camera used for view and projection transforms.
    /// </summary>
    public Camera Camera { get; set; }

    /// <summary>
    /// Secondary camera used to render the first-person viewmodel layer with its own FOV.
    /// Synced to <see cref="Camera"/>'s position/orientation each frame; see <see cref="RenderScenesWithView"/>.
    /// </summary>
    public Camera ViewmodelCamera { get; }

    /// <summary>
    /// Per-frame rendering statistics, including CPU/GPU profiling timings
    /// </summary>
    public PerfStats PerfStats { get; } = new();

    /// <summary>
    /// The main scene to render.
    /// </summary>
    public Scene Scene { get; set; }

    /// <summary>
    /// Optional 3D skybox scene rendered behind the main scene.
    /// </summary>
    public Scene? SkyboxScene { get; set; }

    /// <summary>
    /// Optional 2D skybox rendered as the scene background.
    /// </summary>
    public SceneSkybox2D? Skybox2D { get; set; }

    /// <summary>
    /// Default background used when no skybox is available.
    /// </summary>
    public SceneBackground? BaseBackground { get; protected set; }

    /// <summary>
    /// GPU uniform buffer containing per-view constants such as view-projection matrices.
    /// </summary>
    public UniformBuffer<ViewConstants>? ViewBuffer { get; set; }

    /// <summary>Gets the fullscreen tile mask overlay drawn in the tile debug render modes.</summary>
    public LightTilesOverlay LightTilesOverlay { get; }

    /// <summary>
    /// Named textures bound to reserved slots for all render passes.
    /// </summary>
    public List<(ReservedTextureSlots Slot, string Name, RenderTexture Texture)> Textures { get; } = [];

    internal readonly Shader[] depthOnlyShaders = new Shader[Enum.GetValues<DepthOnlyProgram>().Length];
    private readonly Frustum barnLightShadowFrustum = new();
    /// <summary>
    /// Depth-only framebuffer used for directional (sun) light shadow mapping.
    /// </summary>
    public Framebuffer? ShadowDepthBuffer { get; private set; }

    /// <summary>
    /// Depth-only framebuffer atlas used for barn light shadow mapping.
    /// </summary>
    public Framebuffer? BarnLightShadowBuffer { get; private set; }
    /// <summary>
    /// Resolved (non-MSAA) scene color in rgba16f format, used for refraction, bloom input, and luminance computation.
    /// Filled by <see cref="GrabFramebufferCopy"/>.
    /// </summary>
    public RenderTexture? ResolvedSceneColor { get; private set; }

    /// <summary>
    /// Resolved (non-MSAA) scene depth in R32F format, used for the depth pyramid and occlusion culling.
    /// Filled by <see cref="GrabFramebufferCopy"/>.
    /// </summary>
    public RenderTexture? ResolvedSceneDepth { get; private set; }

    /// <summary>
    /// Screen space map the fancy water shader reads its ripples, silt and foam decals out of, filled by
    /// <see cref="RenderPass.WaterEffects"/> draws. Bound for all passes as <c>g_tWaterEffectsMap</c>.
    /// </summary>
    public Framebuffer? WaterEffectsBuffer { get; private set; }

    /// <summary>
    /// The value the water effects map means "nothing here" by: the ripple, silt and foam channels are
    /// read signed around 0.5.
    /// </summary>
    private static readonly Color4 WaterEffectsNeutral = new(0.5f, 0.5f, 0.5f, 0f);

    /// <summary>Whether <see cref="WaterEffectsBuffer"/> currently holds nothing but <see cref="WaterEffectsNeutral"/>.</summary>
    private bool waterEffectsMapIsNeutral;

    /// <summary>
    /// When set, forces <see cref="ResolvedSceneDepth"/> to be refreshed this frame even if no material
    /// or occlusion pass requests it. Used by overlays (e.g. world-space text) that need the scene depth
    /// to occlude themselves against geometry. Must be set before <see cref="Render(Scene.RenderContext)"/>.
    /// </summary>
    public bool ForceResolveSceneDepth { get; set; }

    private readonly Shader[] histogramShaders = new Shader[2];
    private readonly StorageBuffer[] histogramBuffers = new StorageBuffer[2];

    // Injected
    /// <summary>
    /// Target framebuffer for the main scene render; must be set before calling <see cref="Render(Scene.RenderContext)"/>.
    /// </summary>
    public Framebuffer? MainFramebuffer { get; set; }

    /// <summary>
    /// Post-processing renderer handling tone mapping, bloom, and MSAA resolve.
    /// </summary>
    public PostProcessRenderer Postprocess { get; set; }

    /// <summary>
    /// When not <see langword="null"/>, culling uses this frustum instead of the camera frustum, freezing the cull state.
    /// </summary>
    public Frustum? LockedCullFrustum { get; set; }

    /// <summary>
    /// When not <see langword="null"/>, PVS queries use this position instead of the camera position, freezing the PVS state.
    /// </summary>
    public Vector3? LockedCullPosition { get; set; }

    /// <summary>
    /// When <see langword="true"/>, every form of culling (CPU frustum, GPU meshlet, occlusion, PVS, shadow
    /// frustum) is bypassed so the whole scene is submitted.
    /// </summary>
    public bool DisableAllCulling { get; set; }

    /// <summary>Reused so <see cref="Scene.GetFrustumCullResults"/> keeps its cache across pre-warm calls.</summary>
    private readonly Frustum noCullFrustum = Frustum.CreateEmpty();

    /// <summary>The frustum to cull against, or <see langword="null"/> to use the camera's own frustum.</summary>
    private Frustum? CullFrustum => DisableAllCulling ? noCullFrustum : LockedCullFrustum;

    // options
    /// <summary>
    /// Width and height in texels of the shadow depth buffers.
    /// </summary>
    public int ShadowTextureSize { get; set; } = 1024;

    /// <summary>
    /// When <see langword="true"/>, geometry is rendered as wireframe lines.
    /// </summary>
    public bool IsWireframe { get; set; }

    /// <summary>
    /// When <see langword="true"/>, the 3D skybox scene is included in scene rendering. Does not affect the 2D skybox.
    /// </summary>
    public bool ShowSkybox { get; set; } = true;

    /// <summary>
    /// Initializes a new renderer with the given context.
    /// </summary>
    /// <param name="rendererContext">Shared context providing loaders and caches.</param>
    public Renderer(RendererContext rendererContext)
    {
        RendererContext = rendererContext;
        Postprocess = new(rendererContext);
        LightTilesOverlay = new(rendererContext);
        Camera = new Camera(rendererContext.FieldOfView);
        ViewmodelCamera = new Camera();
        Scene = new Scene(rendererContext);
    }

    /// <summary>
    /// Default sun angles for lighting used by viewers without lighting information
    /// </summary>
    public static Vector2 DefaultSunAngles { get; } = new(80f, 170f);

    /// <summary>
    /// Default sun color for lighting used by viewers without lighting information
    /// </summary>
    public static Vector4 DefaultSunColor { get; } = new(new Vector3(255, 247, 235) / 255.0f, 2.5f);

    /// <summary>
    /// Load default lighting, used by viewers without lighting information
    /// </summary>
    public static void LoadDefaultLighting(Scene scene, Resource ibl)
    {
        var texture = scene.RendererContext.MaterialLoader.LoadTexture(ibl, true);
        var environmentMap = new SceneEnvMap(scene, new AABB(new Vector3(float.MinValue), new Vector3(float.MaxValue)))
        {
            Transform = Matrix4x4.Identity,
            EdgeFadeDists = Vector3.Zero,
            HandShake = 0,
            ProjectionMode = 0,
            EnvMapTexture = texture,
        };

        scene.LightingInfo.AddEnvironmentMap(environmentMap);
        scene.LightingInfo.UseSceneBoundsForSunLightFrustum = true;

        var sunForward = EntityTransformHelper.EulerAnglesToForwardDirection(new Vector3(DefaultSunAngles.X, DefaultSunAngles.Y, 0f));
        scene.LightingInfo.LightingData.SunDirection = new Vector4(-sunForward, 0f);
        scene.LightingInfo.LightingData.SunColor =
            new Vector4(new Vector3(DefaultSunColor.X, DefaultSunColor.Y, DefaultSunColor.Z) * DefaultSunColor.W, 1f);
    }

    /// <summary>
    /// Allocates GPU resources required for rendering; must be called once before <see cref="Render(Scene.RenderContext)"/>.
    /// </summary>
    public void Initialize()
    {
        ViewBuffer = new UniformBuffer<ViewConstants>(ReservedBufferSlots.View);
        Skybox2D = BaseBackground = new SceneBackground(Scene);

        ShadowDepthBuffer = Framebuffer.Prepare(nameof(ShadowDepthBuffer), ShadowTextureSize, ShadowTextureSize, 0, null, Framebuffer.DepthAttachmentFormat.Depth32F);
        ShadowDepthBuffer.Initialize();
        ShadowDepthBuffer.ClearMask = ClearBufferMask.DepthBufferBit;
        Debug.Assert(ShadowDepthBuffer.Depth != null);

        GL.DrawBuffer(DrawBufferMode.None);
        GL.ReadBuffer(ReadBufferMode.None);
        ShadowDepthBuffer.SetShadowDepthSamplerState();
        Textures.Add(new(ReservedTextureSlots.ShadowDepthBufferDepth, "g_tShadowDepthBufferDepth", ShadowDepthBuffer.Depth));

        // Barn light shadow atlas
        BarnLightShadowBuffer = Framebuffer.Prepare(nameof(BarnLightShadowBuffer), 4, 4, 0, null, Framebuffer.DepthAttachmentFormat.Depth16);
        BarnLightShadowBuffer.Initialize();
        BarnLightShadowBuffer.ClearMask = ClearBufferMask.DepthBufferBit;
        Debug.Assert(BarnLightShadowBuffer.Depth != null);

        GL.DrawBuffer(DrawBufferMode.None);
        GL.ReadBuffer(ReadBufferMode.None);
        BarnLightShadowBuffer.SetShadowDepthSamplerState(true);
        Textures.Add(new(ReservedTextureSlots.BarnLightShadowDepth, "g_tBarnLightShadowDepth", BarnLightShadowBuffer.Depth));

        depthOnlyShaders[(int)DepthOnlyProgram.Static] = Scene.RendererContext.ShaderLoader.LoadShader("depth_only");
        //depthOnlyShaders[(int)DepthOnlyProgram.StaticAlphaTest] = GuiContext.ShaderLoader.LoadShader("depth_only", ("F_ALPHA_TEST", 1));
        depthOnlyShaders[(int)DepthOnlyProgram.Animated] = Scene.RendererContext.ShaderLoader.LoadShader("depth_only", ("D_ANIMATED", 1));
        depthOnlyShaders[(int)DepthOnlyProgram.AnimatedEightBones] = Scene.RendererContext.ShaderLoader.LoadShader("depth_only", ("D_ANIMATED", 1), ("D_EIGHT_BONE_BLENDING", 1));

        histogramShaders[0] = Scene.RendererContext.ShaderLoader.LoadShader("histogram");
        histogramShaders[1] = Scene.RendererContext.ShaderLoader.LoadShader("histogram", ("D_HISTOGRAM_MODE", 1));

        histogramBuffers[0] = StorageBuffer.Allocate<uint>(ReservedBufferSlots.Histogram, 256, BufferUsageHint.DynamicDraw);
        histogramBuffers[1] = StorageBuffer.Allocate<uint>(ReservedBufferSlots.AverageLuminance, 4, BufferUsageHint.DynamicRead);

        ResolvedSceneColor = RenderTexture.Create(4, 4, SizedInternalFormat.Rgba16f);
        ResolvedSceneColor.SetFiltering(TextureMinFilter.Linear, TextureMagFilter.Linear);
        ResolvedSceneColor.SetWrapMode(TextureWrapMode.ClampToEdge);

        ResolvedSceneDepth = RenderTexture.Create(4, 4, SizedInternalFormat.R32f);

        Textures.Add(new(ReservedTextureSlots.SceneColor, "g_tSceneColor", ResolvedSceneColor));
        Textures.Add(new(ReservedTextureSlots.SceneDepth, "g_tSceneDepth", ResolvedSceneDepth));

        // Eight bits a channel is what the water shader gets out of it: every read is either recentered
        // around 0.5 or saturated, so the map never carries range beyond what a unorm target holds.
        WaterEffectsBuffer = Framebuffer.Prepare(nameof(WaterEffectsBuffer), 4, 4, 0,
            new(PixelInternalFormat.Rgba8, PixelFormat.Rgba, PixelType.UnsignedByte), null);
        WaterEffectsBuffer.Initialize();
        WaterEffectsBuffer.CheckStatus_ThrowIfIncomplete(nameof(WaterEffectsBuffer));
        WaterEffectsBuffer.ClearColor = WaterEffectsNeutral;
        WaterEffectsBuffer.ClearMask = ClearBufferMask.ColorBufferBit;
        SetupWaterEffectsTexture();

        EnsureDepthPyramidSize(256, 256);
    }

    /// <summary>Slots out of <see cref="MaterialLoader.ShaderTextures"/> that have been resolved.</summary>
    private readonly HashSet<ReservedTextureSlots> loadedShaderTextures = [];

    /// <summary>
    /// Loads any used texture from the <see cref="MaterialLoader.ShaderTextures"/> list.
    /// </summary>
    private void LoadShaderTextures()
    {
        if (loadedShaderTextures.Count == MaterialLoader.ShaderTextures.Count)
        {
            return;
        }

        var declared = RendererContext.ShaderLoader.DeclaredReservedTextures;

        foreach (var (slot, name, path) in MaterialLoader.ShaderTextures)
        {
            if (!declared.Contains(name) || !loadedShaderTextures.Add(slot))
            {
                continue;
            }

            using var resource = RendererContext.FileLoader.LoadFileCompiled(path);

            var texture = resource != null
                ? RendererContext.MaterialLoader.LoadTexture(resource)
                : RendererContext.MaterialLoader.GetDefaultColor();

            Textures.Add(new(slot, name, texture));
        }
    }

    /// <summary>
    /// Loads embedded or game-provided BRDF LUT, cube fog, and blue noise textures into <see cref="Textures"/>.
    /// </summary>
    public void LoadRendererResources()
    {
        var rendererAssembly = Assembly.GetAssembly(typeof(RendererContext)) ?? throw new InvalidOperationException("Failed to get renderer assembly");
        const string vtexFileName = "brdf_lut.vtex_c";

        // Load brdf lut, preferably from game.
        var brdfLutResource = RendererContext.FileLoader.LoadFile("textures/dev/" + vtexFileName);

        const int BrdfTextureSize = 64;
        const int BrdfTextureDepth = 3;

        if (brdfLutResource?.DataBlock is not Texture gameBrdfLut
        || gameBrdfLut.Width != BrdfTextureSize
        || gameBrdfLut.Height != BrdfTextureSize
        || gameBrdfLut.Depth != BrdfTextureDepth
        || gameBrdfLut.Format != VTexFormat.RGBA16161616F)
        {
            brdfLutResource?.Dispose();
            brdfLutResource = null;
        }

        try
        {
            if (brdfLutResource == null)
            {
                // Will be used by LoadTexture, and disposed by resource
                var brdfStream = rendererAssembly.GetManifestResourceStream("Renderer.Resources." + vtexFileName)
                    ?? throw new InvalidOperationException($"Failed to load embedded resource: {vtexFileName}");

                brdfLutResource = new Resource() { FileName = vtexFileName };
                brdfLutResource.Read(brdfStream);
            }

            var brdfLutTexture = Scene.RendererContext.MaterialLoader.LoadTexture(brdfLutResource);
            brdfLutTexture.SetWrapMode(TextureWrapMode.ClampToEdge);
            Textures.Add(new(ReservedTextureSlots.BRDFLookup, "g_tBRDFLookup", brdfLutTexture));
        }
        finally
        {
            brdfLutResource?.Dispose();
        }

        // Load default cube fog texture.
        using var cubeFogStream = rendererAssembly.GetManifestResourceStream("Renderer.Resources.sky_furnace.vtex_c") ?? throw new InvalidOperationException("Failed to load embedded cube fog texture.");
        using var cubeFogResource = new Resource() { FileName = "default_cube.vtex_c" };
        cubeFogResource.Read(cubeFogStream);

        var defaultCubeTexture = Scene.RendererContext.MaterialLoader.LoadTexture(cubeFogResource);
        Textures.Add(new(ReservedTextureSlots.FogCubeTexture, "g_tFogCubeTexture", defaultCubeTexture));


        const string blueNoiseName = "blue_noise_256.vtex_c";
        var blueNoiseResource = RendererContext.FileLoader.LoadFile("textures/dev/" + blueNoiseName);

        try
        {
            Stream? blueNoiseStream; // Same method as brdf

            if (blueNoiseResource == null)
            {
                blueNoiseStream = rendererAssembly.GetManifestResourceStream("Renderer.Resources." + blueNoiseName);

                if (blueNoiseStream == null)
                {
                    throw new InvalidOperationException($"Failed to load embedded resource: {blueNoiseName}");
                }

                blueNoiseResource = new Resource() { FileName = blueNoiseName };
                blueNoiseResource.Read(blueNoiseStream);
            }

            var blueNoise = Scene.RendererContext.MaterialLoader.LoadTexture(blueNoiseResource);
            Postprocess.BlueNoise = blueNoise;
            Textures.Add(new(ReservedTextureSlots.BlueNoise, "g_tBlueNoise", blueNoise));
        }
        finally
        {
            blueNoiseResource?.Dispose();
        }
    }

    void UpdatePerViewGpuBuffers(Scene scene, Camera camera, float deltaTime)
    {
        Debug.Assert(ViewBuffer != null);

        {
            // Skip occlusion culling if the camera moved too much -- we use last frame depth
            var moveDelta = ViewBuffer.Data.CameraPosition - camera.Location;
            var eyeDelta = ViewBuffer.Data.CameraDirWs - camera.Forward;

            var t = moveDelta.LengthSquared();
            var t2 = eyeDelta.LengthSquared();

            if (t > 5000f || t2 > 0.5f)
            {
                scene.DepthPyramidValid = false;
            }
            else
            {
                ViewBuffer.Data.WorldToProjectionPrev = scene.DepthPyramidViewProjection;
            }

            scene.UpdateIndirectRenderingState();
        }

        camera.SetViewConstants(ViewBuffer.Data);
        scene.SetFogConstants(ViewBuffer.Data);

        var cullWidth = (int)ViewBuffer.Data.ViewportSize.X;
        var cullHeight = (int)ViewBuffer.Data.ViewportSize.Y;

        var tileCullEnabled = LockedCullFrustum == null && scene.EnableTiledLightCulling;
        scene.LightBinner.Update(ViewBuffer.Data, cullWidth, cullHeight, tileCullEnabled);
        SkyboxScene?.LightBinner.Update(ViewBuffer.Data, cullWidth, cullHeight, tileCullEnabled);

        ViewBuffer.BindBufferBase();
        ViewBuffer.Update();

        // A locked cull frustum leaves the indirect buffers untouched, freezing the cull state. Disabled
        // culling still has to dispatch, otherwise the indirect draw commands keep the previous contents.
        Frustum? gpuCullFrustum = DisableAllCulling
            ? noCullFrustum
            : LockedCullFrustum == null ? camera.ViewFrustum : null;

        if (gpuCullFrustum.HasValue)
        {
            if (scene.DrawMeshletsIndirect)
            {
                scene.MeshletCullGpu(gpuCullFrustum.Value);
            }

            if (scene.CompactMeshletDraws)
            {
                scene.CompactIndirectDraws();
            }

            using (new GLDebugGroup("Cull Tiles and Depth Bins"))
            {
                scene.LightBinner.Dispatch();
                SkyboxScene?.LightBinner.Dispatch();
            }
        }

        if (Postprocess != null)
        {
            Postprocess.State = scene.PostProcessInfo.CurrentState;
            Postprocess.ResolveColorCorrection(scene.PostProcessInfo.ActiveLuts);
            Postprocess.CalculateTonemapScalar(deltaTime);
        }
    }

    private static void RenderTranslucentLayer(Scene scene, Scene.RenderContext renderContext)
    {
        scene.RenderOpaqueRefractLayer(renderContext);
        scene.RenderWaterLayer(renderContext);

        GL.DepthMask(false);
        GL.Enable(EnableCap.Blend);

        scene.RenderTranslucentLayer(renderContext);

        GL.Disable(EnableCap.Blend);
        GL.DepthMask(true);
    }

    /// <summary>
    /// Renders the opaque and translucent layers of the main scene to <see cref="MainFramebuffer"/>.
    /// </summary>
    public void DrawMainScene()
    {
        if (MainFramebuffer is null)
        {
            throw new InvalidOperationException("MainFramebuffer must be set before rendering");
        }

        var renderContext = new Scene.RenderContext
        {
            Camera = Camera,
            Framebuffer = MainFramebuffer,
            Scene = Scene,
            Textures = Textures,
        };

        LoadShaderTextures();
        UpdatePerViewGpuBuffers(Scene, Camera, DeltaTime);
        Scene.SetSceneBuffers();

        Scene.RenderOpaqueLayer(renderContext);
        RenderWaterEffectsMap(renderContext);
        RenderTranslucentLayer(Scene, renderContext);
    }

    /// <summary>
    /// Renders the scene to the specified framebuffer. The result will be in linear space.
    /// </summary>
    /// <param name="framebuffer">Framebuffer with hdr color support.</param>
    public void Render(Framebuffer framebuffer)
    {
        var renderContext = new Scene.RenderContext
        {
            Camera = Camera,
            Framebuffer = framebuffer,
            Scene = Scene,
            Textures = Textures,
        };

        Render(renderContext);
    }


    /// <summary>
    /// Renders shadows and then the full scene using the provided render context.
    /// </summary>
    public void Render(Scene.RenderContext renderContext)
    {
        LoadShaderTextures();

        // Render backfaces into shadow maps
        GL.FrontFace(FrontFaceDirection.Cw);

        RenderSceneShadows(renderContext);
        RenderBarnLightShadows(renderContext);

        GL.FrontFace(FrontFaceDirection.Ccw);

        RenderScenesWithView(renderContext);
    }

    /// <summary>
    /// Renders the main and skybox scenes using the camera and framebuffer specified in the render context.
    /// </summary>
    public void RenderScenesWithView(Scene.RenderContext renderContext)
    {
        if (ViewBuffer == null)
        {
            throw new InvalidOperationException("Initialize() must be called before rendering");
        }

        var (w, h) = (renderContext.Framebuffer.Width, renderContext.Framebuffer.Height);

        GL.Viewport(0, 0, w, h);
        ViewBuffer.Data.ViewportSize = new Vector2(w, h);
        ViewBuffer.Data.InvViewportSize = Vector2.One / ViewBuffer.Data.ViewportSize;

        renderContext.Framebuffer.BindAndClear();

        var isMainFramebuffer = ReferenceEquals(renderContext.Framebuffer, MainFramebuffer);
        var isStandardPass = renderContext.ReplacementShader == null && isMainFramebuffer;

        if (!isStandardPass)
        {
            PerfStats.Active.SuspendTriangleCounter();
        }

        var isWireframe = IsWireframe && isStandardPass; // To avoid toggling it mid frame
        var computeFramebufferLuminance = Postprocess.State.ExposureSettings.AutoExposureEnabled;


        // TODO: check if renderpass allows wireframe mode
        // TODO+: replace wireframe shaders with solid color
        if (isWireframe)
        {
            GL.PolygonMode(TriangleFace.FrontAndBack, PolygonMode.Line);
        }

        UpdatePerViewGpuBuffers(Scene, renderContext.Camera, DeltaTime);

        using (new GLDebugGroup("Viewmodel Opaque"))
        {
            var mainCamera = renderContext.Camera;

            ViewmodelCamera.CopyFrom(mainCamera);
            ViewmodelCamera.FieldOfView = ComputeViewmodelFov();
            ViewmodelCamera.CreateProjectionMatrix();
            ViewmodelCamera.RecalculateMatrices();

            DepthRange.Viewmodel.Apply();

            ViewmodelCamera.SetViewConstants(ViewBuffer.Data);
            Scene.SetFogConstants(ViewBuffer.Data);

            var viewmodelTileRemap = ViewmodelCamera.GetPixelRemapTo(mainCamera, ViewBuffer.Data.ViewportSize);
            Scene.LightBinner.SetPixelRemap(viewmodelTileRemap);

            ViewBuffer.BindBufferBase();
            ViewBuffer.Update();
            Scene.SetSceneBuffers();

            renderContext.Camera = ViewmodelCamera;
            renderContext.Scene = Scene;
            Scene.RenderViewmodelOpaqueLayer(renderContext);
            renderContext.Camera = mainCamera;

            DepthRange.Scene.Apply();

            mainCamera.SetViewConstants(ViewBuffer.Data);
            Scene.SetFogConstants(ViewBuffer.Data);
            Scene.LightBinner.SetPixelRemap(ViewConstants.PixelRemapIdentity);
            ViewBuffer.BindBufferBase();
            ViewBuffer.Update();
        }

        Scene.SetSceneBuffers();

        using (new GLDebugGroup("Main Scene Opaque Render"))
        {
            renderContext.Scene = Scene;
            Scene.RenderOpaqueLayer(renderContext, isStandardPass ? depthOnlyShaders : Span<Shader>.Empty);
        }

        // Both the 3D sky scene and the main scene draw water below, and both read the same map.
        RenderWaterEffectsMap(renderContext);

        //using (new GLDebugGroup("Sky Render"))
        {
            DepthRange.Sky.Apply();

            renderContext.ReplacementShader?.SetUniform1("isSkybox", 1u);
            var skyboxScene = SkyboxScene;
            var render3DSkybox = ShowSkybox && skyboxScene != null;
            var (copyColor, copyDepth) = (Scene.WantsSceneColor, Scene.WantsSceneDepth);
            copyDepth |= ForceResolveSceneDepth;
            Postprocess.HasOutlineObjects = Scene.HasOutlineObjects;

            if (render3DSkybox)
            {
                Debug.Assert(skyboxScene is not null); // analyzer is failing here

                skyboxScene.SetSceneBuffers();
                renderContext.Scene = skyboxScene;

                copyColor |= skyboxScene.WantsSceneColor;
                copyDepth |= skyboxScene.WantsSceneDepth;
                Postprocess.HasOutlineObjects |= skyboxScene.HasOutlineObjects;

                using var _ = new GLDebugGroup("3D Sky Scene");
                skyboxScene.RenderOpaqueLayer(renderContext);
            }

            if (!isWireframe)
            {
                using (new GLDebugGroup("2D Sky Render"))
                {
                    Skybox2D?.Render();
                }
            }

            copyColor |= computeFramebufferLuminance;

            if (isMainFramebuffer)
            {
                var generateDepthPyramid = Scene.EnableOcclusionCulling
                    && Scene.DrawMeshletsIndirect
                    && LockedCullFrustum == null
                    && !DisableAllCulling
                    && Uptime >= OcclusionCullWarmupSeconds;

                copyDepth |= generateDepthPyramid;
                Scene.DepthPyramidValid = !DisableAllCulling && (generateDepthPyramid || LockedCullFrustum != null);

                GrabFramebufferCopy(renderContext.Framebuffer, copyColor, copyDepth);

                if (generateDepthPyramid)
                {
                    Debug.Assert(ResolvedSceneColor != null && ResolvedSceneDepth != null);
                    EnsureDepthPyramidSize(renderContext.Framebuffer.Width, renderContext.Framebuffer.Height);
                    Scene.GenerateDepthPyramid(ResolvedSceneDepth);
                    Scene.DepthPyramidViewProjection = Camera.ViewProjectionMatrix;
                    Scene.DepthPyramidValid = true;
                }
            }

            if (render3DSkybox)
            {
                Debug.Assert(skyboxScene is not null); // analyzer is failing here

                using (new GLDebugGroup("3D Sky Scene Translucent Render"))
                {
                    RenderTranslucentLayer(skyboxScene, renderContext);
                }

                // Back to main scene.
                Scene.SetSceneBuffers();
                renderContext.Scene = Scene;
            }

            renderContext.ReplacementShader?.SetUniform1("isSkybox", 0u);
            DepthRange.Scene.Apply();
        }

        using (new GLDebugGroup("Main Scene Translucent Render"))
        {
            RenderTranslucentLayer(Scene, renderContext);
        }

        using (new GLDebugGroup("Viewmodel Translucent"))
        {
            var mainCamera = renderContext.Camera;

            DepthRange.Viewmodel.Apply();

            ViewmodelCamera.SetViewConstants(ViewBuffer.Data);
            Scene.SetFogConstants(ViewBuffer.Data);
            Scene.LightBinner.SetPixelRemap(
                ViewmodelCamera.GetPixelRemapTo(mainCamera, ViewBuffer.Data.ViewportSize));
            ViewBuffer.BindBufferBase();
            ViewBuffer.Update();

            renderContext.Camera = ViewmodelCamera;
            Scene.RenderViewmodelTranslucentLayer(renderContext);
            renderContext.Camera = mainCamera;

            DepthRange.Scene.Apply();

            mainCamera.SetViewConstants(ViewBuffer.Data);
            Scene.SetFogConstants(ViewBuffer.Data);
            Scene.LightBinner.SetPixelRemap(ViewConstants.PixelRemapIdentity);
            ViewBuffer.BindBufferBase();
            ViewBuffer.Update();
        }

        if (isWireframe)
        {
            GL.PolygonMode(TriangleFace.FrontAndBack, PolygonMode.Fill);
        }

        if (isStandardPass)
        {
            if (computeFramebufferLuminance)
            {
                ComputeAverageLuminance(renderContext);
            }

            if (Postprocess.HasOutlineObjects)
            {
                RenderOutlineLayer(renderContext);
            }

            var overlayBatch = ValveResourceFormat.Renderer.LightTilesOverlay.BatchFor(ViewBuffer!.Data.RenderMode);

            if (overlayBatch != ValveResourceFormat.Renderer.LightTilesOverlay.Batch.None)
            {
                var (tileBase, words) = Scene.LightBinner.GetOverlayRegion(
                    overlayBatch == ValveResourceFormat.Renderer.LightTilesOverlay.Batch.EnvMaps);

                LightTilesOverlay.Render(Scene.LightBinner.CullBits, tileBase, words);
            }
        }
        else
        {
            PerfStats.Active.ResumeTriangleCounter();
        }
    }

    /// <summary>
    /// Computes the first-person viewmodel camera's FOV.
    /// </summary>
    private float ComputeViewmodelFov()
    {
        var fovRatio = RendererContext.FieldOfView / 90f;

        return RendererContext.ViewmodelFieldOfView * fovRatio;
    }

    /// <summary>
    /// Renders opaque shadow casters for the directional (sun) light into <see cref="ShadowDepthBuffer"/>.
    /// </summary>
    public void RenderSceneShadows(Scene.RenderContext renderContext)
    {
        if (ShadowDepthBuffer is null || ViewBuffer is null)
        {
            throw new InvalidOperationException("Initialize() must be called before rendering");
        }

        GL.Viewport(0, 0, ShadowDepthBuffer.Width, ShadowDepthBuffer.Height);
        ShadowDepthBuffer.Bind(FramebufferTarget.Framebuffer);
        GL.DepthRange(0, 1);
        GL.Clear(ClearBufferMask.DepthBufferBit);

        renderContext.Framebuffer = ShadowDepthBuffer;
        renderContext.Scene = Scene;

        ViewBuffer.Data.WorldToProjection = Scene.LightingInfo.SunViewProjection;
        var worldToShadow = Scene.LightingInfo.SunViewProjection;
        ViewBuffer.Data.WorldToShadow = worldToShadow;
        ViewBuffer.Data.SunLightShadowBias = Scene.LightingInfo.SunLightShadowBias;
        ViewBuffer.Update();

        using (new GLDebugGroup("Direct Light Shadows"))
        {
            PerfStats.Active.Count(Counter.DirectionalShadowMap);
            Scene.RenderOpaqueShadows(renderContext, depthOnlyShaders, Scene.CulledShadowDrawCalls);
        }
    }

    private void RenderBarnLightShadows(Scene.RenderContext renderContext)
    {
        Debug.Assert(ViewBuffer != null);

        if (!ViewBuffer.Data!.ExperimentalLightsEnabled)
        {
            return;
        }

        if (Scene.LightingInfo.ShadowMapper.ShadowCasters.Count == 0)
        {
            return;
        }

        using var _ = new GLDebugGroup("Barn Light Shadows");
        Debug.Assert(BarnLightShadowBuffer != null);

        GL.DepthFunc(DepthFunction.Lequal);
        GL.DepthRange(0.0, 1.0);
        GL.ClearDepth(1.0);

        GL.Enable(EnableCap.PolygonOffsetFill);
        GL.PolygonOffset(2f, 0f);

        BarnLightShadowBuffer.Bind(FramebufferTarget.Framebuffer);

        var atlasSize = Scene.LightingInfo.BarnLightShadowAtlasSize;

        if (BarnLightShadowBuffer.Resize(atlasSize, atlasSize))
        {
            BarnLightShadowBuffer.SetShadowDepthSamplerState(true);
            Textures.RemoveAll(t => t.Slot == ReservedTextureSlots.BarnLightShadowDepth);
            Textures.Add(new(ReservedTextureSlots.BarnLightShadowDepth, "g_tBarnLightShadowDepth", BarnLightShadowBuffer.Depth!));
        }

        GL.Enable(EnableCap.ScissorTest);
        GL.Viewport(0, 0, BarnLightShadowBuffer.Width, BarnLightShadowBuffer.Height);
        GL.Scissor(0, 0, BarnLightShadowBuffer.Width, BarnLightShadowBuffer.Height);
        GL.Clear(ClearBufferMask.DepthBufferBit);

        foreach (var caster in Scene.LightingInfo.ShadowMapper.ShadowCasters)
        {
            var region = caster.Region;

            if (region.Width == 0)
            {
                continue;
            }

            PerfStats.Active.Count(Counter.BarnShadowMap);

            GL.Viewport(region.X, region.Y, region.Width, region.Height);
            GL.Scissor(region.X, region.Y, region.Width, region.Height);

            ViewBuffer.Data.WorldToProjection = caster.WorldToFrustum;
            ViewBuffer.Update();

            barnLightShadowFrustum.Update(caster.WorldToFrustum);

            // This is performing culling mid render, reusing the scene draw lists.
            // Should be in update loop.
            Scene.SetupBarnLightFaceShadow(caster.Light, caster.FaceIndex, barnLightShadowFrustum);

            Scene.RenderOpaqueShadows(renderContext, depthOnlyShaders, caster.Light.FaceShadowCache[caster.FaceIndex].DrawCalls!);
        }

        GL.Disable(EnableCap.ScissorTest);
        GL.Disable(EnableCap.PolygonOffsetFill);

        GL.DepthFunc(DepthFunction.Greater);
        GL.ClearDepth(0.0);
    }

    private void ComputeAverageLuminance(Scene.RenderContext renderContext)
    {
        Debug.Assert(ResolvedSceneColor != null);

        using var _ = new GLDebugGroup("Compute Average Luminance");

        var width = ResolvedSceneColor.Width;
        var height = ResolvedSceneColor.Height;

        static void Dispatch(Shader shader, RenderTexture texture, int x, int y)
        {
            var logMin = -8f;
            var logRange = 13f;

            shader.Use();
            shader.SetTexture(0, "inputImage", texture);
            shader.SetUniform1("logMinLuminance", logMin);
            shader.SetUniform1("logLuminanceRange", logRange);

            GL.DispatchCompute(x, y, 1);
        }

        histogramBuffers[0].Clear();
        histogramBuffers[0].BindBufferBase();
        histogramBuffers[1].BindBufferBase();

        var inputTex = ResolvedSceneColor;

        // Build histogram
        var groupsX = Math.Max(1, (width + 15) / 16);
        var groupsY = Math.Max(1, (height + 15) / 16);
        Dispatch(histogramShaders[0], inputTex, groupsX, groupsY);
        GL.MemoryBarrier(MemoryBarrierFlags.ShaderStorageBarrierBit);

        // Reduce histogram
        Dispatch(histogramShaders[1], inputTex, 1, 1); // local_size_x = 256

        GL.MemoryBarrier(MemoryBarrierFlags.ShaderStorageBarrierBit | MemoryBarrierFlags.BufferUpdateBarrierBit);

        var output = Vector4.Zero;
        histogramBuffers[1].Read(ref output);
        Postprocess.AverageLuminance = output.X;
    }

    private void RenderOutlineLayer(Scene.RenderContext renderContext)
    {
        using var _ = new GLDebugGroup("Outline Stencil Write");

        GL.DepthMask(false);
        GL.Disable(EnableCap.DepthTest);
        GL.Disable(EnableCap.CullFace);

        GL.Enable(EnableCap.StencilTest);
        GL.StencilOp(StencilOp.Keep, StencilOp.Keep, StencilOp.Replace);
        GL.StencilFunc(StencilFunction.Always, 1, 0xFF);
        GL.StencilMask(0xFF);

        SkyboxScene?.RenderOutlineLayer(renderContext);
        Scene.RenderOutlineLayer(renderContext);

        GL.Disable(EnableCap.StencilTest);
        GL.Enable(EnableCap.CullFace);
        GL.Enable(EnableCap.DepthTest);
        GL.DepthMask(true);
    }

    /// <summary>Points the reserved <c>g_tWaterEffectsMap</c> slot at the current color attachment.</summary>
    private void SetupWaterEffectsTexture()
    {
        Debug.Assert(WaterEffectsBuffer?.Color != null);

        // The shader reprojects world positions into this map and samples them a few texels apart, so the
        // reads land off-center and off-screen; clamping keeps the edges from wrapping ripples around.
        WaterEffectsBuffer.Color.SetFiltering(TextureMinFilter.Linear, TextureMagFilter.Linear);
        WaterEffectsBuffer.Color.SetWrapMode(TextureWrapMode.ClampToEdge);

        Textures.RemoveAll(static t => t.Slot == ReservedTextureSlots.WaterEffectsMap);
        Textures.Add(new(ReservedTextureSlots.WaterEffectsMap, "g_tWaterEffectsMap", WaterEffectsBuffer.Color));
    }

    /// <summary>
    /// Draws the nodes flagged <see cref="CustomRenderPasses.WaterEffects"/> into <see cref="WaterEffectsBuffer"/>,
    /// which the water pass then samples. Must run before any water layer this frame.
    /// </summary>
    private void RenderWaterEffectsMap(Scene.RenderContext renderContext)
    {
        Debug.Assert(WaterEffectsBuffer != null);

        var skyboxScene = ShowSkybox ? SkyboxScene : null;
        var hasDraws = Scene.HasWaterEffects || skyboxScene?.HasWaterEffects == true;

        // A map that is already neutral everywhere stays a valid thing to sample, so there is nothing to
        // do until something actually draws into it again.
        if (!hasDraws && waterEffectsMapIsNeutral)
        {
            return;
        }

        using var _ = new GLDebugGroup("Water Effects Render");

        var (width, height) = (renderContext.Framebuffer.Width, renderContext.Framebuffer.Height);

        if (WaterEffectsBuffer.Resize(width, height))
        {
            SetupWaterEffectsTexture();
        }

        // The render context is a struct, so the scene framebuffer has to be remembered rather than
        // restored: only the GL state below outlives this call.
        var sceneFramebuffer = renderContext.Framebuffer;

        GL.Viewport(0, 0, width, height);
        WaterEffectsBuffer.BindAndClear();

        // The map is a flat screen space accumulation with no depth of its own, and the particle renderers
        // set up their own blend state per draw.
        GL.Disable(EnableCap.DepthTest);
        renderContext.Framebuffer = WaterEffectsBuffer;

        if (hasDraws)
        {
            if (skyboxScene != null)
            {
                renderContext.Scene = skyboxScene;
                skyboxScene.RenderWaterEffectsLayer(renderContext);
            }

            renderContext.Scene = Scene;
            Scene.RenderWaterEffectsLayer(renderContext);
        }

        GL.Disable(EnableCap.Blend);
        GL.DepthMask(true);
        GL.Enable(EnableCap.DepthTest);

        GL.Viewport(0, 0, sceneFramebuffer.Width, sceneFramebuffer.Height);
        sceneFramebuffer.Bind(FramebufferTarget.Framebuffer);

        waterEffectsMapIsNeutral = !hasDraws;
    }

    private void EnsureResolvedTextureSize(int width, int height)
    {
        if (ResolvedSceneColor!.Width != width ||
            ResolvedSceneColor.Height != height)
        {
            ResolvedSceneColor.Delete();
            ResolvedSceneColor = RenderTexture.Create(width, height, SizedInternalFormat.Rgba16f);
            ResolvedSceneColor.SetFiltering(TextureMinFilter.Linear, TextureMagFilter.Linear);
            ResolvedSceneColor.SetWrapMode(TextureWrapMode.ClampToEdge);

            ResolvedSceneDepth!.Delete();
            ResolvedSceneDepth = RenderTexture.Create(width, height, SizedInternalFormat.R32f);

            Textures.RemoveAll(static t => t.Slot == ReservedTextureSlots.SceneColor || t.Slot == ReservedTextureSlots.SceneDepth);
            Textures.Add(new(ReservedTextureSlots.SceneColor, "g_tSceneColor", ResolvedSceneColor));
            Textures.Add(new(ReservedTextureSlots.SceneDepth, "g_tSceneDepth", ResolvedSceneDepth));
        }
    }

    /// <summary>
    /// Resolves MSAA and copies color and/or depth from the framebuffer into <see cref="ResolvedSceneColor"/> and <see cref="ResolvedSceneDepth"/>.
    /// </summary>
    public void GrabFramebufferCopy(Framebuffer framebuffer, bool copyColor, bool copyDepth)
    {
        if (!copyColor && !copyDepth)
        {
            return;
        }

        using var _ = new GLDebugGroup("Framebuffer Copy");

        EnsureResolvedTextureSize(framebuffer.Width, framebuffer.Height);

        Postprocess.ResolveMsaa(framebuffer, ResolvedSceneColor!, ResolvedSceneDepth!, copyColor, copyDepth);

        framebuffer.Bind(FramebufferTarget.Framebuffer);
    }

    /// <summary>
    /// Multisampling resolve, postprocess the image, and convert to gamma.
    /// </summary>
    public void PostprocessRender(Framebuffer inputFramebuffer, Framebuffer outputFramebuffer, bool flipY = false)
    {
        using var _ = new GLDebugGroup("Post Processing");

        inputFramebuffer.Bind(FramebufferTarget.ReadFramebuffer);
        outputFramebuffer.Bind(FramebufferTarget.DrawFramebuffer);

        Debug.Assert(inputFramebuffer.NumSamples > 0);
        Debug.Assert(outputFramebuffer.NumSamples == 0);

        EnsureResolvedTextureSize(inputFramebuffer.Width, inputFramebuffer.Height);

        Postprocess.Render(inputFramebuffer, outputFramebuffer, ResolvedSceneColor!, Camera, flipY);
    }

    /// <summary>
    /// Gets or sets whether the vsnd name of every active positioned sound is billboarded in the world.
    /// </summary>
    public bool ShowSoundDebug { get; set; }

    // Reused buffers for the sound debug billboards and 2D (non-positioned) sound list
    private readonly List<(Vector3 Position, string Text)> debugWorldSounds = [];
    private readonly List<string> debugFlatSounds = [];

    /// <summary>
    /// Releases GPU resources owned by this renderer.
    /// </summary>
    public void Dispose()
    {
        ViewBuffer?.Dispose();
        Scene?.Dispose();
        SkyboxScene?.Dispose();
        PerfStats?.Dispose();
        ResolvedSceneColor?.Delete();
        ResolvedSceneDepth?.Delete();
        WaterEffectsBuffer?.Delete();
        Skybox2D?.Delete();

        if (BaseBackground != Skybox2D && BaseBackground != null)
        {
            BaseBackground.Delete();
        }
    }

    /// <summary>
    /// Advances the simulation, updates scene draw calls, and prepares shadow data for the next frame.
    /// </summary>
    public void Update(Scene.UpdateContext updateContext)
    {
        if (ViewBuffer is null || ShadowDepthBuffer is null)
        {
            throw new InvalidOperationException("Initialize() must be called before updating");
        }

        Uptime += updateContext.Timestep;
        DeltaTime = updateContext.Timestep;
        ViewBuffer.Data.Time = Uptime;

        updateContext = updateContext with { Uptime = Uptime };

        Camera.RecalculateMatrices();

        Scene.Update(updateContext);
        SkyboxScene?.Update(updateContext);

        Scene.PostProcessInfo.UpdatePostProcessing(updateContext.Camera, updateContext.Timestep);

        Scene.SetupSceneShadows(updateContext.Camera, DisableAllCulling ? -1 : ShadowDepthBuffer.Width);

        if (ViewBuffer.Data.ExperimentalLightsEnabled)
        {
            Scene.LightingInfo.BinBarnLights(Camera, ShadowTextureSize);
        }

        if (!DisableAllCulling && Scene is { EnablePvsCulling: true, VoxelVisibility: not null })
        {
            var pvsPosition = LockedCullPosition ?? updateContext.Camera.Location;
            Scene.CurrentFramePvs = Scene.VoxelVisibility.GetPVSForPoint(pvsPosition);
        }
        else
        {
            Scene.CurrentFramePvs = null;
        }

        var cullFrustum = CullFrustum;
        Scene.CollectSceneDrawCalls(updateContext.Camera, cullFrustum);
        SkyboxScene?.CollectSceneDrawCalls(updateContext.Camera, cullFrustum);

        if (ShowSoundDebug && Sound.Player != null)
        {
            CollectSoundDebugText(updateContext);
        }
    }

    /// <summary>
    /// Queues a billboard per audible positioned sound, and a bottom-right corner list of the
    /// non-positioned (2D) ones.
    /// </summary>
    private void CollectSoundDebugText(Scene.UpdateContext updateContext)
    {
        debugWorldSounds.Clear();
        debugFlatSounds.Clear();
        Sound.Player!.CollectDebugSounds(debugWorldSounds, debugFlatSounds);

        foreach (var (position, text) in debugWorldSounds)
        {
            updateContext.TextRenderer.AddTextBillboard(position, new TextRenderer.TextRenderRequest
            {
                Scale = 8f,
                Text = text,
                CenterHorizontal = true,
                Color = new Color32(0.4f, 1f, 0.4f, 1f),
            }, updateContext.Camera);
        }

        if (debugFlatSounds.Count == 0)
        {
            return;
        }

        const float scale = 10f;
        const float lineHeight = scale * 1.5f;
        const float marginRight = 8f;
        const float marginBottom = 8f;

        // Right edge every line is aligned to, so the ".vsnd" suffix lines up flush against the screen corner.
        var cornerX = updateContext.Camera.WindowSize.X - marginRight;
        var y = updateContext.Camera.WindowSize.Y - marginBottom - (debugFlatSounds.Count * lineHeight);

        foreach (var text in debugFlatSounds)
        {
            updateContext.TextRenderer.AddText(new TextRenderer.TextRenderRequest
            {
                X = cornerX - TextRenderer.MeasureTextWidth(text, scale),
                Y = y,
                Scale = scale,
                Text = text,
                Color = new Color32(0.4f, 1f, 1f, 1f),
            });

            y += lineHeight;
        }
    }

    void EnsureDepthPyramidSize(int width, int height)
    {
        // Get the target pyramid size
        var maxDim = Math.Max(width, height);
        var cappedDim = Math.Min(maxDim, 256);
        var targetSize = 1 << (int)Math.Floor(Math.Log2(cappedDim));

        if (Scene.DepthPyramid != null && Scene.DepthPyramid.Width == targetSize && Scene.DepthPyramid.Height == targetSize)
        {
            return;
        }

        Scene.DepthPyramid?.Delete();

        // Calculate mips needed to go from targetSize down to 1x1
        var maxMipLevel = (int)Math.Log2(targetSize);

        Scene.DepthPyramid = RenderTexture.Create(targetSize, targetSize, SizedInternalFormat.R32f, maxMipLevel + 1);
        Scene.DepthPyramid.SetLabel("DepthPyramid");

        Scene.DepthPyramid.SetBaseMaxLevel(0, maxMipLevel);
    }
}
