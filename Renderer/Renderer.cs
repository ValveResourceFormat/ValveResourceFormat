using System.Diagnostics;
using System.Reflection;
using OpenTK.Graphics.OpenGL;
using ValveResourceFormat.Renderer.Entities;
using ValveResourceFormat.Renderer.PostProcess;
using ValveResourceFormat.Renderer.SceneEnvironment;
using ValveResourceFormat.Renderer.World;
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
    /// Camera the 3D sky is drawn through. Follows the main camera, see <see cref="World.Skybox3D.ConfigureCamera"/>.
    /// </summary>
    public Camera SkyCamera { get; }

    /// <summary>
    /// Per-frame rendering statistics, including CPU/GPU profiling timings
    /// </summary>
    public PerfStats PerfStats { get; }

    /// <summary>
    /// The main scene to render.
    /// </summary>
    public Scene Scene { get; }

    /// <summary>
    /// The entity world the scenes are spawned into, ticked once a frame ahead of the scenes that draw it.
    /// </summary>
    public EntitySystem EntitySystem { get; }

    /// <summary>Finds the node an entity was loaded as, in whichever scene it went into.</summary>
    /// <param name="entity">The entity to look for.</param>
    /// <returns>Its node, or <see langword="null"/> when it has none.</returns>
    public SceneNode? FindNode(EntityLump.Entity entity)
    {
        foreach (var scene in Scenes)
        {
            if (scene.Find(entity) is { } node)
            {
                return node;
            }
        }

        return null;
    }

    /// <summary>Finds a node by its entity's <c>targetname</c>, in whichever scene it went into.</summary>
    /// <param name="pattern">The target name to match.</param>
    /// <returns>The first matching node, or <see langword="null"/> when there is none.</returns>
    public SceneNode? FindNodeByTargetName(string pattern)
    {
        foreach (var scene in Scenes)
        {
            if (scene.FindNodeByTargetName(pattern) is { } node)
            {
                return node;
            }
        }

        return null;
    }

    /// <summary>The scenes this renderer draws, the map's first and its 3D sky's after it.</summary>
    public IEnumerable<Scene> Scenes
    {
        get
        {
            yield return Scene;

            if (SkyboxScene is { } skyboxScene)
            {
                yield return skyboxScene;
            }
        }
    }

    /// <summary>
    /// The 3D sky of the loaded map, rendered behind the main scene, or <see langword="null"/> when it has none.
    /// </summary>
    public Skybox3D? Skybox3D { get; set; }

    /// <summary>
    /// The scene the 3D sky was loaded into, or <see langword="null"/> when the map has no 3D sky.
    /// </summary>
    public Scene? SkyboxScene => Skybox3D?.Scene;

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

    internal Shader depthOnlyShader = null!;
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
    /// Single channel coverage mask written by the outline geometry pass and read by the outline edge post pass.
    /// Lazily created to match <see cref="MainFramebuffer"/>'s dimensions and sample count.
    /// </summary>
    public Framebuffer? OutlineMaskBuffer { get; private set; }

    private const ImageFormat OutlineMaskFormat = ImageFormat.I8;

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

    /// <summary>Screen space map of ripple, silt and foam decals that the fancy water shader reads.</summary>
    public Framebuffer? WaterEffectsBuffer { get; private set; }

    /// <summary>The water effects map's "nothing here" value; its channels are read signed around the midpoint.</summary>
    private static readonly Color4 WaterEffectsNeutral = new(32767f / 65535f, 32767f / 65535f, 32767f / 65535f, 0f);

    private Shader depthDownsampleShader = null!;

    /// <summary>Scene pixels per water effects texel, horizontally and vertically.</summary>
    public static (int X, int Y) WaterEffectsDownsample { get; } = (4, 2); // 6:3 in low settings

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
    /// Setting it also freezes the 3D sky's cull frustum.
    /// </summary>
    public Frustum? LockedCullFrustum
    {
        get;
        set
        {
            field = value;
            lockedSkyCullFrustum = value == null ? null : SkyCamera.ViewFrustum.Clone();
        }
    }

    private Frustum? lockedSkyCullFrustum;

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

    private readonly SceneView[] frameViews = new SceneView[2];

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
    /// Enable barn light types in shaders.
    /// </summary>
    public bool EnableBarnLights { get; set; } = true;

    /// <summary>
    /// Whether scene nodes simulate across the thread pool. Off runs them in scene order.
    /// </summary>
    public bool ParallelSimulation { get; set; } = true;

    /// <summary>
    /// Initializes a new renderer with the given context.
    /// </summary>
    /// <param name="rendererContext">Shared context providing loaders and caches.</param>
    public Renderer(RendererContext rendererContext)
    {
        RendererContext = rendererContext;
        PerfStats = new PerfStats();
        Postprocess = new(rendererContext);
        LightTilesOverlay = new(rendererContext);
        Camera = new Camera(rendererContext.FieldOfView);
        ViewmodelCamera = new Camera();
        SkyCamera = new Camera();
        Scene = new Scene(rendererContext);
        EntitySystem = new EntitySystem(rendererContext);
    }

    /// <summary>
    /// The views this frame draws, main view first, valid until the next call.
    /// Also updates <see cref="SkyCamera"/> from <paramref name="camera"/>.
    /// </summary>
    private ReadOnlySpan<SceneView> CollectViews(Camera camera)
    {
        var count = 1;

        frameViews[0] = new SceneView
        {
            Scene = Scene,
            Camera = camera,
            LockedCullFrustum = LockedCullFrustum,
        };

        if (Skybox3D is { } skybox)
        {
            skybox.ConfigureCamera(SkyCamera, camera);

            frameViews[count++] = new SceneView
            {
                Scene = skybox.Scene,
                Camera = SkyCamera,
                Skybox = skybox,
                LockedCullFrustum = lockedSkyCullFrustum,
            };
        }

        return frameViews.AsSpan(0, count);
    }

    /// <summary>The frustum a view's CPU cull runs against, or <see langword="null"/> for the camera's own.</summary>
    private Frustum? CullFrustumFor(in SceneView view) => DisableAllCulling ? noCullFrustum : view.LockedCullFrustum;

    /// <summary>
    /// The frustum a view's GPU meshlet cull runs against, or <see langword="null"/> to leave its
    /// indirect buffers untouched, freezing the cull state. Disabled culling still has to dispatch,
    /// otherwise the indirect draw commands keep the previous contents.
    /// </summary>
    private Frustum? MeshletCullFrustumFor(in SceneView view)
    {
        if (DisableAllCulling)
        {
            return noCullFrustum;
        }

        return view.LockedCullFrustum == null ? view.Camera.ViewFrustum : null;
    }

    /// <summary>
    /// The view the first person viewmodel is drawn through: the main scene at the viewmodel field of
    /// view, reusing the lights binned for the main view.
    /// </summary>
    private SceneView ViewmodelView(in SceneView main)
        => main with { Camera = ViewmodelCamera, BinnedFor = main.Camera };

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
            new Vector4(DefaultSunColor.AsVector3() * DefaultSunColor.W, 1f);
    }

    /// <summary>
    /// Allocates GPU resources required for rendering; must be called once before <see cref="Render(Scene.RenderContext)"/>.
    /// </summary>
    public void Initialize()
    {
        ViewBuffer = new UniformBuffer<ViewConstants>(ReservedBufferSlots.View);
        Skybox2D = BaseBackground = new SceneBackground(Scene);

        ShadowDepthBuffer = Framebuffer.Prepare(nameof(ShadowDepthBuffer), ShadowTextureSize, ShadowTextureSize, 0, null, ImageFormat.D16);
        ShadowDepthBuffer.DepthLayers = WorldLightingInfo.SunCascadeCount;
        ShadowDepthBuffer.Initialize();
        ShadowDepthBuffer.ClearMask = ClearBufferMask.DepthBufferBit;
        Debug.Assert(ShadowDepthBuffer.Depth != null);

        ShadowDepthBuffer.SetShadowDepthSamplerState();
        Textures.Add(new(ReservedTextureSlots.ShadowDepthBufferDepth, "g_tShadowDepthBufferDepth", ShadowDepthBuffer.Depth));

        // Barn light shadow atlas
        BarnLightShadowBuffer = Framebuffer.Prepare(nameof(BarnLightShadowBuffer), 4, 4, 0, null, ImageFormat.D16);
        BarnLightShadowBuffer.Initialize();
        BarnLightShadowBuffer.ClearMask = ClearBufferMask.DepthBufferBit;
        Debug.Assert(BarnLightShadowBuffer.Depth != null);

        BarnLightShadowBuffer.SetShadowDepthSamplerState(true);
        Textures.Add(new(ReservedTextureSlots.BarnLightShadowDepth, "g_tBarnLightShadowDepth", BarnLightShadowBuffer.Depth));

        depthOnlyShader = Scene.RendererContext.ShaderLoader.LoadShader("depth_only");
        depthDownsampleShader = Scene.RendererContext.ShaderLoader.LoadShader("depth_downsample");

        histogramShaders[0] = Scene.RendererContext.ShaderLoader.LoadShader("histogram");
        histogramShaders[1] = Scene.RendererContext.ShaderLoader.LoadShader("histogram", ("D_HISTOGRAM_MODE", 1));

        histogramBuffers[0] = StorageBuffer.Allocate<uint>(ReservedBufferSlots.BufferSlot15, "Histogram", 256, BufferUsage.GpuOnly);
        histogramBuffers[1] = StorageBuffer.Allocate<uint>(ReservedBufferSlots.BufferSlot11, "HistogramReadback", 4, BufferUsage.Readback);

        ResolvedSceneColor = RenderTexture.Create(4, 4, ImageFormat.RGBA16161616F, nameof(ResolvedSceneColor));
        ResolvedSceneColor.SetFiltering(TextureMinFilter.Linear, TextureMagFilter.Linear);
        ResolvedSceneColor.SetWrapMode(RsTextureAddressMode.Clamp);

        ResolvedSceneDepth = RenderTexture.Create(4, 4, ImageFormat.R32F, nameof(ResolvedSceneDepth));

        Textures.Add(new(ReservedTextureSlots.SceneColor, "g_tSceneColor", ResolvedSceneColor));
        Textures.Add(new(ReservedTextureSlots.SceneDepth, "g_tSceneDepth", ResolvedSceneDepth));

        // The game's own target is D24S8; nothing here needs the stencil.
        WaterEffectsBuffer = Framebuffer.Prepare(nameof(WaterEffectsBuffer), 4, 4, 0,
            ImageFormat.RGBA16161616, ImageFormat.D16);
        WaterEffectsBuffer.Initialize();
        WaterEffectsBuffer.ClearColor = WaterEffectsNeutral;

        WaterEffectsBuffer.SetColorSamplerState(TextureMinFilter.Linear, TextureMagFilter.Linear, RsTextureAddressMode.Border);
        SetupWaterEffectsTexture();

        EnsureDepthPyramidSize(256, 256);
    }

    /// <summary>Slots out of <see cref="MaterialLoader.ShaderTextures"/> that have been resolved.</summary>
    private readonly HashSet<ReservedTextureSlots> loadedShaderTextures = [];
    private RenderTexture? morphAtlasTexture;

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
    /// Loads the embedded BRDF LUT and cube fog textures, and the game's blue noise where it has one, into <see cref="Textures"/>.
    /// </summary>
    public void LoadRendererResources()
    {
        var rendererAssembly = Assembly.GetAssembly(typeof(RendererContext)) ?? throw new InvalidOperationException("Failed to get renderer assembly");
        const string vtexFileName = "brdf_lut.vtex_c";

        using var brdfStream = rendererAssembly.GetManifestResourceStream("Renderer.Resources." + vtexFileName)
            ?? throw new InvalidOperationException($"Failed to load embedded resource: {vtexFileName}");
        using var brdfLutResource = new Resource() { FileName = vtexFileName };
        brdfLutResource.Read(brdfStream);

        var brdfLutTexture = Scene.RendererContext.MaterialLoader.LoadTexture(brdfLutResource);
        brdfLutTexture.SetWrapMode(RsTextureAddressMode.Clamp);
        Textures.Add(new(ReservedTextureSlots.BRDFLookup, "g_tBRDFLookup", brdfLutTexture));

        // Load default cube fog texture.
        using var cubeFogStream = rendererAssembly.GetManifestResourceStream("Renderer.Resources.sky_furnace.vtex_c") ?? throw new InvalidOperationException("Failed to load embedded cube fog texture.");
        using var cubeFogResource = new Resource() { FileName = "default_cube.vtex_c" };
        cubeFogResource.Read(cubeFogStream);

        var defaultCubeTexture = Scene.RendererContext.MaterialLoader.LoadTexture(cubeFogResource);
        Textures.Add(new(ReservedTextureSlots.FogCubeTexture, "g_tFogCubeTexture", defaultCubeTexture));

        const string blueNoiseName = "blue_noise_256.vtex_c";
        var blueNoiseResource = RendererContext.FileLoader.LoadFile("textures/dev/" + blueNoiseName);

        // Preferred from the game, embedded otherwise
        if (blueNoiseResource?.DataBlock is not Texture)
        {
            blueNoiseResource?.Dispose();
            blueNoiseResource = null;
        }

        try
        {
            if (blueNoiseResource == null)
            {
                var blueNoiseStream = rendererAssembly.GetManifestResourceStream("Renderer.Resources." + blueNoiseName)
                    ?? throw new InvalidOperationException($"Failed to load embedded resource: {blueNoiseName}");

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

    /// <summary>
    /// Switches drawing to a view: its view constants, its scene's buffers, and the camera and scene in
    /// the render context. These must always change together.
    /// </summary>
    private void DrawThrough(in SceneView view, ref Scene.RenderContext renderContext)
    {
        BindView(view);
        view.Scene.SetSceneBuffers();

        renderContext.Camera = view.Camera;
        renderContext.Scene = view.Scene;
    }

    /// <summary>Fills the view constants for a view and uploads them.</summary>
    private void BindView(in SceneView view)
    {
        Debug.Assert(ViewBuffer != null);

        view.Camera.SetViewConstants(ViewBuffer.Data);

        // The depth pyramid is shared, but each view reprojects it with its own camera
        ViewBuffer.Data.WorldToProjectionPrev = view.Scene.DepthPyramidViewProjection;

        // The fog toggle and the weather of the main scene apply to every view
        Scene.SetFogConstants(ViewBuffer.Data, view.Fog, view.FogSpace);

        ViewBuffer.Data.IsSkybox = view.Skybox != null;

        // The shadow cascades only cover the main scene
        ViewBuffer.Data.SunShadowsEnabled = ReferenceEquals(view.Scene, Scene);

        view.Scene.LightBinner.SetPixelRemap(view.BinnedFor is { } binnedFor
            ? view.Camera.GetPixelRemapTo(binnedFor, ViewBuffer.Data.ViewportSize)
            : ViewConstants.PixelRemapIdentity);

        ViewBuffer.BindBufferBase();
        ViewBuffer.Update();
    }

    /// <summary>
    /// Updates one view's per-frame GPU state and leaves it bound: view constants, light binning and
    /// meshlet culling.
    /// </summary>
    private void UpdateViewGpuBuffers(in SceneView view)
    {
        Debug.Assert(ViewBuffer != null);

        var scene = view.Scene;

        BindView(view);

        var cullWidth = (int)ViewBuffer.Data.ViewportSize.X;
        var cullHeight = (int)ViewBuffer.Data.ViewportSize.Y;

        // The main scene's tile culling toggle applies to every view
        scene.LightBinner.Update(ViewBuffer.Data, cullWidth, cullHeight, Scene.EnableTiledLightCulling);

        if (MeshletCullFrustumFor(view) is { } meshletCullFrustum)
        {
            if (scene.DrawMeshletsIndirect)
            {
                using var _ = new GLDebugGroup("Cull Meshlet Draws");
                scene.MeshletCullGpu(meshletCullFrustum);
            }

            if (scene.CompactMeshletDraws)
            {
                using var _ = new GLDebugGroup("Compact Meshlet Draws");
                scene.CompactIndirectDraws();
            }
        }

        // Also writes the all visible mask when tile culling is off, so it runs even with the cull frozen
        using (new GLDebugGroup("Cull Tiles and Depth Bins"))
        {
            scene.LightBinner.Dispatch();
        }
    }

    /// <summary>Updates every view's per-frame GPU state, leaving the main view bound.</summary>
    private void UpdatePerViewGpuBuffers(ReadOnlySpan<SceneView> views, float deltaTime)
    {
        Debug.Assert(ViewBuffer != null);

        var mainCamera = views[0].Camera;

        // Skip occlusion culling if the camera moved too much -- we use last frame depth
        var moveDelta = ViewBuffer.Data.CameraPosition - mainCamera.Location;
        var eyeDelta = ViewBuffer.Data.CameraDirWs - mainCamera.Forward;

        if (moveDelta.LengthSquared() > 5000f || eyeDelta.LengthSquared() > 0.5f)
        {
            foreach (var view in views)
            {
                view.Scene.DepthPyramidValid = false;
            }
        }

        // Backwards, so the main view is the one left bound
        for (var i = views.Length - 1; i >= 0; i--)
        {
            UpdateViewGpuBuffers(views[i]);
        }

        if (Postprocess != null)
        {
            Postprocess.State = Scene.PostProcessInfo.CurrentState;
            Postprocess.ResolveColorCorrection(Scene.PostProcessInfo.ActiveLuts);
            Postprocess.CalculateTonemapScalar(deltaTime);
        }
    }

    /// <summary>Draws the refract, water and translucent passes of the scene in the render context.</summary>
    private static void RenderTranslucentLayer(Scene.RenderContext renderContext)
    {
        var scene = renderContext.Scene;

        scene.RenderOpaqueRefractLayer(renderContext);
        scene.RenderWaterLayer(renderContext);

        using var _ = GraphicsContext.RenderState.Scope(depthWrite: false, blend: true);

        scene.RenderTranslucentLayer(renderContext);
    }

    /// <summary>
    /// Empties the entity world and every scene, leaving the renderer ready to load something else.
    /// </summary>
    public void Clear()
    {
        // The scenes go first: emptying them leaves the entities nothing to unhook themselves from
        foreach (var scene in Scenes)
        {
            scene.Clear();
        }

        EntitySystem.Clear();

        // The 3D sky's scene came with the map, so it goes with it rather than outliving the next load
        if (Skybox3D is { } skybox)
        {
            skybox.Scene.Dispose();
            Skybox3D = null;
        }
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
        UpdatePerViewGpuBuffers(CollectViews(Camera), DeltaTime);
        Scene.SetSceneBuffers();

        Scene.RenderOpaqueLayer(renderContext);

        // No grab of its own, so resolve the depth the water effects pass occludes against.
        if (NeedsWaterEffectsMap)
        {
            GrabFramebufferCopy(renderContext.Framebuffer, false, true);
        }

        RenderWaterEffectsMap(renderContext);
        RenderTranslucentLayer(renderContext);
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

        using var frameScope = GraphicsContext.RenderState.Scope(multisampleEnable: renderContext.Framebuffer.NumSamples > 1);
        renderContext.Framebuffer.BindAndClear();

        var isMainFramebuffer = ReferenceEquals(renderContext.Framebuffer, MainFramebuffer);
        var isMaterialPass = renderContext.ReplacementShader == null && isMainFramebuffer;

        // The outline has its own program and its own mask, so a replacement shader does not stop it
        var drawsOutline = isMainFramebuffer && renderContext.OverdrawShader == null;
        var isStandardPass = isMaterialPass && drawsOutline;

        if (!isStandardPass)
        {
            PerfStats.Active.SuspendTriangleCounter();
        }

        var isWireframe = IsWireframe && isStandardPass; // To avoid toggling it mid frame
        var computeFramebufferLuminance = Postprocess.State.ExposureSettings.AutoExposureEnabled;

        // TODO: check if renderpass allows wireframe mode
        // TODO+: replace wireframe shaders with solid color
        var wireframeScope = isWireframe
            ? GraphicsContext.RenderState.Scope(fillMode: RsFillMode.Wireframe)
            : default;

        var views = CollectViews(renderContext.Camera);
        var mainView = views[0];

        UpdatePerViewGpuBuffers(views, DeltaTime);

        using (new GLDebugGroup("Viewmodel Opaque"))
        {
            ViewmodelCamera.CopyFrom(mainView.Camera);
            ViewmodelCamera.FieldOfView = ComputeViewmodelFov();
            ViewmodelCamera.CreateProjectionMatrix();
            ViewmodelCamera.RecalculateMatrices();

            GraphicsContext.RenderState.SetDepthRange(DepthRange.Viewmodel);

            DrawThrough(ViewmodelView(mainView), ref renderContext);
            Scene.RenderViewmodelOpaqueLayer(renderContext);

            GraphicsContext.RenderState.SetDepthRange(DepthRange.Scene);

            DrawThrough(mainView, ref renderContext);
        }

        using (new GLDebugGroup("Main Scene Opaque Render"))
        {
            Scene.RenderOpaqueLayer(renderContext, isMaterialPass ? depthOnlyShader : null);
        }

        //using (new GLDebugGroup("Sky Render"))
        {
            GraphicsContext.RenderState.SetDepthRange(DepthRange.Sky);

            SceneView? skyView = ShowSkybox && views.Length > 1 ? views[1] : null;
            var (copyColor, copyDepth) = (Scene.WantsSceneColor, Scene.WantsSceneDepth);
            copyDepth |= ForceResolveSceneDepth;

            if (skyView is { Scene: var skyboxScene } skyOpaque)
            {
                copyColor |= skyboxScene.WantsSceneColor;
                copyDepth |= skyboxScene.WantsSceneDepth;

                using (new GLDebugGroup("3D Sky Scene"))
                {
                    DrawThrough(skyOpaque, ref renderContext);
                    skyboxScene.RenderOpaqueLayer(renderContext);
                }

                // The 2D sky, the framebuffer grab and the water effects belong to the main view
                DrawThrough(mainView, ref renderContext);
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

                copyDepth |= generateDepthPyramid || NeedsWaterEffectsMap;

                var depthPyramidValid = !DisableAllCulling && (generateDepthPyramid || LockedCullFrustum != null);

                foreach (var view in views)
                {
                    view.Scene.DepthPyramidValid = depthPyramidValid;
                }

                GrabFramebufferCopy(renderContext.Framebuffer, copyColor, copyDepth);

                if (generateDepthPyramid)
                {
                    Debug.Assert(ResolvedSceneColor != null && ResolvedSceneDepth != null);
                    EnsureDepthPyramidSize(renderContext.Framebuffer.Width, renderContext.Framebuffer.Height);
                    Scene.GenerateDepthPyramid(ResolvedSceneDepth);

                    // All views were drawn into the same depth buffer, so they share the pyramid
                    foreach (var view in views)
                    {
                        view.Scene.DepthPyramid = Scene.DepthPyramid;
                        view.Scene.DepthPyramidViewProjection = view.Camera.ViewProjectionMatrix;
                        view.Scene.DepthPyramidValid = true;
                    }
                }

                // After the grab, so it occludes against the depth resolved there rather than resolving
                // its own. The particles are scene geometry, not sky.
                GraphicsContext.RenderState.SetDepthRange(DepthRange.Scene);
                RenderWaterEffectsMap(renderContext);
                GraphicsContext.RenderState.SetDepthRange(DepthRange.Sky);
            }

            if (skyView is { } skyTranslucent)
            {
                using (new GLDebugGroup("3D Sky Scene Translucent Render"))
                {
                    DrawThrough(skyTranslucent, ref renderContext);
                    RenderTranslucentLayer(renderContext);
                }

                DrawThrough(mainView, ref renderContext);
            }

            GraphicsContext.RenderState.SetDepthRange(DepthRange.Scene);
        }

        using (new GLDebugGroup("Main Scene Translucent Render"))
        {
            RenderTranslucentLayer(renderContext);
        }

        using (new GLDebugGroup("Viewmodel Translucent"))
        {
            GraphicsContext.RenderState.SetDepthRange(DepthRange.Viewmodel);

            DrawThrough(ViewmodelView(mainView), ref renderContext);
            Scene.RenderViewmodelTranslucentLayer(renderContext);

            GraphicsContext.RenderState.SetDepthRange(DepthRange.Scene);

            DrawThrough(mainView, ref renderContext);
        }

        wireframeScope.Dispose();

        if (isStandardPass && computeFramebufferLuminance)
        {
            ComputeAverageLuminance(renderContext);
        }

        if (drawsOutline)
        {
            Postprocess.HasOutlineObjects = Scene.HasOutlineObjects || SkyboxScene?.HasOutlineObjects == true;

            if (Postprocess.HasOutlineObjects)
            {
                RenderOutlineLayer(renderContext, views);
            }
        }

        if (isStandardPass)
        {
            var overlayBatch = ValveResourceFormat.Renderer.LightTilesOverlay.BatchFor(ViewBuffer!.Data.RenderMode);

            if (overlayBatch != ValveResourceFormat.Renderer.LightTilesOverlay.Batch.None)
            {
                var (tileBase, words) = Scene.LightBinner.GetOverlayRegion(
                    overlayBatch == ValveResourceFormat.Renderer.LightTilesOverlay.Batch.EnvMaps);

                LightTilesOverlay.Render(Scene.LightBinner.CullBits, tileBase, words);
            }

            foreach (var view in views)
            {
                view.Scene.LightBinner.SubmitVisibilityReadback();
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

        using var _ = GraphicsContext.RenderState.Scope(multisampleEnable: ShadowDepthBuffer.NumSamples > 1,
            cullMode: RsCullMode.None, slopeScaledDepthBias: -2f);

        using var shadowDepth = GraphicsContext.RenderState.ScopeDynamic(DepthRange.Full);

        GL.Viewport(0, 0, ShadowDepthBuffer.Width, ShadowDepthBuffer.Height);
        ShadowDepthBuffer.Bind(FramebufferTarget.Framebuffer);

        renderContext.Framebuffer = ShadowDepthBuffer;
        renderContext.Scene = Scene;

        ViewBuffer.Data.WorldToShadow = Scene.LightingInfo.SunViewProjections[0];
        ViewBuffer.Data.WorldToShadowCascade1 = Scene.LightingInfo.SunViewProjections[1];
        ViewBuffer.Data.SunLightShadowBias = Scene.LightingInfo.SunLightShadowBias;

        using (new GLDebugGroup("Direct Light Shadows"))
        {
            for (var cascade = 0; cascade < WorldLightingInfo.SunCascadeCount; cascade++)
            {
                ShadowDepthBuffer.AttachDepthLayer(cascade);
                GL.Clear(ClearBufferMask.DepthBufferBit);

                if (cascade >= Scene.LightingInfo.ActiveSunCascadeCount)
                {
                    continue;
                }

                ViewBuffer.Data.WorldToProjection = Scene.LightingInfo.SunViewProjections[cascade];
                ViewBuffer.Update();

                PerfStats.Active.Count(Counter.DirectionalShadowMap);
                Scene.RenderOpaqueShadows(renderContext, depthOnlyShader, Scene.CulledShadowDrawCallsCascades[cascade]);
            }
        }
    }

    private void RenderBarnLightShadows(Scene.RenderContext renderContext)
    {
        Debug.Assert(ViewBuffer != null);

        if (Scene.LightingInfo.ShadowMapper.ShadowCasters.Count == 0)
        {
            return;
        }

        using var _ = new GLDebugGroup("Barn Light Shadows");
        Debug.Assert(BarnLightShadowBuffer != null);

        // The barn shadow atlas uses forward depth, unlike the reverse-Z main view.
        using var forwardDepth = GraphicsContext.RenderState.Scope(depthFunc: RsComparison.FartherEqual,
            slopeScaledDepthBias: 2f, multisampleEnable: BarnLightShadowBuffer.NumSamples > 1);

        using var atlasDepth = GraphicsContext.RenderState.ScopeDynamic(DepthRange.Full, clearDepth: 1f, scissorTest: true);

        BarnLightShadowBuffer.Bind(FramebufferTarget.Framebuffer);

        var atlasSize = Scene.LightingInfo.BarnLightShadowAtlasSize;

        if (BarnLightShadowBuffer.Resize(atlasSize, atlasSize))
        {
            Textures.RemoveAll(t => t.Slot == ReservedTextureSlots.BarnLightShadowDepth);
            Textures.Add(new(ReservedTextureSlots.BarnLightShadowDepth, "g_tBarnLightShadowDepth", BarnLightShadowBuffer.Depth!));
        }

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
            var drawCalls = Scene.SetupBarnLightFaceShadow(caster.Light, barnLightShadowFrustum);

            Scene.RenderOpaqueShadows(renderContext, depthOnlyShader, drawCalls);
        }
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
        var groupsX = Math.Max(1, MathUtils.DivideRoundUp(width, 16));
        var groupsY = Math.Max(1, MathUtils.DivideRoundUp(height, 16));
        Dispatch(histogramShaders[0], inputTex, groupsX, groupsY);
        GL.MemoryBarrier(MemoryBarrierFlags.ShaderStorageBarrierBit);

        // Reduce histogram
        Dispatch(histogramShaders[1], inputTex, 1, 1); // local_size_x = 256

        GL.MemoryBarrier(MemoryBarrierFlags.ShaderStorageBarrierBit | MemoryBarrierFlags.BufferUpdateBarrierBit);

        var output = Vector4.Zero;
        histogramBuffers[1].Read(ref output);
        Postprocess.AverageLuminance = output.X;
    }

    private void RenderOutlineLayer(Scene.RenderContext renderContext, ReadOnlySpan<SceneView> views)
    {
        using var _ = new GLDebugGroup("Outline Mask Write");

        var sceneFramebuffer = renderContext.Framebuffer;
        var maskBuffer = GetOutlineMaskBuffer(sceneFramebuffer);

        Postprocess.OutlineMask = maskBuffer.Color;

        // Custom scene nodes may leave state changed, and the outline layer is drawn mid frame.
        using var maskState = GraphicsContext.RenderState.Scope(cullMode: RsCullMode.None,
            multisampleEnable: maskBuffer.NumSamples > 1, depthTest: false, depthWrite: false, blend: false);

        GL.Viewport(0, 0, maskBuffer.Width, maskBuffer.Height);
        maskBuffer.BindAndClear();

        // Backwards, so the sky's outlines are drawn first and the main view is the one left bound
        for (var i = views.Length - 1; i >= 0; i--)
        {
            DrawThrough(views[i], ref renderContext);
            renderContext.Scene.RenderOutlineLayer(renderContext);
        }

        sceneFramebuffer.Bind(FramebufferTarget.Framebuffer);
        GL.Viewport(0, 0, sceneFramebuffer.Width, sceneFramebuffer.Height);
    }

    /// <summary>
    /// Returns the outline mask framebuffer, creating or resizing it to match the scene framebuffer.
    /// </summary>
    private Framebuffer GetOutlineMaskBuffer(Framebuffer sceneFramebuffer)
    {
        var (width, height, msaa) = (sceneFramebuffer.Width, sceneFramebuffer.Height, sceneFramebuffer.NumSamples);

        // The edge detection pass reads the mask per sample, so the mask has to be multisampled the same way.
        Debug.Assert(msaa > 0);

        if (OutlineMaskBuffer == null)
        {
            OutlineMaskBuffer = Framebuffer.Prepare(nameof(OutlineMaskBuffer), width, height, msaa, OutlineMaskFormat, null);
            OutlineMaskBuffer.ClearMask = ClearBufferMask.ColorBufferBit;
            OutlineMaskBuffer.Initialize();
        }
        else
        {
            OutlineMaskBuffer.Resize(width, height, msaa);
        }

        return OutlineMaskBuffer;
    }

    // Run after every scene update
    private void UpdateMorphAtlas(ReadOnlySpan<SceneView> views)
    {
        var atlas = RendererContext.MorphAtlas;
        atlas.Render();

        foreach (var view in views)
        {
            view.Scene.UpdateMorphAtlasRects();
        }

        // Growing the atlas replaces its texture
        if (atlas.Texture is { } texture && texture != morphAtlasTexture)
        {
            morphAtlasTexture = texture;
            Textures.RemoveAll(static t => t.Slot == ReservedTextureSlots.MorphCompositeTexture);
            Textures.Add(new(ReservedTextureSlots.MorphCompositeTexture, "g_tCompositeMorphTextureAtlas", texture));
        }
    }

    /// <summary>Points the reserved <c>g_tWaterEffectsMap</c> slot at the current color attachment.</summary>
    private void SetupWaterEffectsTexture()
    {
        Debug.Assert(WaterEffectsBuffer?.Color != null);

        // Reprojections land off-screen at grazing angles; a neutral border reads as "no effect there".
        GL.TextureParameter(WaterEffectsBuffer.Color.Handle, TextureParameterName.TextureBorderColor,
            [WaterEffectsNeutral.R, WaterEffectsNeutral.G, WaterEffectsNeutral.B, WaterEffectsNeutral.A]);

        Textures.RemoveAll(static t => t.Slot == ReservedTextureSlots.WaterEffectsMap);
        Textures.Add(new(ReservedTextureSlots.WaterEffectsMap, "g_tWaterEffectsMap", WaterEffectsBuffer.Color));
    }

    /// <summary>Whether anything draws into the water effects map this frame, and anything reads it.</summary>
    private bool NeedsWaterEffectsMap => Scene.HasWater && Scene.HasWaterEffects;

    /// <summary>Fills <see cref="WaterEffectsBuffer"/>; must run before any water layer this frame.</summary>
    private void RenderWaterEffectsMap(Scene.RenderContext renderContext)
    {
        Debug.Assert(WaterEffectsBuffer != null && ViewBuffer != null);

        var hasDraws = NeedsWaterEffectsMap;

        if (!hasDraws && waterEffectsMapIsNeutral)
        {
            return;
        }

        using var _ = new GLDebugGroup("Fancy Water Effects");

        var (downsampleX, downsampleY) = WaterEffectsDownsample;
        var width = Math.Max(1, MathUtils.DivideRoundUp(renderContext.Framebuffer.Width, downsampleX));
        var height = Math.Max(1, MathUtils.DivideRoundUp(renderContext.Framebuffer.Height, downsampleY));

        if (WaterEffectsBuffer.Resize(width, height))
        {
            SetupWaterEffectsTexture();
        }

        var sceneFramebuffer = renderContext.Framebuffer;

        GL.Viewport(0, 0, width, height);
        WaterEffectsBuffer.BindAndClear();

        renderContext.Framebuffer = WaterEffectsBuffer;

        // Data rather than an image: fog would write its color into the ripple and foam channels.
        Scene.FogInfo.SetFogUniforms(ViewBuffer.Data, viewerFogEnabled: false, FogSpace.World);
        ViewBuffer.Update();

        if (hasDraws)
        {
            Debug.Assert(ResolvedSceneDepth != null);

            using (GraphicsContext.RenderState.Scope(depthTest: true, depthWrite: true,
                depthFunc: RsComparison.Always, blend: false, colorWriteMask: RsColorWriteEnableBits.None))
            {
                // Furthest depth per block, so an occluder never reaches past its own silhouette.
                depthDownsampleShader.Use();
                depthDownsampleShader.SetTexture(0, "g_tSceneDepth", ResolvedSceneDepth);
                depthDownsampleShader.SetUniform("g_vDownsampleFactor", new Vector2(downsampleX, downsampleY));
                GL.BindVertexArray(RendererContext.MeshBufferCache.EmptyVAO);
                GL.DrawArrays(PrimitiveType.Triangles, 0, 3);
            }

            using (GraphicsContext.RenderState.Scope(depthTest: true, depthWrite: false, depthFunc: RsComparison.CloserEqual))
            {
                renderContext.Scene = Scene;
                Scene.RenderWaterEffectsLayer(renderContext);
            }
        }

        Scene.SetFogConstants(ViewBuffer.Data);
        ViewBuffer.Update();

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
            ResolvedSceneColor = RenderTexture.Create(width, height, ImageFormat.RGBA16161616F, nameof(ResolvedSceneColor));
            ResolvedSceneColor.SetFiltering(TextureMinFilter.Linear, TextureMagFilter.Linear);
            ResolvedSceneColor.SetWrapMode(RsTextureAddressMode.Clamp);

            ResolvedSceneDepth!.Delete();
            ResolvedSceneDepth = RenderTexture.Create(width, height, ImageFormat.R32F, nameof(ResolvedSceneDepth));

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

        foreach (var scene in Scenes)
        {
            scene.Dispose();
        }

        PerfStats?.Dispose();
        ResolvedSceneColor?.Delete();
        ResolvedSceneDepth?.Delete();
        OutlineMaskBuffer?.Delete();
        ShadowDepthBuffer?.Delete();
        BarnLightShadowBuffer?.Delete();
        histogramBuffers[0]?.Delete();
        histogramBuffers[1]?.Delete();
        WaterEffectsBuffer?.Delete();
        Skybox2D?.Delete();

        if (BaseBackground != Skybox2D && BaseBackground != null)
        {
            BaseBackground.Delete();
        }
    }

    /// <summary>Set while the shader specialization prewarm frame is being drawn.</summary>
    public bool Prewarming { get; set; }

    /// <summary>
    /// Advances the simulation, updates scene draw calls, and prepares shadow data for the next frame.
    /// </summary>
    public void Update(Scene.UpdateContext updateContext)
    {
        if (ViewBuffer is null || ShadowDepthBuffer is null)
        {
            throw new InvalidOperationException("Initialize() must be called before updating");
        }

        RendererContext.ParallelSimulation = ParallelSimulation;

        Uptime += updateContext.Timestep;
        DeltaTime = updateContext.Timestep;
        ViewBuffer.Data.Time = Uptime;

        updateContext = updateContext with { Uptime = Uptime };

        Camera.RecalculateMatrices();

        // Entities simulate on their own fixed tick, then the scene nodes of every view pick the result up
        EntitySystem.Update(updateContext.Timestep);

        var views = CollectViews(updateContext.Camera);

        foreach (var view in views)
        {
            view.Scene.Update(updateContext with { Camera = view.Camera });
        }

        UpdateMorphAtlas(views);

        Scene.PostProcessInfo.UpdatePostProcessing(updateContext.Camera, updateContext.Timestep);

        Scene.SetupSceneShadows(updateContext.Camera, DisableAllCulling ? -1 : ShadowDepthBuffer.Width);

        if (EnableBarnLights)
        {
            Scene.LightingInfo.BinBarnLights(Camera, ShadowTextureSize);
        }
        else
        {
            Scene.LightingInfo.ClearBarnLights();
        }

        if (!DisableAllCulling && Scene is { EnablePvsCulling: true, VoxelVisibility: not null })
        {
            var pvsPosition = LockedCullPosition ?? updateContext.Camera.Location;
            Scene.CurrentFramePvs = Scene.VoxelVisibility.GetVisibilityRowForPoint(pvsPosition);
        }
        else
        {
            Scene.CurrentFramePvs = default;
        }

        foreach (var view in views)
        {
            view.Scene.UpdateIndirectRenderingState();
            view.Scene.CollectSceneDrawCalls(view.Camera, CullFrustumFor(view));
        }

        if (ShowSoundDebug && Sound.Player != null)
        {
            CollectSoundDebugText(updateContext);
        }

        if (!Prewarming)
        {
            if (RendererContext.TextureStreaming.Mode == TextureStreamingMode.Immediate)
            {
                RendererContext.TextureStreaming.FinishAllStreaming(RendererContext.CancellationToken);
            }
            else
            {
                RendererContext.TextureStreaming.Timeslice(DeltaTime);
            }
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

    /// <summary>Largest width of the depth pyramid; height follows the viewport's aspect.</summary>
    private const int DepthPyramidMaxDimension = 512;

    void EnsureDepthPyramidSize(int width, int height)
    {
        var scale = Math.Min(1f, DepthPyramidMaxDimension / (float)Math.Max(width, height));

        static int NearestPowerOfTwo(float value)
            => 1 << Math.Max(0, (int)MathF.Round(MathF.Log2(MathF.Max(value, 1f))));

        var targetWidth = NearestPowerOfTwo(width * scale);
        var targetHeight = NearestPowerOfTwo(height * scale);

        if (Scene.DepthPyramid != null && Scene.DepthPyramid.Width == targetWidth && Scene.DepthPyramid.Height == targetHeight)
        {
            return;
        }

        Scene.DepthPyramid?.Delete();

        // Mips needed to take the larger axis down to 1
        var maxMipLevel = (int)Math.Log2(Math.Max(targetWidth, targetHeight));

        Scene.DepthPyramid = RenderTexture.Create(targetWidth, targetHeight, ImageFormat.R32F, maxMipLevel + 1, "DepthPyramid");
        Scene.DepthPyramid.SetBaseMaxLevel(0, maxMipLevel);
    }
}
