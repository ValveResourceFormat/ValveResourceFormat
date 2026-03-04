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
using static ValveResourceFormat.ResourceTypes.EntityLump;
using Matrix4x4 = System.Numerics.Matrix4x4;
using Vector2 = System.Numerics.Vector2;
using Vector3 = System.Numerics.Vector3;

internal record MapTransitionData(
    Dictionary<string, Vector3> Landmarks,
    List<(string LandmarkName, string NextMapName)> Transitions
);

internal record MapChainEntry(
    string MapVpkPath,
    string MapName,
    Vector3 Offset,
    string? LandmarkName = null,
    Vector3 LandmarkWorldPos = default
);

internal class RenderTestWindow : GameWindow
{
    private Renderer? SceneRenderer;
    private Framebuffer? framebuffer;
    private TextRenderer? textRenderer;
    private UserInput? Input;
    private readonly RendererContext rendererContext;
    private readonly string gamePath;
    private readonly ILogger logger;

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

        logger = loggerFactory.CreateLogger<RenderTestWindow>();

        var hlvrGame = GameFolderLocator.FindSteamGameByAppId(546560)
            ?? throw new DirectoryNotFoundException("Half-Life: Alyx (AppID 546560) not found.");

        gamePath = hlvrGame.GamePath;
        logger.LogInformation("Found HL:A at {GamePath}", gamePath);

        var firstMapVpk = Path.Join(gamePath, "game/hlvr/maps/a1_intro_world_2.vpk");
        if (!File.Exists(firstMapVpk))
        {
            throw new FileNotFoundException($"First map not found: {firstMapVpk}");
        }

#pragma warning disable CA2000 // Dispose objects before losing scope
        var vpk = new Package();
        vpk.Read(firstMapVpk);
        var fileLoader = new GameFileLoader(vpk, firstMapVpk);
#pragma warning restore CA2000 // Dispose objects before losing scope

        rendererContext = new RendererContext(fileLoader, logger)
        {
            FieldOfView = 75,
            MaxTextureSize = 32,
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
        LoadScene();
        Loaded = true;

        // Lock cursor for mouse look
        CursorState = CursorState.Grabbed;
        isCursorLocked = true;
    }

    private static MapTransitionData ReadMapTransitionData(GameFileLoader fileLoader, string mapVpkPath)
    {
        using var vpk = new Package();
        vpk.Read(mapVpkPath);

        if (vpk.Entries == null || !vpk.Entries.TryGetValue("vmap_c", out var vmaps))
        {
            return new MapTransitionData([], []);
        }

        var mapPath = vmaps[0].GetFullPath();
        var mapResourceName = mapPath.EndsWith(GameFileLoader.CompiledFileSuffix, StringComparison.OrdinalIgnoreCase)
            ? mapPath[..^GameFileLoader.CompiledFileSuffix.Length]
            : mapPath;
        var worldPath = WorldLoader.GetWorldNameFromMap(mapResourceName);

        // Temporarily add this VPK to search paths to load resources
        fileLoader.AddPackageToSearch(vpk);

        try
        {
            var worldResource = fileLoader.LoadFileCompiled(worldPath);
            if (worldResource?.DataBlock is not World world)
            {
                return new MapTransitionData([], []);
            }

            var landmarks = new Dictionary<string, Vector3>();
            var transitions = new List<(string LandmarkName, string NextMapName)>();

            foreach (var lumpName in world.GetEntityLumpNames())
            {
                if (lumpName == null)
                {
                    continue;
                }

                var lumpResource = fileLoader.LoadFileCompiled(lumpName);
                if (lumpResource?.DataBlock is not EntityLump entityLump)
                {
                    continue;
                }

                foreach (var entity in entityLump.GetEntities())
                {
                    var classname = entity.GetProperty<string>("classname");

                    switch (classname)
                    {
                        case "info_landmark":
                        case "info_spawngroup_landmark":
                            {
                                var name = entity.GetProperty<string>("targetname");
                                if (name != null)
                                {
                                    var origin = entity.GetVector3Property("origin");
                                    landmarks[name] = origin;
                                }

                                break;
                            }
                        case "trigger_changelevel":
                            {
                                var landmarkName = entity.GetProperty<string>("landmark");
                                var nextMap = entity.GetProperty<string>("map");
                                if (landmarkName != null && nextMap != null)
                                {
                                    transitions.Add((landmarkName, nextMap));
                                }

                                break;
                            }
                        case "info_spawngroup_load_unload":
                            {
                                var mapName = entity.GetProperty<string>("mapname");
                                var landmarkName = entity.GetProperty<string>("landmark");
                                if (mapName != null && landmarkName != null)
                                {
                                    transitions.Add((landmarkName, mapName));
                                }

                                break;
                            }
                    }
                }
            }

            return new MapTransitionData(landmarks, transitions);
        }
        finally
        {
            fileLoader.RemovePackageFromSearch(vpk);
        }
    }

    private List<MapChainEntry> BuildMapChain(GameFileLoader fileLoader, int maxMaps = int.MaxValue)
    {
        var mapsDir = Path.Join(gamePath, "game/hlvr/maps");

        // Scan all map VPKs upfront (skip skybox maps)
        var allMapVpks = Directory.GetFiles(mapsDir, "*.vpk")
            .Where(f =>
            {
                var name = Path.GetFileNameWithoutExtension(f);
                return !name.EndsWith("_skybox", StringComparison.OrdinalIgnoreCase)
                    && !name.EndsWith("_commentary", StringComparison.OrdinalIgnoreCase);
            })
            .ToList();

        logger.LogInformation("Found {Count} map VPKs, reading transition data...", allMapVpks.Count);

        // Read transition data from every map
        var allTransitions = new Dictionary<string, MapTransitionData>();
        foreach (var vpkPath in allMapVpks)
        {
            var mapName = Path.GetFileNameWithoutExtension(vpkPath);
            logger.LogInformation("  Reading {Map}...", mapName);
            var data = ReadMapTransitionData(fileLoader, vpkPath);
            allTransitions[mapName] = data;
            logger.LogInformation("    Landmarks: {L}, Transitions: {T}",
                data.Landmarks.Count, data.Transitions.Count);
        }

        // Build reverse lookup: which maps reference which other maps
        // Also build forward lookup from transitions
        var forwardEdges = new Dictionary<string, List<(string landmarkName, string targetMap)>>();
        var reverseEdges = new Dictionary<string, List<(string landmarkName, string sourceMap)>>();

        foreach (var (mapName, data) in allTransitions)
        {
            foreach (var (landmarkName, nextMapName) in data.Transitions)
            {
                if (!forwardEdges.TryGetValue(mapName, out var fwd))
                {
                    fwd = [];
                    forwardEdges[mapName] = fwd;
                }
                fwd.Add((landmarkName, nextMapName));

                if (!reverseEdges.TryGetValue(nextMapName, out var rev))
                {
                    rev = [];
                    reverseEdges[nextMapName] = rev;
                }
                rev.Add((landmarkName, mapName));
            }
        }

        // Print the graph
        Console.WriteLine("=== Map Transition Graph ===");
        foreach (var (mapName, data) in allTransitions.OrderBy(x => x.Key))
        {
            if (data.Landmarks.Count == 0 && data.Transitions.Count == 0)
            {
                continue;
            }

            Console.WriteLine($"[{mapName}]");
            foreach (var (name, pos) in data.Landmarks)
            {
                Console.WriteLine($"  landmark: {name} at {pos}");
            }
            foreach (var (landmarkName, nextMap) in data.Transitions)
            {
                Console.WriteLine($"  -> {nextMap} via '{landmarkName}'");
            }
        }
        Console.WriteLine("============================");

        // Find the starting map
        var firstMapName = "a1_intro_world_2";
        if (!allTransitions.ContainsKey(firstMapName))
        {
            firstMapName = allTransitions.Keys
                .FirstOrDefault(m => !reverseEdges.ContainsKey(m))
                ?? allTransitions.Keys.First();
        }

        var firstMapVpk = Path.Join(mapsDir, $"{firstMapName}.vpk");

        var chain = new List<MapChainEntry>
        {
            new(firstMapVpk, firstMapName, Vector3.Zero)
        };

        var visited = new HashSet<string> { firstMapName };

        // Walk the graph using both forward and reverse edges
        while (chain.Count < maxMaps)
        {
            var current = chain[^1];
            var currentData = allTransitions[current.MapName];
            var foundNext = false;

            Console.WriteLine($"Walking from: '{current.MapName}' (fwd={forwardEdges.ContainsKey(current.MapName)}, rev={reverseEdges.ContainsKey(current.MapName)})");
            if (chain.Count == 2)
            {
                Console.WriteLine("Forward edge keys:");
                foreach (var k in forwardEdges.Keys.Order())
                {
                    Console.WriteLine($"  '{k}'");
                }
            }

            // Try forward edges first (this map transitions to next)
            if (forwardEdges.TryGetValue(current.MapName, out var fwdEdges))
            {
                foreach (var (landmarkName, nextMapName) in fwdEdges)
                {
                    if (visited.Contains(nextMapName) || !allTransitions.ContainsKey(nextMapName))
                    {
                        Console.WriteLine($"  skip fwd {current.MapName} -> {nextMapName} (visited={visited.Contains(nextMapName)}, known={allTransitions.ContainsKey(nextMapName)})");
                        continue;
                    }

                    if (!currentData.Landmarks.TryGetValue(landmarkName, out var currLandmarkPos))
                    {
                        Console.WriteLine($"  skip fwd {current.MapName} -> {nextMapName}: landmark '{landmarkName}' not in current map");
                        continue;
                    }

                    var nextData = allTransitions[nextMapName];
                    if (!nextData.Landmarks.TryGetValue(landmarkName, out var nextLandmarkPos))
                    {
                        Console.WriteLine($"  skip fwd {current.MapName} -> {nextMapName}: landmark '{landmarkName}' not in next map");
                        continue;
                    }

                    var nextOffset = current.Offset + (currLandmarkPos - nextLandmarkPos);
                    var nextMapVpk = Path.Join(mapsDir, $"{nextMapName}.vpk");

                    var landmarkWorldPos = currLandmarkPos + current.Offset;
                    logger.LogInformation("Chain: {Map} -> {NextMap} via landmark '{Landmark}'",
                        current.MapName, nextMapName, landmarkName);

                    chain.Add(new MapChainEntry(nextMapVpk, nextMapName, nextOffset, landmarkName, landmarkWorldPos));
                    visited.Add(nextMapName);
                    foundNext = true;
                    break;
                }
            }

            // Try reverse edges (another map transitions to this one, check its other neighbors)
            if (!foundNext && reverseEdges.TryGetValue(current.MapName, out var revEdges))
            {
                Console.WriteLine($"  trying {revEdges.Count} reverse edges");
                foreach (var (landmarkName, sourceMap) in revEdges)
                {
                    Console.WriteLine($"  rev edge: {sourceMap} -> {current.MapName} via '{landmarkName}' (sourceVisited={visited.Contains(sourceMap)}, sourceFwd={forwardEdges.ContainsKey(sourceMap)})");
                    if (!visited.Contains(sourceMap) || !forwardEdges.TryGetValue(sourceMap, out var srcFwd))
                    {
                        continue;
                    }

                    foreach (var (srcLandmark, srcTarget) in srcFwd)
                    {
                        if (visited.Contains(srcTarget) || !allTransitions.ContainsKey(srcTarget))
                        {
                            continue;
                        }

                        var srcData = allTransitions[sourceMap];
                        if (!srcData.Landmarks.TryGetValue(srcLandmark, out var srcLandmarkPos))
                        {
                            continue;
                        }

                        var targetData = allTransitions[srcTarget];
                        if (!targetData.Landmarks.TryGetValue(srcLandmark, out var targetLandmarkPos))
                        {
                            continue;
                        }

                        // Find the source map in the chain to get its offset
                        var srcEntry = chain.First(e => e.MapName == sourceMap);
                        var nextOffset = srcEntry.Offset + (srcLandmarkPos - targetLandmarkPos);
                        var nextMapVpk = Path.Join(mapsDir, $"{srcTarget}.vpk");

                        var landmarkWorldPos = srcLandmarkPos + srcEntry.Offset;
                        logger.LogInformation("Chain: {Source} -> {NextMap} via landmark '{Landmark}'",
                            sourceMap, srcTarget, srcLandmark);

                        chain.Add(new MapChainEntry(nextMapVpk, srcTarget, nextOffset, srcLandmark, landmarkWorldPos));
                        visited.Add(srcTarget);
                        foundNext = true;
                        break;
                    }

                    if (foundNext)
                    {
                        break;
                    }
                }
            }

            if (!foundNext)
            {
                logger.LogInformation("No more unvisited connected maps. Chain length: {Count}", chain.Count);
                break;
            }
        }

        // Print unaccounted maps
        var allMapNames = allTransitions.Keys.ToHashSet();
        allMapNames.ExceptWith(visited);
        if (allMapNames.Count > 0)
        {
            Console.WriteLine($"=== Unaccounted maps ({allMapNames.Count}) ===");
            foreach (var map in allMapNames.Order())
            {
                Console.WriteLine($"  {map}");
            }
        }

        return chain;
    }

    private void LoadScene()
    {
        Debug.Assert(SceneRenderer != null);
        var scene = SceneRenderer.Scene;
        var fileLoader = rendererContext.FileLoader;

        // Create TextRenderer (needed for Scene.Update)
        textRenderer = new TextRenderer(rendererContext, SceneRenderer.Camera);
        textRenderer.Load();

        // Create framebuffer for rendering
        framebuffer = Framebuffer.Prepare("MainFramebuffer", 4, 4, 4,
            new(PixelInternalFormat.Rgba16f, PixelFormat.Rgba, PixelType.HalfFloat),
            Framebuffer.DepthAttachmentFormat.Depth32FStencil8);
        framebuffer.Initialize();

        SceneRenderer.Initialize();
        SceneRenderer.MainFramebuffer = framebuffer;
        SceneRenderer.Postprocess.Load(framebuffer.NumSamples);

        SceneRenderer.LoadRendererResources();

        // Build map chain - limit to first 2 maps for initial test
        var mapChain = BuildMapChain(fileLoader);

        logger.LogInformation("Loading {Count} maps...", mapChain.Count);

        foreach (var entry in mapChain)
        {
            logger.LogInformation("Loading map: {Map} with offset {Offset}", entry.MapName, entry.Offset);

            // Add this map's VPK to search paths
            var vpk = fileLoader.AddPackageToSearch(entry.MapVpkPath);

            if (vpk.Entries == null || !vpk.Entries.TryGetValue("vmap_c", out var vmaps))
            {
                logger.LogWarning("No vmap_c found in {Map}", entry.MapName);
                continue;
            }

            var mapPath = vmaps[0].GetFullPath();

            // Record existing nodes before loading
            var existingNodes = new HashSet<SceneNode>(scene.AllNodes);

            var mapResourceName = mapPath.EndsWith(GameFileLoader.CompiledFileSuffix, StringComparison.OrdinalIgnoreCase)
                ? mapPath[..^GameFileLoader.CompiledFileSuffix.Length]
                : mapPath;
            var mapResource = fileLoader.LoadFileCompiled(mapResourceName) ?? throw new FileNotFoundException($"Failed to load map file '{mapResourceName}'.");
            var worldPath = WorldLoader.GetWorldNameFromMap(mapResourceName);
            var worldResource = fileLoader.LoadFileCompiled(worldPath) ?? throw new FileNotFoundException($"Failed to load world file '{worldPath}'.");
            var worldLoader = new WorldLoader((World)worldResource.DataBlock!, scene);
            worldLoader.ParallelPreloadResources(mapResource.ExternalReferences);
            worldLoader.LoadWorldNodes();

            // Apply translation offset to newly added nodes
            if (entry.Offset != Vector3.Zero)
            {
                var offsetTransform = Matrix4x4.CreateTranslation(entry.Offset);
                foreach (var node in scene.AllNodes)
                {
                    if (!existingNodes.Contains(node))
                    {
                        node.Transform *= offsetTransform;
                    }
                }
            }

            var totalNodes = scene.AllNodes.Count();
            logger.LogInformation("Loaded {Map}: {NewNodes} nodes added (total: {Total})",
                entry.MapName, totalNodes - existingNodes.Count, totalNodes);

            // Free the VPK file handle and collect garbage to reclaim Resource objects
            fileLoader.RemovePackageFromSearch(vpk);
            vpk.Dispose();
            GC.Collect();
        }

        // Draw landmark markers at each transition point
        const float markerHeight = 5000f;
        const float markerRadius = 200f;
        var markerColor = new Color32(255, 0, 255);
        var markerColorTop = new Color32(255, 255, 0);

        foreach (var entry in mapChain)
        {
            if (entry.LandmarkName == null)
            {
                continue;
            }

            var pos = entry.LandmarkWorldPos;

            // Vertical line shooting up
            scene.Add(new LineSceneNode(scene, pos, pos + new Vector3(0, 0, markerHeight), markerColor, markerColorTop), false);

            // 4 lines forming a cross around the landmark
            scene.Add(new LineSceneNode(scene, pos + new Vector3(-markerRadius, 0, 0), pos + new Vector3(markerRadius, 0, 0), markerColor, markerColor), false);
            scene.Add(new LineSceneNode(scene, pos + new Vector3(0, -markerRadius, 0), pos + new Vector3(0, markerRadius, 0), markerColor, markerColor), false);
            scene.Add(new LineSceneNode(scene, pos + new Vector3(-markerRadius, 0, markerRadius), pos + new Vector3(markerRadius, 0, markerRadius), markerColor, markerColor), false);
            scene.Add(new LineSceneNode(scene, pos + new Vector3(0, -markerRadius, markerRadius), pos + new Vector3(0, markerRadius, markerRadius), markerColor, markerColor), false);
        }

        // No skybox for multi-map view

        // Initialize scene (creates lighting buffers, octrees, etc.)
        SceneRenderer.Scene.Initialize();

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
