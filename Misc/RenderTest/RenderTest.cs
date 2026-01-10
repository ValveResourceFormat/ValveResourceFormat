using System.Diagnostics;
using System.IO;
using System.Linq;
using Microsoft.Extensions.Logging;
using OpenTK.Graphics.OpenGL;
using OpenTK.Windowing.Common;
using OpenTK.Windowing.Desktop;
using OpenTK.Windowing.GraphicsLibraryFramework;
using SteamDatabase.ValvePak;
using ValveResourceFormat.IO;
using ValveResourceFormat.Renderer;
using ValveResourceFormat.Renderer.Buffers;
using ValveResourceFormat.ResourceTypes;
using Vector2 = System.Numerics.Vector2;
using Vector3 = System.Numerics.Vector3;

internal class RendererImplementation : Renderer
{
    public RendererImplementation(RendererContext rendererContext) : base(rendererContext)
    {
    }

    public void UpdateUptime(float deltaTime)
    {
        Uptime += deltaTime;
    }
}

internal class RenderTestWindow(GameWindowSettings gameWindowSettings, NativeWindowSettings nativeWindowSettings)
    : GameWindow(gameWindowSettings, nativeWindowSettings)
{
    private Scene? scene;
    private Camera? camera;
    private WorldLoader? worldLoader;
    private Framebuffer? framebuffer;
    private UniformBuffer<ViewConstants>? viewBuffer;
    private TextRenderer? textRenderer;
    private RendererContext? rendererContext;
    private SceneSkybox2D? skybox2D;
    private RendererImplementation renderer;

    private Vector2 lastMousePosition;
    private bool firstMouseMove = true;

    protected override void OnLoad()
    {
        base.OnLoad();

        using var loggerFactory = LoggerFactory.Create(builder =>
        {
            builder.AddConsole();
            builder.SetMinimumLevel(LogLevel.Debug);
        });

        var logger = loggerFactory.CreateLogger<RenderTestWindow>();

        var gamesAndMaps = new Dictionary<int, string>
        {
            { 730, "game/csgo/maps/de_dust2.vpk" },
            { 570, "game/dota/maps/dota.vpk" },
            { 546560, "game/hlvr/maps/a2_train_yard.vpk" },
            { 1422450, "game/citadel/maps/dl_hideout.vpk" },
            { 1902490, "game/steampal/maps/aperture_desk_job.vpk" },
        };

        string? mapVpk = null;

        foreach (var (appId, mapPath) in gamesAndMaps)
        {
            var gamePath = GameFolderLocator.FindSteamGameByAppId(appId);
            if (gamePath.HasValue)
            {
                var potentialMapPath = Path.Join(gamePath.Value.GamePath, mapPath);
                if (File.Exists(potentialMapPath))
                {
                    mapVpk = potentialMapPath;
                    logger.LogInformation("Found map: {MapPath} for {GameName} (AppID: {AppId})",
                        mapVpk, gamePath.Value.AppName, appId);
                    break;
                }
            }
        }

        if (mapVpk == null)
        {
            throw new DirectoryNotFoundException($"Failed to find any supported Source 2 game. Tried AppIDs: {string.Join(", ", gamesAndMaps.Keys)}");
        }

        using var vpk = new Package();
        vpk.OptimizeEntriesForBinarySearch();
        vpk.Read(mapVpk);

        using var fileLoader = new GameFileLoader(vpk, mapVpk);
        rendererContext = new RendererContext(fileLoader, logger)
        {
            FieldOfView = 75
        };

        GLEnvironment.Initialize(rendererContext.Logger);
        GLEnvironment.SetDefaultRenderState();

        GL.Enable(EnableCap.FramebufferSrgb);

        logger.LogInformation("Loading scene...");

        // Load scene resources
        LoadScene(vpk, rendererContext, ClientSize.X, ClientSize.Y);

        // Lock cursor for mouse look
        CursorState = CursorState.Grabbed;
    }

    protected override void OnUpdateFrame(FrameEventArgs args)
    {
        base.OnUpdateFrame(args);

        if (renderer == null || camera == null)
        {
            return;
        }

        var input = KeyboardState;
        var deltaTime = (float)args.Time;

        // ESC to close
        if (input.IsKeyDown(Keys.Escape))
        {
            Close();
            return;
        }

        // Map OpenTK keys to TrackedKeys
        var trackedKeys = TrackedKeys.None;
        if (input.IsKeyDown(Keys.W)) trackedKeys |= TrackedKeys.Forward;
        if (input.IsKeyDown(Keys.S)) trackedKeys |= TrackedKeys.Back;
        if (input.IsKeyDown(Keys.A)) trackedKeys |= TrackedKeys.Left;
        if (input.IsKeyDown(Keys.D)) trackedKeys |= TrackedKeys.Right;
        if (input.IsKeyDown(Keys.Q)) trackedKeys |= TrackedKeys.Up;
        if (input.IsKeyDown(Keys.Z)) trackedKeys |= TrackedKeys.Down;
        if (input.IsKeyDown(Keys.LeftShift) || input.IsKeyDown(Keys.RightShift)) trackedKeys |= TrackedKeys.Shift;
        if (input.IsKeyDown(Keys.LeftAlt) || input.IsKeyDown(Keys.RightAlt)) trackedKeys |= TrackedKeys.Alt;
        if (input.IsKeyDown(Keys.LeftControl) || input.IsKeyDown(Keys.RightControl)) trackedKeys |= TrackedKeys.Control;

        // Get mouse delta
        var mousePos = new Vector2(MouseState.Position.X, MouseState.Position.Y);
        var mouseDelta = firstMouseMove ? Vector2.Zero : mousePos - lastMousePosition;
        lastMousePosition = mousePos;
        firstMouseMove = false;

        // Update input system
        renderer.UpdateUptime(deltaTime);
        renderer.Input.Tick(deltaTime, trackedKeys, mouseDelta, camera);
    }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        base.OnMouseWheel(e);

        renderer?.Input.OnMouseWheel(e.OffsetY);
    }

    protected override void OnResize(ResizeEventArgs e)
    {
        base.OnResize(e);

        GL.Viewport(0, 0, e.Width, e.Height);
        camera?.SetViewportSize(e.Width, e.Height);
        framebuffer?.Resize(e.Width, e.Height);
    }

    protected override void OnRenderFrame(FrameEventArgs args)
    {
        base.OnRenderFrame(args);

        // Clear the screen
        GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);

        RenderScene((float)args.Time);

        SwapBuffers();
    }

    private void LoadScene(Package vpk, RendererContext rendererContext, int width, int height)
    {
        // Load the map world file
        Debug.Assert(vpk.Entries != null);

        if (!vpk.Entries.TryGetValue("vwrld_c", out var worlds))
        {
            throw new InvalidOperationException("This vpk has no vwwrld_c files");
        }

        var mapResource = rendererContext.FileLoader.LoadFile(worlds[0].GetFullPath()) ?? throw new FileNotFoundException("World not found");

        // Extract World data
        if (mapResource.DataBlock is not World worldData)
        {
            throw new InvalidOperationException("Resource is not a World type.");
        }

        renderer = new RendererImplementation(rendererContext);
        camera = renderer.Camera;
        camera.SetViewportSize(width, height);

        scene = new Scene(rendererContext);

        // Create ViewConstants buffer (required for shaders)
        viewBuffer = new UniformBuffer<ViewConstants>(ReservedBufferSlots.View);

        // Create TextRenderer (needed for Scene.Update)
        textRenderer = new TextRenderer(rendererContext, camera);
        textRenderer.Load();

        // Create framebuffer for rendering
        framebuffer = Framebuffer.Prepare("MainFramebuffer", width, height, 4,
            new(PixelInternalFormat.R11fG11fB10f, PixelFormat.Rgb, PixelType.UnsignedInt),
            Framebuffer.DepthAttachmentFormat.Depth32FStencil8);
        framebuffer.Initialize();

        // Create WorldLoader and load
        worldLoader = new WorldLoader(worldData, scene, mapResource.ExternalReferences);

        // Initialise 2d skybox
        skybox2D = worldLoader.Skybox2D;

        // Initialize scene (creates lighting buffers, octrees, etc.)
        scene.Initialize();

        // Set initial camera position
        if (scene.AllNodes.Any())
        {
            var bbox = scene.AllNodes.First().BoundingBox;
            foreach (var node in scene.AllNodes.Skip(1))
            {
                bbox = bbox.Union(node.BoundingBox);
            }
            var center = bbox.Center;
            var size = bbox.Size;
            var offset = Math.Max(size.X, Math.Max(size.Y, size.Z)) * 0.5f;
            renderer.Input.Camera.SetLocation(new Vector3(center.X + offset, center.Y + offset * 0.5f, center.Z + offset));
            renderer.Input.Camera.LookAt(center);
        }
    }

    private void RenderScene(float deltaTime)
    {
        Debug.Assert(scene != null, "Scene is not loaded.");
        Debug.Assert(camera != null, "Camera is not loaded.");
        Debug.Assert(viewBuffer != null, "ViewBuffer is not created.");
        Debug.Assert(framebuffer is not null, "Framebuffer is not created.");
        Debug.Assert(rendererContext != null, "RendererContext is not created.");
        Debug.Assert(textRenderer != null, "TextRenderer is not created.");

        // Update camera matrices
        camera.RecalculateMatrices();

        // Update scene (clears previous frame's draw calls and updates internal state)
        var updateContext = new Scene.UpdateContext
        {
            Camera = camera,
            TextRenderer = textRenderer,
            Timestep = deltaTime,
        };
        scene.Update(updateContext);

        // Setup scene lighting and shadows (required even without shadow rendering)
        scene.SetupSceneShadows(camera, 1024);

        // Collect visible draw calls (culling)
        scene.CollectSceneDrawCalls(camera, null);

        // Update view constants (camera matrices and fog)
        camera.SetViewConstants(viewBuffer.Data);
        scene.SetFogConstants(viewBuffer.Data);
        viewBuffer.BindBufferBase();
        viewBuffer.Update();

        // Setup render context
        var renderContext = new Scene.RenderContext
        {
            Camera = camera,
            Framebuffer = framebuffer,
            Scene = scene,
            Textures = [],
        };

        // Bind scene lighting buffers
        scene.SetSceneBuffers();

        // Clear and render opaque layer only
        framebuffer.BindAndClear();
        scene.RenderOpaqueLayer(renderContext);

        // Render 2d skybox if present
        if (skybox2D != null)
        {
            skybox2D.Render();
        }

        // Render translucent layer including water
        scene.RenderWaterLayer(renderContext);

        GL.DepthMask(false);
        GL.Enable(EnableCap.Blend);

        scene.RenderTranslucentLayer(renderContext);

        GL.Disable(EnableCap.Blend);
        GL.DepthMask(true);

        GL.BlitNamedFramebuffer(
            framebuffer.FboHandle, 0,
            0, 0, framebuffer.Width, framebuffer.Height,
            0, 0, framebuffer.Width, framebuffer.Height,
            ClearBufferMask.ColorBufferBit,
            BlitFramebufferFilter.Nearest
        );

        // Reset framebuffer state for next frame
        GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            scene?.Dispose();
            viewBuffer?.Dispose();
            rendererContext?.Dispose();
        }

        base.Dispose(disposing);
    }
}

internal class Program
{
    private static void Main()
    {
        var gameWindowSettings = new GameWindowSettings()
        {
            UpdateFrequency = 60.0,
        };

        var nativeWindowSettings = new NativeWindowSettings()
        {
            APIVersion = GLEnvironment.RequiredVersion,
            ClientSize = new(1280, 720),
            WindowBorder = WindowBorder.Resizable,
            WindowState = WindowState.Normal,
            Title = "S2V Render Test",
            Flags = ContextFlags.ForwardCompatible,
            Profile = ContextProfile.Core,
        };

        using var window = new RenderTestWindow(gameWindowSettings, nativeWindowSettings);
        window.Run();
    }
}
