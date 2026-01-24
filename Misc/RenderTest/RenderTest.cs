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
    private Renderer? SceneRenderer;
    private Framebuffer? framebuffer;
    private TextRenderer? textRenderer;
    private UserInput? Input;
    private readonly RendererContext rendererContext;

    private Vector2 lastMousePosition;
    private bool firstMouseMove = true;
    private bool isFullscreen;
    private bool isCursorLocked = true;
    private OpenTK.Mathematics.Vector2i windowedSize;
    private OpenTK.Mathematics.Vector2i windowedPosition;

    private double fpsUpdateTimer;
    private int frameCount;
    private double currentFps;

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

        SceneRenderer = new Renderer(rendererContext);
        Input = new UserInput(SceneRenderer);

        rendererContext.Logger.LogInformation("Loading scene...");
        LoadScene(rendererContext.FileLoader.CurrentPackage!, rendererContext);
        Loaded = true;

        // Lock cursor for mouse look
        CursorState = CursorState.Grabbed;
        isCursorLocked = true;
    }

    private void SetFullscreen(bool fullscreen)
    {
        if (fullscreen)
        {
            // Save current window state
            windowedSize = ClientSize;
            windowedPosition = Location;

            // Enter fullscreen - cover entire monitor
            var monitor = Monitors.GetMonitorFromWindow(this);
            WindowBorder = WindowBorder.Hidden;
            WindowState = WindowState.Normal;
            Location = new OpenTK.Mathematics.Vector2i(monitor.ClientArea.Min.X, monitor.ClientArea.Min.Y);
            ClientSize = new OpenTK.Mathematics.Vector2i(monitor.ClientArea.Size.X, monitor.ClientArea.Size.Y);
        }
        else
        {
            // Restore windowed mode
            WindowBorder = WindowBorder.Resizable;
            WindowState = WindowState.Normal;
            ClientSize = windowedSize;
            Location = windowedPosition;
        }

        isFullscreen = fullscreen;
    }

    protected override void OnUpdateFrame(FrameEventArgs args)
    {
        base.OnUpdateFrame(args);

        var input = KeyboardState;
        var deltaTime = (float)args.Time;

        if (input.IsKeyPressed(Keys.Escape))
        {
            if (isFullscreen)
            {
                // Exit fullscreen first
                SetFullscreen(false);
            }
            else if (isCursorLocked)
            {
                // Unlock cursor first
                CursorState = CursorState.Normal;
                isCursorLocked = false;
            }
            else
            {
                Close();
            }
            return;
        }

        // F11 to toggle fullscreen
        if (input.IsKeyPressed(Keys.F11))
        {
            SetFullscreen(!isFullscreen);
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

        if (Loaded == false)
        {
            return;
        }

        // Get mouse delta
        var mousePos = new Vector2(MouseState.Position.X, MouseState.Position.Y);
        var mouseDelta = Vector2.Zero;

        if (isCursorLocked && !firstMouseMove)
        {
            mouseDelta = mousePos - lastMousePosition;
        }

        lastMousePosition = mousePos;
        firstMouseMove = false;

        Input!.Tick(deltaTime, trackedKeys, mouseDelta, SceneRenderer!.Camera);

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

        Input?.OnMouseWheel(e.OffsetY);
    }

    protected override void OnMouseDown(MouseButtonEventArgs e)
    {
        base.OnMouseDown(e);

        // Re-lock cursor when clicking in the window
        if (!isCursorLocked)
        {
            CursorState = CursorState.Grabbed;
            isCursorLocked = true;
            firstMouseMove = true; // Reset to prevent camera jump
        }
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

        // Update FPS calculation
        frameCount++;
        fpsUpdateTimer += args.Time;

        if (fpsUpdateTimer >= 0.1) // Update FPS every 100ms
        {
            currentFps = frameCount / fpsUpdateTimer;
            frameCount = 0;
            fpsUpdateTimer = 0;
        }

        // Clear the screen
        GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);

        RenderScene((float)args.Time);

        SwapBuffers();
    }

    private void LoadScene(Package vpk, RendererContext rendererContext)
    {
        Debug.Assert(vpk.Entries != null);

        Debug.Assert(SceneRenderer != null);
        var scene = SceneRenderer.Scene;

        // Create TextRenderer (needed for Scene.Update)
        textRenderer = new TextRenderer(rendererContext, SceneRenderer.Camera);
        textRenderer.Load();

        SceneRenderer.Postprocess.Load();

        // Create framebuffer for rendering
        framebuffer = Framebuffer.Prepare("MainFramebuffer", 4, 4, 4,
            new(PixelInternalFormat.Rgba16f, PixelFormat.Rgba, PixelType.HalfFloat),
            Framebuffer.DepthAttachmentFormat.Depth32FStencil8);
        framebuffer.Initialize();

        SceneRenderer.Initialize();
        SceneRenderer.MainFramebuffer = framebuffer;

        SceneRenderer.LoadRendererResources();

        if (!vpk.Entries.TryGetValue("vmap_c", out var vmaps))
        {
            throw new InvalidOperationException("This vpk has no vmap_c file");
        }

        var mapPath = vmaps[0].GetFullPath();
        var loadedMap = WorldLoader.LoadMap(mapPath, scene);

        SceneRenderer.SkyboxScene = loadedMap.SkyboxScene;
        SceneRenderer.Skybox2D = loadedMap.Skybox2D;

        // Initialize scene (creates lighting buffers, octrees, etc.)
        SceneRenderer.Scene.Initialize();
        SceneRenderer.SkyboxScene?.Initialize();

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

            Input!.SaveCameraForTransition(3f);
            Input.Camera.SetLocation(new Vector3(center.X + offset, center.Y + offset * 0.5f, center.Z + offset));
            Input.Camera.LookAt(center);
        }
    }

    private void RenderScene(float deltaTime)
    {
        Debug.Assert(SceneRenderer != null, "SceneRenderer is not loaded.");
        Debug.Assert(framebuffer is not null, "Framebuffer is not created.");
        Debug.Assert(textRenderer != null, "TextRenderer is not created.");

        SceneRenderer.Render(framebuffer);
        SceneRenderer.PostprocessRender(framebuffer, Framebuffer.GLDefaultFramebuffer, flipY: false);

        textRenderer.AddText(new TextRenderer.TextRenderRequest
        {
            X = framebuffer.Width * 0.95f,
            Y = framebuffer.Height * 0.03f,
            Scale = 12f,
            Color = new Color32(0, 255, 0),
            Text = $"FPS: {currentFps:0}"
        });

        textRenderer.Render(SceneRenderer.Camera);
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
            UpdateFrequency = 0,
        };

        var nativeWindowSettings = new NativeWindowSettings()
        {
            APIVersion = GLEnvironment.RequiredVersion,
            Vsync = VSyncMode.Adaptive,
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
