using System.Diagnostics;
using System.IO;
using System.Reflection;
using OpenTK.Graphics.OpenGL;
using ValveResourceFormat.Renderer.Buffers;

namespace ValveResourceFormat.Renderer;

public class Renderer
{
    public float Uptime { get; set; }
    public RendererContext RendererContext { get; }
    public Camera Camera { get; set; }

    public Scene Scene { get; set; }
    public Scene? SkyboxScene { get; set; }
    public SceneSkybox2D? Skybox2D { get; set; }
    public SceneBackground BaseBackground { get; protected set; }

    public UniformBuffer<ViewConstants>? ViewBuffer { get; set; }
    public List<(ReservedTextureSlots Slot, string Name, RenderTexture Texture)> Textures { get; } = [];

    private readonly Shader[] depthOnlyShaders = new Shader[Enum.GetValues<DepthOnlyProgram>().Length];
    public Framebuffer ShadowDepthBuffer { get; private set; }
    public Framebuffer FramebufferCopy { get; private set; }

    // Injected
    public Framebuffer MainFramebuffer { get; set; }
    public PostProcessRenderer Postprocess { get; set; }
    public Frustum? LockedCullFrustum { get; set; }


    // options
    public int ShadowTextureSize { get; set; } = 1024;
    public bool IsWireframe { get; set; }
    public bool ShowSkybox { get; set; } = true;

#nullable disable

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
        Textures.Add(new(ReservedTextureSlots.ShadowDepthBufferDepth, "g_tShadowDepthBufferDepth", ShadowDepthBuffer.Depth));

        GL.TextureParameter(ShadowDepthBuffer.Depth!.Handle, TextureParameterName.TextureCompareMode, (int)TextureCompareMode.CompareRToTexture);
        ShadowDepthBuffer.Depth.SetParameter(TextureParameterName.TextureCompareMode, (int)TextureCompareMode.CompareRToTexture);
        ShadowDepthBuffer.Depth.SetFiltering(TextureMinFilter.Linear, TextureMagFilter.Linear);
        ShadowDepthBuffer.Depth.SetWrapMode(TextureWrapMode.ClampToBorder);

        depthOnlyShaders[(int)DepthOnlyProgram.Static] = Scene.RendererContext.ShaderLoader.LoadShader("vrf.depth_only");
        //depthOnlyShaders[(int)DepthOnlyProgram.StaticAlphaTest] = GuiContext.ShaderLoader.LoadShader("vrf.depth_only", new Dictionary<string, byte> { { "F_ALPHA_TEST", 1 } });
        depthOnlyShaders[(int)DepthOnlyProgram.Animated] = Scene.RendererContext.ShaderLoader.LoadShader("vrf.depth_only", new Dictionary<string, byte> { { "D_ANIMATED", 1 } });
        depthOnlyShaders[(int)DepthOnlyProgram.AnimatedEightBones] = Scene.RendererContext.ShaderLoader.LoadShader("vrf.depth_only", new Dictionary<string, byte> { { "D_ANIMATED", 1 }, { "D_EIGHT_BONE_BLENDING", 1 } });

        depthOnlyShaders[(int)DepthOnlyProgram.OcclusionQueryAABBProxy] = Scene.RendererContext.ShaderLoader.LoadShader("vrf.depth_only_aabb");

        FramebufferCopy = Framebuffer.Prepare(nameof(FramebufferCopy), 4, 4, 0,
            new Framebuffer.AttachmentFormat(PixelInternalFormat.R11fG11fB10f, PixelFormat.Rgb, PixelType.HalfFloat),
            new Framebuffer.DepthAttachmentFormat(PixelInternalFormat.DepthComponent32f, PixelType.Float)
        );

        FramebufferCopy.Initialize();
        FramebufferCopy.ClearColor = new(0, 0, 0, 255);
        Debug.Assert(FramebufferCopy.Color != null && FramebufferCopy.Depth != null);

        Textures.Add(new(ReservedTextureSlots.SceneColor, "g_tSceneColor", FramebufferCopy.Color));
        Textures.Add(new(ReservedTextureSlots.SceneDepth, "g_tSceneDepth", FramebufferCopy.Depth));
        // Textures.Add(new(ReservedTextureSlots.SceneStencil, "g_tSceneStencil", FramebufferCopy.Stencil));
    }

    public void LoadRendererResources()
    {
        var rendererAssembly = Assembly.GetAssembly(typeof(RendererContext));

        const string vtexFileName = "ggx_integrate_brdf_lut_schlick.vtex_c";

        // Load brdf lut, preferably from game.
        var brdfLutResource = RendererContext.FileLoader.LoadFile("textures/dev/" + vtexFileName);

        try
        {
            Stream brdfStream; // Will be used by LoadTexture, and disposed by resource

            if (brdfLutResource == null)
            {
                brdfStream = rendererAssembly.GetManifestResourceStream("Renderer.Resources." + vtexFileName);

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
        using var cubeFogStream = rendererAssembly.GetManifestResourceStream("Renderer.Resources.sky_furnace.vtex_c");
        using var cubeFogResource = new Resource() { FileName = "default_cube.vtex_c" };
        cubeFogResource.Read(cubeFogStream);

        var defaultCubeTexture = Scene.RendererContext.MaterialLoader.LoadTexture(cubeFogResource);
        Textures.Add(new(ReservedTextureSlots.FogCubeTexture, "g_tFogCubeTexture", defaultCubeTexture));


        const string blueNoiseName = "blue_noise_256.vtex_c";
        var blueNoiseResource = RendererContext.FileLoader.LoadFile("textures/dev/" + blueNoiseName);

        if (Postprocess != null)
        {
            try
            {
                Stream blueNoiseStream; // Same method as brdf

                if (blueNoiseResource == null)
                {
                    blueNoiseStream = rendererAssembly.GetManifestResourceStream("Renderer.Resources." + blueNoiseName);

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
    }

    void UpdatePerViewGpuBuffers(Scene scene, Camera camera)
    {
        Debug.Assert(ViewBuffer != null);

        camera.SetViewConstants(ViewBuffer.Data);
        scene.SetFogConstants(ViewBuffer.Data);

        ViewBuffer.BindBufferBase();
        ViewBuffer.Update();

        if (Postprocess != null)
        {
            Postprocess.State = scene.PostProcessInfo.CurrentState;
            Postprocess.TonemapScalar = scene.PostProcessInfo.CalculateTonemapScalar();
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
        var renderContext = new Scene.RenderContext
        {
            Camera = Camera,
            Framebuffer = MainFramebuffer,
            Scene = Scene,
            Textures = Textures,
        };

        UpdatePerViewGpuBuffers(Scene, Camera);
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

        RenderSceneShadows(renderContext);
        RenderScenesWithView(renderContext);
    }


    public void Render(Scene.RenderContext renderContext)
    {
        RenderSceneShadows(renderContext);
        RenderScenesWithView(renderContext);
    }

    public void RenderScenesWithView(Scene.RenderContext renderContext)
    {
        var (w, h) = (renderContext.Framebuffer.Width, renderContext.Framebuffer.Height);

        GL.Viewport(0, 0, w, h);
        ViewBuffer!.Data.InvViewportSize = Vector4.One / new Vector4(w, h, 1, 1);

        renderContext.Framebuffer.BindAndClear();

        var isWireframe = IsWireframe; // To avoid toggling it mid frame

        // TODO: check if renderpass allows wireframe mode
        // TODO+: replace wireframe shaders with solid color
        if (isWireframe)
        {
            GL.PolygonMode(TriangleFace.FrontAndBack, PolygonMode.Line);
        }

        GL.DepthRange(0.05, 1);

        UpdatePerViewGpuBuffers(Scene, renderContext.Camera);
        Scene.SetSceneBuffers();

        using (new GLDebugGroup("Main Scene Opaque Render"))
        {
            renderContext.Scene = Scene;
            Scene.RenderOpaqueLayer(renderContext);
        }

        using (new GLDebugGroup("Occlusion Tests"))
        {
            Scene.RenderOcclusionProxies(renderContext, depthOnlyShaders[(int)DepthOnlyProgram.OcclusionQueryAABBProxy]);
        }

        using (new GLDebugGroup("Sky Render"))
        {
            GL.DepthRange(0, 0.05);

            renderContext.ReplacementShader?.SetUniform1("isSkybox", 1u);
            var render3DSkybox = ShowSkybox && SkyboxScene != null;
            var (copyColor, copyDepth) = (Scene.WantsSceneColor, Scene.WantsSceneDepth);

            if (render3DSkybox)
            {
                SkyboxScene!.SetSceneBuffers();
                renderContext.Scene = SkyboxScene;

                copyColor |= SkyboxScene.WantsSceneColor;
                copyDepth |= SkyboxScene.WantsSceneDepth;

                using var _ = new GLDebugGroup("3D Sky Scene");
                SkyboxScene.RenderOpaqueLayer(renderContext);
            }

            if (!isWireframe)
            {
                using (new GLDebugGroup("2D Sky Render"))
                {
                    Skybox2D.Render();
                }
            }

            if (renderContext.Framebuffer == MainFramebuffer)
            {
                GrabFramebufferCopy(renderContext.Framebuffer, copyColor, copyDepth);
            }

            if (render3DSkybox)
            {
                using (new GLDebugGroup("3D Sky Scene Translucent Render"))
                {
                    RenderTranslucentLayer(SkyboxScene, renderContext);
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

        if (renderContext.ReplacementShader is null)
        {
            using (new GLDebugGroup("Outline Render"))
            {
                RenderOutlineLayer(renderContext);
            }
        }
    }

    public void RenderSceneShadows(Scene.RenderContext renderContext)
    {
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

        Scene.RenderOpaqueShadows(renderContext, depthOnlyShaders);
    }

    private void RenderOutlineLayer(Scene.RenderContext renderContext)
    {
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

    public void GrabFramebufferCopy(Framebuffer framebuffer, bool copyColor, bool copyDepth)
    {
        if (!copyColor && !copyDepth)
        {
            return;
        }

        if (FramebufferCopy.Width != framebuffer.Width ||
            FramebufferCopy.Height != framebuffer.Height)
        {
            FramebufferCopy.Resize(framebuffer.Width, framebuffer.Height);
        }

        FramebufferCopy.BindAndClear(FramebufferTarget.DrawFramebuffer);

        var flags = ClearBufferMask.None;
        flags |= copyColor ? ClearBufferMask.ColorBufferBit : 0;
        flags |= copyDepth ? ClearBufferMask.DepthBufferBit : 0;

        GL.BlitNamedFramebuffer(framebuffer.FboHandle, FramebufferCopy.FboHandle,
            0, 0, framebuffer.Width, framebuffer.Height,
            0, 0, FramebufferCopy.Width, FramebufferCopy.Height, flags, BlitFramebufferFilter.Nearest);

        framebuffer.Bind(FramebufferTarget.Framebuffer);
    }

    /// <summary>
    /// Multisampling resolve, postprocess the image & convert to gamma.
    /// </summary>
    public void PostprocessRender(Framebuffer inputFramebuffer, Framebuffer outputFramebuffer, bool flipY = false)
    {
        using var _ = new GLDebugGroup("Post Processing");

        inputFramebuffer.Bind(FramebufferTarget.ReadFramebuffer);
        outputFramebuffer.Bind(FramebufferTarget.DrawFramebuffer);

        Debug.Assert(inputFramebuffer.NumSamples > 0);
        Debug.Assert(outputFramebuffer.NumSamples == 0);

        Postprocess.Render(colorBuffer: inputFramebuffer, flipY);
    }

    public void Dispose()
    {
        ViewBuffer?.Dispose();
        Scene?.Dispose();
        SkyboxScene?.Dispose();
    }

    public void Update(Scene.UpdateContext updateContext)
    {
        Uptime += updateContext.Timestep;
        ViewBuffer.Data.Time = Uptime;

        Camera.RecalculateMatrices();

        Scene.Update(updateContext);
        SkyboxScene?.Update(updateContext);

        Scene.PostProcessInfo.UpdatePostProcessing(updateContext.Camera);

        Scene.SetupSceneShadows(updateContext.Camera, ShadowDepthBuffer.Width);
        Scene.GetOcclusionTestResults();

        Scene.CollectSceneDrawCalls(updateContext.Camera, LockedCullFrustum);
        SkyboxScene?.CollectSceneDrawCalls(updateContext.Camera, LockedCullFrustum);
    }
}
