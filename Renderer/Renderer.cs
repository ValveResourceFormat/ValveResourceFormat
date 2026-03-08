using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Threading;
using OpenTK.Graphics.OpenGL;
using ValveResourceFormat.Renderer.Buffers;

namespace ValveResourceFormat.Renderer;

/// <summary>
/// Main renderer for Source 2 scenes with support for shadows, post-processing, and multiple render passes.
/// </summary>
public class Renderer
{
    public float Uptime { get; set; }
    public float DeltaTime { get; set; }
    public RendererContext RendererContext { get; }
    public Camera Camera { get; set; }

    public Timings Timings { get; } = new();

    public Scene Scene { get; set; }
    public Scene? SkyboxScene { get; set; }
    public SceneSkybox2D? Skybox2D { get; set; }
    public SceneBackground? BaseBackground { get; protected set; }

    public UniformBuffer<ViewConstants>? ViewBuffer { get; set; }
    public List<(ReservedTextureSlots Slot, string Name, RenderTexture Texture)> Textures { get; } = [];

    internal readonly Shader[] depthOnlyShaders = new Shader[Enum.GetValues<DepthOnlyProgram>().Length];
    private readonly Frustum barnLightShadowFrustum = new();
    public Framebuffer? ShadowDepthBuffer { get; private set; }
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

    private readonly Shader[] histogramShaders = new Shader[2];
    private readonly StorageBuffer[] histogramBuffers = new StorageBuffer[2];

    // Injected
    public Framebuffer? MainFramebuffer { get; set; }
    public PostProcessRenderer Postprocess { get; set; }
    public Frustum? LockedCullFrustum { get; set; }

    // options
    public int ShadowTextureSize { get; set; } = 1024;
    public bool IsWireframe { get; set; }
    public bool ShowSkybox { get; set; } = true;

    public Renderer(RendererContext rendererContext)
    {
        RendererContext = rendererContext;
        Postprocess = new(rendererContext);
        Camera = new Camera(rendererContext);
        Scene = new Scene(rendererContext);
    }

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

        depthOnlyShaders[(int)DepthOnlyProgram.Static] = Scene.RendererContext.ShaderLoader.LoadShader("vrf.depth_only");
        //depthOnlyShaders[(int)DepthOnlyProgram.StaticAlphaTest] = GuiContext.ShaderLoader.LoadShader("vrf.depth_only", ("F_ALPHA_TEST", 1));
        depthOnlyShaders[(int)DepthOnlyProgram.Animated] = Scene.RendererContext.ShaderLoader.LoadShader("vrf.depth_only", ("D_ANIMATED", 1));
        depthOnlyShaders[(int)DepthOnlyProgram.AnimatedEightBones] = Scene.RendererContext.ShaderLoader.LoadShader("vrf.depth_only", ("D_ANIMATED", 1), ("D_EIGHT_BONE_BLENDING", 1));

        depthOnlyShaders[(int)DepthOnlyProgram.OcclusionQueryAABBProxy] = Scene.RendererContext.ShaderLoader.LoadShader("vrf.depth_only_aabb");

        histogramShaders[0] = Scene.RendererContext.ShaderLoader.LoadShader("vrf.histogram");
        histogramShaders[1] = Scene.RendererContext.ShaderLoader.LoadShader("vrf.histogram", ("D_HISTOGRAM_MODE", 1));

        histogramBuffers[0] = StorageBuffer.Allocate<uint>(ReservedBufferSlots.Histogram, 256, BufferUsageHint.DynamicDraw);
        histogramBuffers[1] = StorageBuffer.Allocate<uint>(ReservedBufferSlots.AverageLuminance, 4, BufferUsageHint.DynamicRead);

        ResolvedSceneColor = RenderTexture.Create(4, 4, SizedInternalFormat.Rgba16f);
        ResolvedSceneColor.SetFiltering(TextureMinFilter.Linear, TextureMagFilter.Linear);
        ResolvedSceneColor.SetWrapMode(TextureWrapMode.ClampToEdge);

        ResolvedSceneDepth = RenderTexture.Create(4, 4, SizedInternalFormat.R32f);

        Textures.Add(new(ReservedTextureSlots.SceneColor, "g_tSceneColor", ResolvedSceneColor));
        Textures.Add(new(ReservedTextureSlots.SceneDepth, "g_tSceneDepth", ResolvedSceneDepth));
        // Textures.Add(new(ReservedTextureSlots.SceneStencil, "g_tSceneStencil", FramebufferCopy.Stencil));

        EnsureDepthPyramidSize(256, 256);
    }

    public void LoadRendererResources()
    {
        var rendererAssembly = Assembly.GetAssembly(typeof(RendererContext)) ?? throw new InvalidOperationException("Failed to get renderer assembly");
        const string vtexFileName = "ggx_integrate_brdf_lut_schlick.vtex_c";

        // Load brdf lut, preferably from game.
        var brdfLutResource = RendererContext.FileLoader.LoadFile("textures/dev/" + vtexFileName);

        try
        {
            Stream? brdfStream; // Will be used by LoadTexture, and disposed by resource

            if (brdfLutResource == null)
            {
                brdfStream = rendererAssembly.GetManifestResourceStream("Renderer.Resources." + vtexFileName);

                if (brdfStream == null)
                {
                    throw new InvalidOperationException($"Failed to load embedded resource: {vtexFileName}");
                }

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

        ViewBuffer.BindBufferBase();
        ViewBuffer.Update();

        if (LockedCullFrustum == null)
        {
            if (scene.DrawMeshletsIndirect)
            {
                scene.MeshletCullGpu(camera.ViewFrustum);
            }

            if (scene.CompactMeshletDraws)
            {
                scene.CompactIndirectDraws();
            }
        }

        if (Postprocess != null)
        {
            Postprocess.State = scene.PostProcessInfo.CurrentState;
            Postprocess.CalculateTonemapScalar(deltaTime);
        }
    }

    private static void RenderTranslucentLayer(Scene scene, Scene.RenderContext renderContext)
    {
        scene.RenderWaterLayer(renderContext);

        GL.DepthMask(false);
        GL.Enable(EnableCap.Blend);

        scene.RenderTranslucentLayer(renderContext);

        GL.Disable(EnableCap.Blend);
        GL.DepthMask(true);
    }

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

        UpdatePerViewGpuBuffers(Scene, Camera, DeltaTime);
        Scene.SetSceneBuffers();

        Scene.RenderOpaqueLayer(renderContext);
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


    public void Render(Scene.RenderContext renderContext)
    {
        RenderSceneShadows(renderContext);
        RenderBarnLightShadows(renderContext);
        RenderScenesWithView(renderContext);
    }

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

        var isWireframe = IsWireframe && isStandardPass; // To avoid toggling it mid frame
        var computeFramebufferLuminance = Postprocess.State.ExposureSettings.AutoExposureEnabled;


        // TODO: check if renderpass allows wireframe mode
        // TODO+: replace wireframe shaders with solid color
        if (isWireframe)
        {
            GL.PolygonMode(TriangleFace.FrontAndBack, PolygonMode.Line);
        }

        GL.DepthRange(0.05, 1);

        UpdatePerViewGpuBuffers(Scene, renderContext.Camera, DeltaTime);
        Scene.SetSceneBuffers();

        using (new GLDebugGroup("Main Scene Opaque Render"))
        {
            renderContext.Scene = Scene;
            Scene.RenderOpaqueLayer(renderContext, isStandardPass ? depthOnlyShaders : Span<Shader>.Empty);
        }

        if (isStandardPass && Scene.EnableOcclusionQueries)
        {
            Scene.RenderOcclusionProxies(renderContext, depthOnlyShaders[(int)DepthOnlyProgram.OcclusionQueryAABBProxy]);
        }

        //using (new GLDebugGroup("Sky Render"))
        {
            GL.DepthRange(0, 0.05);

            renderContext.ReplacementShader?.SetUniform1("isSkybox", 1u);
            var skyboxScene = SkyboxScene;
            var render3DSkybox = ShowSkybox && skyboxScene != null;
            var (copyColor, copyDepth) = (Scene.WantsSceneColor, Scene.WantsSceneDepth);
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
                    && LockedCullFrustum == null;

                copyDepth |= generateDepthPyramid;
                Scene.DepthPyramidValid = generateDepthPyramid || LockedCullFrustum != null;

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
            GL.DepthRange(0.05, 1);
        }

        using (new GLDebugGroup("Main Scene Translucent Render"))
        {
            RenderTranslucentLayer(Scene, renderContext);
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
        }
    }

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

        if (Scene.LightingInfo.BinnedShadowCasters.Count == 0)
        {
            return;
        }

        using var _ = new GLDebugGroup("Barn Light Shadows");
        Debug.Assert(BarnLightShadowBuffer != null);

        GL.DepthFunc(DepthFunction.Lequal);
        GL.DepthRange(0.0, 1.0);
        GL.ClearDepth(1.0);
        GL.FrontFace(FrontFaceDirection.Cw);

        GL.Enable(EnableCap.PolygonOffsetFill);
        GL.PolygonOffset(2f, 0f);

        BarnLightShadowBuffer.Bind(FramebufferTarget.Framebuffer);

        var atlasSize = ShadowTextureSize;
        Scene.LightingInfo.BarnLightShadowAtlasSize = atlasSize;

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

        foreach (var caster in Scene.LightingInfo.BinnedShadowCasters)
        {
            var region = caster.Region;

            if (region.Width == 0)
            {
                continue;
            }

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

        GL.FrontFace(FrontFaceDirection.Ccw);
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
            var minLuminance = 0.005f / 256.0f;
            var maxLuminance = 8f; //65_204f;
            var logMin = MathF.Log2(minLuminance);
            var logRange = MathF.Log2(maxLuminance) - logMin;

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

    private void EnsureResolvedTextureSize(int width, int height)
    {
        if (ResolvedSceneColor!.Width != width ||
            ResolvedSceneColor.Height != height)
        {
            // TODO: Textures list holds stale references after recreating these textures
            ResolvedSceneColor.Delete();
            ResolvedSceneColor = RenderTexture.Create(width, height, SizedInternalFormat.Rgba16f);
            ResolvedSceneColor.SetFiltering(TextureMinFilter.Linear, TextureMagFilter.Linear);
            ResolvedSceneColor.SetWrapMode(TextureWrapMode.ClampToEdge);

            ResolvedSceneDepth!.Delete();
            ResolvedSceneDepth = RenderTexture.Create(width, height, SizedInternalFormat.R32f);
        }
    }

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

    public void Dispose()
    {
        ViewBuffer?.Dispose();
        Scene?.Dispose();
        SkyboxScene?.Dispose();
        Timings?.Dispose();
        ResolvedSceneColor?.Delete();
        ResolvedSceneDepth?.Delete();
    }

    public void Update(Scene.UpdateContext updateContext)
    {
        if (ViewBuffer is null || ShadowDepthBuffer is null)
        {
            throw new InvalidOperationException("Initialize() must be called before updating");
        }

        Uptime += updateContext.Timestep;
        DeltaTime = updateContext.Timestep;
        ViewBuffer.Data.Time = Uptime;

        Camera.RecalculateMatrices();

        Scene.Update(updateContext);
        SkyboxScene?.Update(updateContext);

        Scene.PostProcessInfo.UpdatePostProcessing(updateContext.Camera);

        Scene.SetupSceneShadows(updateContext.Camera, ShadowDepthBuffer.Width);

        if (ViewBuffer.Data.ExperimentalLightsEnabled)
        {
            Scene.LightingInfo.BinBarnLights(Camera.ViewFrustum, Camera.Location);
        }

        if (LockedCullFrustum == null)
        {
            Scene.GetOcclusionTestResults();
        }

        Scene.CollectSceneDrawCalls(updateContext.Camera, LockedCullFrustum);
        SkyboxScene?.CollectSceneDrawCalls(updateContext.Camera, LockedCullFrustum);
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

        // Delete old texture
        Scene.DepthPyramid?.Delete();

        // Calculate mips needed to go from targetSize down to 1x1
        var maxMipLevel = (int)Math.Log2(targetSize);

        Scene.DepthPyramid = RenderTexture.Create(targetSize, targetSize, SizedInternalFormat.R32f, maxMipLevel + 1);
        Scene.DepthPyramid.SetLabel("DepthPyramid");

        Scene.DepthPyramid.SetBaseMaxLevel(0, maxMipLevel);
    }
}
