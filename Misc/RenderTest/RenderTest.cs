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
using ValveResourceFormat.ResourceTypes;
using Vector2 = System.Numerics.Vector2;
using Vector3 = System.Numerics.Vector3;

internal class RenderTestWindow : GameWindow
{
    private SceneRenderer? SceneRenderer;
    private Framebuffer? framebuffer;
    private TextRenderer? textRenderer;
    private readonly RendererContext rendererContext;

    private Vector2 lastMousePosition;
    private bool firstMouseMove = true;

    public bool Loaded { get; private set; }

    public RenderTestWindow(GameWindowSettings gameWindowSettings, NativeWindowSettings nativeWindowSettings)
        : base(gameWindowSettings, nativeWindowSettings)
    {
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

#pragma warning disable CA2000 // Dispose objects before losing scope
        var vpk = new Package();
        vpk.Read(mapVpk);
        var fileLoader = new GameFileLoader(vpk, mapVpk);
#pragma warning restore CA2000 // Dispose objects before losing scope

        rendererContext = new RendererContext(fileLoader, logger)
        {
            FieldOfView = 75
        };
    }

    protected override void OnLoad()
    {
        base.OnLoad();

        GLEnvironment.Initialize(rendererContext.Logger);
        GLEnvironment.SetDefaultRenderState();
        GL.Enable(EnableCap.FramebufferSrgb);

        SceneRenderer = new SceneRenderer(rendererContext);

        rendererContext.Logger.LogInformation("Loading scene...");
        LoadScene(rendererContext.FileLoader.CurrentPackage!, rendererContext);
        Loaded = true;

        // Lock cursor for mouse look
        CursorState = CursorState.Grabbed;
    }

    protected override void OnUpdateFrame(FrameEventArgs args)
    {
        base.OnUpdateFrame(args);

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

        if (Loaded == false)
        {
            return;
        }

        SceneRenderer!.Input.Tick(deltaTime, trackedKeys, mouseDelta, SceneRenderer.Camera);

        // Update scene
        var updateContext = new Scene.UpdateContext
        {
            Camera = SceneRenderer.Camera,
            TextRenderer = textRenderer!,
            Timestep = deltaTime,
        };

        SceneRenderer.Update(updateContext);
    }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        base.OnMouseWheel(e);

        SceneRenderer?.Input.OnMouseWheel(e.OffsetY);
    }

    protected override void OnResize(ResizeEventArgs e)
    {
        base.OnResize(e);

        GL.Viewport(0, 0, e.Width, e.Height);
        SceneRenderer?.Camera.SetViewportSize(e.Width, e.Height);
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

    private void LoadScene(Package vpk, RendererContext rendererContext)
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

        Debug.Assert(SceneRenderer != null);
        var scene = SceneRenderer.Scene;

        // Create TextRenderer (needed for Scene.Update)
        textRenderer = new TextRenderer(rendererContext, SceneRenderer.Camera);
        textRenderer.Load();

        // Create framebuffer for rendering
        framebuffer = Framebuffer.Prepare("MainFramebuffer", 4, 4, 4,
            new(PixelInternalFormat.R11fG11fB10f, PixelFormat.Rgb, PixelType.UnsignedInt),
            Framebuffer.DepthAttachmentFormat.Depth32FStencil8);
        framebuffer.Initialize();

        SceneRenderer.Initialize();
        SceneRenderer.MainFramebuffer = framebuffer;

        SceneRenderer.LoadRendererResources();

        var worldLoader = new WorldLoader(worldData, scene, mapResource.ExternalReferences);
        SceneRenderer.Skybox2D = worldLoader.Skybox2D;

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

            SceneRenderer.Input.SaveCameraForTransition(3f);
            SceneRenderer.Input.Camera.SetLocation(new Vector3(center.X + offset, center.Y + offset * 0.5f, center.Z + offset));
            SceneRenderer.Input.Camera.LookAt(center);
        }
    }

    private void RenderScene(float deltaTime)
    {
        Debug.Assert(SceneRenderer != null, "SceneRenderer is not loaded.");
        Debug.Assert(framebuffer is not null, "Framebuffer is not created.");
        Debug.Assert(textRenderer != null, "TextRenderer is not created.");

        // Setup render context
        var renderContext = new Scene.RenderContext
        {
            Camera = SceneRenderer.Camera,
            Framebuffer = framebuffer,
            Scene = SceneRenderer.Scene,
            Textures = SceneRenderer.Textures,
        };

        // Render using SceneRenderer
        SceneRenderer.Render(renderContext);

        // Blit to default framebuffer
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
            SceneRenderer?.Dispose();
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
