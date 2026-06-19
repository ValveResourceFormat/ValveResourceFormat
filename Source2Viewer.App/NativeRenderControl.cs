using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using Avalonia;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.OpenGL;
using Avalonia.OpenGL.Controls;
using Avalonia.Threading;
using Microsoft.Extensions.Logging.Abstractions;
using OpenTK;
using OpenTK.Graphics.OpenGL;
using SkiaSharp;
using SteamDatabase.ValvePak;
using ValveResourceFormat;
using ValveResourceFormat.Blocks;
using ValveResourceFormat.IO;
using ValveResourceFormat.Renderer;
using ValveResourceFormat.Renderer.Materials;
using ValveResourceFormat.Renderer.SceneEnvironment;
using ValveResourceFormat.Renderer.SceneNodes;
using ValveResourceFormat.Renderer.World;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.ResourceTypes.ModelAnimation;
using ValveResourceFormat.ResourceTypes.ModelAnimation2;
using ValveResourceFormat.Serialization.KeyValues;
using ValveResourceFormat.Utils;
using VrfRenderer = ValveResourceFormat.Renderer.Renderer;
using WorldResource = ValveResourceFormat.ResourceTypes.World;

namespace Source2Viewer.App;

public sealed class NativeRenderControl : OpenGlControlBase
{
    private static bool BindingsLoaded;
    private readonly Lock renderLock = new();
    private readonly HashSet<Key> pressedKeys = [];
    private RenderRequest? pendingRequest;
    private NativeRenderSession? session;
    private bool clearRequested;
    private bool resetCameraRequested;
    private bool wireframe;
    private bool orbitDrag;
    private bool panDrag;
    private bool lookDrag;
    private Point? previousPointer;
    private Vector2 pendingMouseDelta;
    private float pendingWheelDelta;
    private string renderMode = "Default";
    private bool gridVisible = true;
    private bool skyboxVisible = true;
    private bool fogVisible = true;
    private bool colorCorrectionEnabled = true;
    private bool occlusionCullingEnabled = true;
    private bool gpuCullingEnabled = true;
    private bool depthPrepassEnabled;
    private bool experimentalLightsEnabled;
    private bool toolMaterialsVisible;
    private bool occludedBoundsVisible;
    private bool staticOctreeVisible;
    private bool dynamicOctreeVisible;
    private bool animationPaused;
    private float animationSpeed = 1f;
    private bool rootMotionEnabled;
    private bool modelStatsVisible;
    private string? lastError;
    private string? pendingScreenshotPath;

    public event Action<IReadOnlyList<string>>? MapCamerasChanged;
    public event Action<IReadOnlyList<string>>? AnimationsChanged;
    public event Action<IReadOnlyList<string>, IReadOnlySet<string>>? MeshGroupsChanged;
    public event Action<IReadOnlyList<string>, string?>? MaterialGroupsChanged;
    public event Action<IReadOnlyList<string>>? LodsChanged;
    public event Action<IReadOnlyList<string>>? HitboxSetsChanged;
    public event Action<bool>? SkeletonChanged;
    public event Action<IReadOnlyList<string>, IReadOnlySet<string>>? WorldLayersChanged;
    public event Action<IReadOnlyList<string>, IReadOnlySet<string>>? PhysicsGroupsChanged;
    public event Action<IReadOnlyList<string>>? EntitiesChanged;
    public event Action<IReadOnlyList<string>>? RenderModesChanged;
    public event Action<string?>? ModelStatsChanged;
    public event Action<string?>? ViewportErrorChanged;
    public event Action<string>? ViewportStatusChanged;
    public event Action<string>? ScreenshotSaved;

    public NativeRenderControl()
    {
        Focusable = true;
        LostFocus += OnControlLostFocus;
    }

    public void LoadLooseFile(string path)
    {
        SetPending(RenderRequest.ForLooseFile(path));
    }

    public void LoadPackageEntry(Package package, string packagePath, PackageEntry entry)
    {
        SetPending(RenderRequest.ForPackageEntry(package, packagePath, entry));
    }

    public void SaveScreenshot(string path)
    {
        using var _ = renderLock.EnterScope();
        pendingScreenshotPath = path;
        RequestNextFrameRendering();
    }

    public void ClearResource()
    {
        using var _ = renderLock.EnterScope();
        pendingRequest = null;
        clearRequested = true;
        lastError = null;
        NotifyMapCamerasChanged([]);
        NotifyAnimationsChanged([]);
        NotifyMeshGroupsChanged([], new HashSet<string>());
        NotifyMaterialGroupsChanged([], null);
        NotifyLodsChanged([]);
        NotifyHitboxSetsChanged([]);
        NotifySkeletonChanged(false);
        NotifyWorldLayersChanged([], new HashSet<string>());
        NotifyPhysicsGroupsChanged([], new HashSet<string>());
        NotifyEntitiesChanged([]);
        NotifyRenderModesChanged([]);
        NotifyModelStatsChanged(null);
        NotifyViewportErrorChanged(null);
        RequestNextFrameRendering();
    }

    public void ResetCamera()
    {
        using var _ = renderLock.EnterScope();
        resetCameraRequested = true;
        RequestNextFrameRendering();
    }

    public void SetWireframe(bool enabled)
    {
        using var _ = renderLock.EnterScope();
        wireframe = enabled;
        session?.SetWireframe(enabled);
        RequestNextFrameRendering();
    }

    public void SetRenderMode(string mode)
    {
        using var _ = renderLock.EnterScope();
        renderMode = mode;
        session?.SetRenderMode(mode);
        RequestNextFrameRendering();
    }

    public void SetGridVisible(bool visible)
    {
        using var _ = renderLock.EnterScope();
        gridVisible = visible;
        session?.SetGridVisible(visible);
        RequestNextFrameRendering();
    }

    public void SetSkyboxVisible(bool visible)
    {
        using var _ = renderLock.EnterScope();
        skyboxVisible = visible;
        session?.SetSkyboxVisible(visible);
        RequestNextFrameRendering();
    }

    public void SetFogVisible(bool visible)
    {
        using var _ = renderLock.EnterScope();
        fogVisible = visible;
        session?.SetFogVisible(visible);
        RequestNextFrameRendering();
    }

    public void SetColorCorrectionEnabled(bool enabled)
    {
        using var _ = renderLock.EnterScope();
        colorCorrectionEnabled = enabled;
        session?.SetColorCorrectionEnabled(enabled);
        RequestNextFrameRendering();
    }

    public void SetOcclusionCullingEnabled(bool enabled)
    {
        using var _ = renderLock.EnterScope();
        occlusionCullingEnabled = enabled;
        session?.SetOcclusionCullingEnabled(enabled);
        RequestNextFrameRendering();
    }

    public void SetGpuCullingEnabled(bool enabled)
    {
        using var _ = renderLock.EnterScope();
        gpuCullingEnabled = enabled;
        session?.SetGpuCullingEnabled(enabled);
        RequestNextFrameRendering();
    }

    public void SetDepthPrepassEnabled(bool enabled)
    {
        using var _ = renderLock.EnterScope();
        depthPrepassEnabled = enabled;
        session?.SetDepthPrepassEnabled(enabled);
        RequestNextFrameRendering();
    }

    public void SetExperimentalLightsEnabled(bool enabled)
    {
        using var _ = renderLock.EnterScope();
        experimentalLightsEnabled = enabled;
        session?.SetExperimentalLightsEnabled(enabled);
        RequestNextFrameRendering();
    }

    public void SetToolMaterialsVisible(bool visible)
    {
        using var _ = renderLock.EnterScope();
        toolMaterialsVisible = visible;
        session?.SetToolMaterialsVisible(visible);
        RequestNextFrameRendering();
    }

    public void SetOccludedBoundsVisible(bool visible)
    {
        using var _ = renderLock.EnterScope();
        occludedBoundsVisible = visible;
        session?.SetOccludedBoundsVisible(visible);
        RequestNextFrameRendering();
    }

    public void SetStaticOctreeVisible(bool visible)
    {
        using var _ = renderLock.EnterScope();
        staticOctreeVisible = visible;
        session?.SetStaticOctreeVisible(visible);
        RequestNextFrameRendering();
    }

    public void SetDynamicOctreeVisible(bool visible)
    {
        using var _ = renderLock.EnterScope();
        dynamicOctreeVisible = visible;
        session?.SetDynamicOctreeVisible(visible);
        RequestNextFrameRendering();
    }

    public void SetMapCamera(int index)
    {
        using var _ = renderLock.EnterScope();
        session?.SetMapCamera(index);
        RequestNextFrameRendering();
    }

    public string? GetEntityDetails(int index)
    {
        using var _ = renderLock.EnterScope();
        return session?.GetEntityDetails(index);
    }

    public void FocusEntity(int index)
    {
        using var _ = renderLock.EnterScope();
        session?.FocusEntity(index);
        RequestNextFrameRendering();
    }

    public void SetAnimation(string? name)
    {
        using var _ = renderLock.EnterScope();
        session?.SetAnimation(name);
        RequestNextFrameRendering();
    }

    public void SetAnimationPaused(bool paused)
    {
        using var _ = renderLock.EnterScope();
        animationPaused = paused;
        session?.SetAnimationPaused(paused);
        RequestNextFrameRendering();
    }

    public void SetAnimationSpeed(float speed)
    {
        using var _ = renderLock.EnterScope();
        animationSpeed = speed;
        session?.SetAnimationSpeed(speed);
        RequestNextFrameRendering();
    }

    public void SetAnimationFrame(float fraction)
    {
        using var _ = renderLock.EnterScope();
        session?.SetAnimationFrame(fraction);
        RequestNextFrameRendering();
    }

    public void SetRootMotionEnabled(bool enabled)
    {
        using var _ = renderLock.EnterScope();
        rootMotionEnabled = enabled;
        session?.SetRootMotionEnabled(enabled);
        RequestNextFrameRendering();
    }

    public void SetModelStatsVisible(bool visible)
    {
        using var _ = renderLock.EnterScope();
        modelStatsVisible = visible;
        session?.SetModelStatsVisible(visible);
        NotifyModelStatsChanged(session?.ModelStatsText);
        RequestNextFrameRendering();
    }

    public void SetMeshGroups(IReadOnlyCollection<string> groups)
    {
        using var _ = renderLock.EnterScope();
        session?.SetMeshGroups(groups);
        NotifyModelStatsChanged(session?.ModelStatsText);
        RequestNextFrameRendering();
    }

    public void SetMaterialGroup(string group)
    {
        using var _ = renderLock.EnterScope();
        session?.SetMaterialGroup(group);
        NotifyModelStatsChanged(session?.ModelStatsText);
        RequestNextFrameRendering();
    }

    public void SetLod(int lod)
    {
        using var _ = renderLock.EnterScope();
        session?.SetLod(lod < 0 ? null : lod);
        NotifyModelStatsChanged(session?.ModelStatsText);
        RequestNextFrameRendering();
    }

    public void SetHitboxSet(string? name)
    {
        using var _ = renderLock.EnterScope();
        session?.SetHitboxSet(name);
        RequestNextFrameRendering();
    }

    public void SetSkeletonVisible(bool visible)
    {
        using var _ = renderLock.EnterScope();
        session?.SetSkeletonVisible(visible);
        RequestNextFrameRendering();
    }

    public void SetWorldLayers(IReadOnlyCollection<string> layers)
    {
        using var _ = renderLock.EnterScope();
        session?.SetWorldLayers(layers);
        RequestNextFrameRendering();
    }

    public void SetPhysicsGroups(IReadOnlyCollection<string> groups)
    {
        using var _ = renderLock.EnterScope();
        session?.SetPhysicsGroups(groups);
        RequestNextFrameRendering();
    }

    public void BeginPointerInput(Point position, bool left, bool right, bool middle)
    {
        using var _ = renderLock.EnterScope();
        SetPointerButtons(left, right, middle);
        previousPointer = position;
        RequestNextFrameRendering();
    }

    public void EndPointerInput(bool left, bool right, bool middle)
    {
        using var _ = renderLock.EnterScope();
        SetPointerButtons(left, right, middle);
        if (!orbitDrag && !lookDrag && !panDrag)
        {
            previousPointer = null;
        }

        RequestNextFrameRendering();
    }

    public void MovePointerInput(Point position)
    {
        using var _ = renderLock.EnterScope();
        if (previousPointer is not { } previous || (!orbitDrag && !lookDrag && !panDrag))
        {
            return;
        }

        pendingMouseDelta += new Vector2((float)(position.X - previous.X), (float)(position.Y - previous.Y));
        previousPointer = position;
        RequestNextFrameRendering();
    }

    public void WheelInput(float delta)
    {
        using var _ = renderLock.EnterScope();
        pendingWheelDelta += delta;
        RequestNextFrameRendering();
    }

    public void KeyInput(Key key, bool pressed)
    {
        using var _ = renderLock.EnterScope();
        if (pressed && key == Key.F)
        {
            resetCameraRequested = true;
            RequestNextFrameRendering();
            return;
        }

        if (pressed)
        {
            pressedKeys.Add(key);
        }
        else
        {
            pressedKeys.Remove(key);
        }

        RequestNextFrameRendering();
    }

    protected override void OnOpenGlInit(GlInterface gl)
    {
        if (!BindingsLoaded)
        {
            GL.LoadBindings(new AvaloniaBindingsContext(gl));
            BindingsLoaded = true;
        }
    }

    protected override void OnOpenGlDeinit(GlInterface gl)
    {
        using var _ = renderLock.EnterScope();
        session?.Dispose();
        session = null;
    }

    protected override void OnOpenGlRender(GlInterface gl, int fb)
    {
        var width = Math.Max(1, (int)Bounds.Width);
        var height = Math.Max(1, (int)Bounds.Height);

        using (renderLock.EnterScope())
        {
            if (clearRequested)
            {
                session?.Dispose();
                session = null;
                clearRequested = false;
            }

            if (pendingRequest != null)
            {
                try
                {
                    session?.Dispose();
                    session = NativeRenderSession.Load(pendingRequest, width, height);
                    session.SetWireframe(wireframe);
                    session.SetRenderMode(renderMode);
                    session.SetGridVisible(gridVisible);
                    session.SetSkyboxVisible(skyboxVisible);
                    session.SetFogVisible(fogVisible);
                    session.SetColorCorrectionEnabled(colorCorrectionEnabled);
                    session.SetOcclusionCullingEnabled(occlusionCullingEnabled);
                    session.SetGpuCullingEnabled(gpuCullingEnabled);
                    session.SetDepthPrepassEnabled(depthPrepassEnabled);
                    session.SetExperimentalLightsEnabled(experimentalLightsEnabled);
                    session.SetToolMaterialsVisible(toolMaterialsVisible);
                    session.SetOccludedBoundsVisible(occludedBoundsVisible);
                    session.SetStaticOctreeVisible(staticOctreeVisible);
                    session.SetDynamicOctreeVisible(dynamicOctreeVisible);
                    session.SetAnimationPaused(animationPaused);
                    session.SetAnimationSpeed(animationSpeed);
                    session.SetRootMotionEnabled(rootMotionEnabled);
                    session.SetModelStatsVisible(modelStatsVisible);
                    lastError = null;
                    NotifyViewportErrorChanged(null);
                    NotifyViewportStatusChanged(session.StatusText);
                    NotifyMapCamerasChanged(session.CameraNames);
                    NotifyAnimationsChanged(session.AnimationNames);
                    NotifyMeshGroupsChanged(session.MeshGroupNames, session.ActiveMeshGroupNames);
                    NotifyMaterialGroupsChanged(session.MaterialGroupNames, session.ActiveMaterialGroupName);
                    NotifyLodsChanged(session.LodNames);
                    NotifyHitboxSetsChanged(session.HitboxSetNames);
                    NotifySkeletonChanged(session.HasSkeleton);
                    NotifyWorldLayersChanged(session.WorldLayerNames, session.EnabledWorldLayerNames);
                    NotifyPhysicsGroupsChanged(session.PhysicsGroupNames, session.EnabledPhysicsGroupNames);
                    NotifyEntitiesChanged(session.EntitySummaries);
                    NotifyRenderModesChanged(session.RenderModeNames);
                    NotifyModelStatsChanged(session.ModelStatsText);
                }
                catch (Exception exception)
                {
                    session = null;
                    lastError = exception.Message;
                    _ = Console.Error.WriteLineAsync(exception.ToString());
                    NotifyViewportErrorChanged(lastError);
                    NotifyMapCamerasChanged([]);
                    NotifyAnimationsChanged([]);
                    NotifyMeshGroupsChanged([], new HashSet<string>());
                    NotifyMaterialGroupsChanged([], null);
                    NotifyLodsChanged([]);
                    NotifyHitboxSetsChanged([]);
                    NotifySkeletonChanged(false);
                    NotifyWorldLayersChanged([], new HashSet<string>());
                    NotifyPhysicsGroupsChanged([], new HashSet<string>());
                    NotifyEntitiesChanged([]);
                    NotifyRenderModesChanged([]);
                    NotifyModelStatsChanged(null);
                }
                finally
                {
                    pendingRequest = null;
                }
            }

            if (session != null)
            {
                if (resetCameraRequested)
                {
                    session.ResetCamera();
                    resetCameraRequested = false;
                }

                var input = ConsumeInput();
                session.Render(fb, width, height, input);
                if (pendingScreenshotPath != null)
                {
                    try
                    {
                        NativeRenderSession.SaveFramebufferPng(fb, width, height, pendingScreenshotPath);
                        NotifyViewportStatusChanged($"Saved screenshot to {pendingScreenshotPath}.");
                        NotifyScreenshotSaved(pendingScreenshotPath);
                    }
                    catch (Exception exception)
                    {
                        NotifyViewportErrorChanged($"Failed to save screenshot: {exception.Message}");
                    }
                    finally
                    {
                        pendingScreenshotPath = null;
                    }
                }
                RequestNextFrameRendering();
                return;
            }
        }

        ClearPlaceholder(fb, width, height, lastError != null);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        KeyInput(e.Key, pressed: true);
        e.Handled = true;
        base.OnKeyDown(e);
    }

    protected override void OnKeyUp(KeyEventArgs e)
    {
        KeyInput(e.Key, pressed: false);
        e.Handled = true;
        base.OnKeyUp(e);
    }

    private void OnControlLostFocus(object? sender, RoutedEventArgs e)
    {
        using var _ = renderLock.EnterScope();
        pressedKeys.Clear();
        orbitDrag = false;
        panDrag = false;
        lookDrag = false;
        previousPointer = null;
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        Focus();
        var point = e.GetCurrentPoint(this);
        BeginPointerInput(point.Position, point.Properties.IsLeftButtonPressed, point.Properties.IsRightButtonPressed, point.Properties.IsMiddleButtonPressed);
        e.Pointer.Capture(this);
        e.Handled = true;
        base.OnPointerPressed(e);
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        var point = e.GetCurrentPoint(this);
        EndPointerInput(point.Properties.IsLeftButtonPressed, point.Properties.IsRightButtonPressed, point.Properties.IsMiddleButtonPressed);
        e.Pointer.Capture(null);
        e.Handled = true;
        base.OnPointerReleased(e);
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        MovePointerInput(e.GetPosition(this));
        e.Handled = true;
        base.OnPointerMoved(e);
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        WheelInput((float)e.Delta.Y);
        e.Handled = true;
        base.OnPointerWheelChanged(e);
    }

    private void SetPending(RenderRequest request)
    {
        using var _ = renderLock.EnterScope();
        pendingRequest = request;
        lastError = null;
        RequestNextFrameRendering();
    }

    private void NotifyMapCamerasChanged(IReadOnlyList<string> cameras)
    {
        var snapshot = cameras.ToArray();
        Dispatcher.UIThread.Post(() => MapCamerasChanged?.Invoke(snapshot));
    }

    private void NotifyAnimationsChanged(IReadOnlyList<string> animations)
    {
        var snapshot = animations.ToArray();
        Dispatcher.UIThread.Post(() => AnimationsChanged?.Invoke(snapshot));
    }

    private void NotifyMeshGroupsChanged(IReadOnlyList<string> groups, IReadOnlySet<string> activeGroups)
    {
        var groupSnapshot = groups.ToArray();
        var activeSnapshot = activeGroups.ToHashSet(StringComparer.Ordinal);
        Dispatcher.UIThread.Post(() => MeshGroupsChanged?.Invoke(groupSnapshot, activeSnapshot));
    }

    private void NotifyMaterialGroupsChanged(IReadOnlyList<string> groups, string? activeGroup)
    {
        var groupSnapshot = groups.ToArray();
        Dispatcher.UIThread.Post(() => MaterialGroupsChanged?.Invoke(groupSnapshot, activeGroup));
    }

    private void NotifyLodsChanged(IReadOnlyList<string> lods)
    {
        var snapshot = lods.ToArray();
        Dispatcher.UIThread.Post(() => LodsChanged?.Invoke(snapshot));
    }

    private void NotifyHitboxSetsChanged(IReadOnlyList<string> hitboxSets)
    {
        var snapshot = hitboxSets.ToArray();
        Dispatcher.UIThread.Post(() => HitboxSetsChanged?.Invoke(snapshot));
    }

    private void NotifySkeletonChanged(bool hasSkeleton)
    {
        Dispatcher.UIThread.Post(() => SkeletonChanged?.Invoke(hasSkeleton));
    }

    private void NotifyWorldLayersChanged(IReadOnlyList<string> layers, IReadOnlySet<string> enabledLayers)
    {
        var layerSnapshot = layers.ToArray();
        var enabledSnapshot = enabledLayers.ToHashSet(StringComparer.Ordinal);
        Dispatcher.UIThread.Post(() => WorldLayersChanged?.Invoke(layerSnapshot, enabledSnapshot));
    }

    private void NotifyPhysicsGroupsChanged(IReadOnlyList<string> groups, IReadOnlySet<string> enabledGroups)
    {
        var groupSnapshot = groups.ToArray();
        var enabledSnapshot = enabledGroups.ToHashSet(StringComparer.Ordinal);
        Dispatcher.UIThread.Post(() => PhysicsGroupsChanged?.Invoke(groupSnapshot, enabledSnapshot));
    }

    private void NotifyEntitiesChanged(IReadOnlyList<string> entities)
    {
        var snapshot = entities.ToArray();
        Dispatcher.UIThread.Post(() => EntitiesChanged?.Invoke(snapshot));
    }

    private void NotifyRenderModesChanged(IReadOnlyList<string> modes)
    {
        var snapshot = modes.ToArray();
        Dispatcher.UIThread.Post(() => RenderModesChanged?.Invoke(snapshot));
    }

    private void NotifyModelStatsChanged(string? stats)
    {
        Dispatcher.UIThread.Post(() => ModelStatsChanged?.Invoke(stats));
    }

    private void NotifyViewportErrorChanged(string? message)
    {
        Dispatcher.UIThread.Post(() => ViewportErrorChanged?.Invoke(message));
    }

    private void NotifyViewportStatusChanged(string message)
    {
        Dispatcher.UIThread.Post(() => ViewportStatusChanged?.Invoke(message));
    }

    private void NotifyScreenshotSaved(string path)
    {
        Dispatcher.UIThread.Post(() => ScreenshotSaved?.Invoke(path));
    }

    private void SetPointerButtons(bool left, bool right, bool middle)
    {
        orbitDrag = left;
        lookDrag = right;
        panDrag = middle;
    }

    private ViewInput ConsumeInput()
    {
        var input = new ViewInput(
            pendingMouseDelta,
            pendingWheelDelta,
            orbitDrag,
            lookDrag,
            panDrag,
            pressedKeys.Contains(Key.W) || pressedKeys.Contains(Key.Up),
            pressedKeys.Contains(Key.S) || pressedKeys.Contains(Key.Down),
            pressedKeys.Contains(Key.A) || pressedKeys.Contains(Key.Left),
            pressedKeys.Contains(Key.D) || pressedKeys.Contains(Key.Right),
            pressedKeys.Contains(Key.Q),
            pressedKeys.Contains(Key.E),
            pressedKeys.Contains(Key.LeftShift) || pressedKeys.Contains(Key.RightShift));

        pendingMouseDelta = Vector2.Zero;
        pendingWheelDelta = 0f;
        return input;
    }

    private static void ClearPlaceholder(int fb, int width, int height, bool error)
    {
        GL.BindFramebuffer(FramebufferTarget.Framebuffer, fb);
        GL.Viewport(0, 0, width, height);

        if (error)
        {
            GL.ClearColor(0.18f, 0.03f, 0.04f, 1f);
        }
        else
        {
            GL.ClearColor(0.03f, 0.04f, 0.06f, 1f);
        }

        GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);
    }

    private readonly record struct ViewInput(
        Vector2 MouseDelta,
        float WheelDelta,
        bool OrbitDrag,
        bool LookDrag,
        bool PanDrag,
        bool Forward,
        bool Backward,
        bool Left,
        bool Right,
        bool Down,
        bool Up,
        bool Fast);

    private sealed class AvaloniaBindingsContext(GlInterface gl) : IBindingsContext
    {
        public IntPtr GetProcAddress(string procName)
        {
            return gl.GetProcAddress(procName);
        }
    }

    private sealed class RenderRequest
    {
        public string DisplayPath { get; private init; } = string.Empty;
        public string? LoosePath { get; private init; }
        public Package? Package { get; private init; }
        public string? PackagePath { get; private init; }
        public PackageEntry? Entry { get; private init; }

        public static RenderRequest ForLooseFile(string path)
        {
            return new RenderRequest
            {
                DisplayPath = path,
                LoosePath = path,
            };
        }

        public static RenderRequest ForPackageEntry(Package package, string packagePath, PackageEntry entry)
        {
            return new RenderRequest
            {
                DisplayPath = entry.GetFullPath(),
                Package = package,
                PackagePath = packagePath,
                Entry = entry,
            };
        }
    }

    private sealed class NativeRenderSession : IDisposable
    {
        private readonly GameFileLoader fileLoader;
        private readonly RendererContext rendererContext;
        private readonly VrfRenderer renderer;
        private readonly TextRenderer textRenderer;
        private readonly Resource resource;
        private readonly Framebuffer mainFramebuffer;
        private InfiniteGrid? grid;
        private readonly Stopwatch stopwatch = Stopwatch.StartNew();
        private readonly List<Matrix4x4> cameraMatrices = [];
        private readonly List<string> cameraNames = [];
        private readonly List<string> animationNames = [];
        private readonly List<string> meshGroupNames = [];
        private readonly List<string> materialGroupNames = [];
        private readonly List<string> lodNames = [];
        private readonly List<string> hitboxSetNames = [];
        private readonly List<string> worldLayerNames = [];
        private readonly List<string> physicsGroupNames = [];
        private readonly List<string> renderModeNames = [];
        private readonly List<string> entitySummaries = [];
        private readonly List<EntityLump.Entity> entities = [];
        private readonly HashSet<string> activeMeshGroupNames = new(StringComparer.Ordinal);
        private readonly HashSet<string> enabledWorldLayerNames = new(StringComparer.Ordinal);
        private readonly HashSet<string> enabledPhysicsGroupNames = new(StringComparer.Ordinal);
        private long lastUpdate = Stopwatch.GetTimestamp();
        private Vector3 cameraTarget;
        private bool cameraSetByWorld;
        private string? activeMaterialGroupName;
        private bool staticOctreeVisible;
        private bool dynamicOctreeVisible;
        private bool animationPaused;
        private float animationSpeed = 1f;
        private bool rootMotionEnabled;
        private bool rootMotionInitialized;
        private Vector3 lastRootMotionPosition;
        private bool modelStatsVisible;

        private NativeRenderSession(
            GameFileLoader fileLoader,
            RendererContext rendererContext,
            VrfRenderer renderer,
            TextRenderer textRenderer,
            Resource resource,
            Framebuffer mainFramebuffer)
        {
            this.fileLoader = fileLoader;
            this.rendererContext = rendererContext;
            this.renderer = renderer;
            this.textRenderer = textRenderer;
            this.resource = resource;
            this.mainFramebuffer = mainFramebuffer;
        }

        public IReadOnlyList<string> CameraNames => cameraNames;
        public IReadOnlyList<string> AnimationNames => animationNames;
        public IReadOnlyList<string> MeshGroupNames => meshGroupNames;
        public IReadOnlySet<string> ActiveMeshGroupNames => activeMeshGroupNames;
        public IReadOnlyList<string> MaterialGroupNames => materialGroupNames;
        public string? ActiveMaterialGroupName => activeMaterialGroupName;
        public IReadOnlyList<string> LodNames => lodNames;
        public IReadOnlyList<string> HitboxSetNames => hitboxSetNames;
        public bool HasSkeleton => renderer.Scene.AllNodes.OfType<SkeletonSceneNode>().Any();
        public IReadOnlyList<string> WorldLayerNames => worldLayerNames;
        public IReadOnlySet<string> EnabledWorldLayerNames => enabledWorldLayerNames;
        public IReadOnlyList<string> PhysicsGroupNames => physicsGroupNames;
        public IReadOnlySet<string> EnabledPhysicsGroupNames => enabledPhysicsGroupNames;
        public IReadOnlyList<string> RenderModeNames => renderModeNames;
        public IReadOnlyList<string> EntitySummaries => entitySummaries;
        public string? ModelStatsText { get; private set; }
        public string StatusText { get; private set; } = string.Empty;

        public static NativeRenderSession Load(RenderRequest request, int width, int height)
        {
            var fileLoader = request.Package != null
                ? new GameFileLoader(request.Package, request.PackagePath)
                : new GameFileLoader(null, request.LoosePath);

            var resource = ReadResource(request);
            var rendererContext = new RendererContext(fileLoader, NullLogger.Instance);
            var renderer = new VrfRenderer(rendererContext);
            var textRenderer = new TextRenderer(rendererContext, renderer.Camera);
            var mainFramebuffer = CreateMainFramebuffer(width, height);

            var session = new NativeRenderSession(fileLoader, rendererContext, renderer, textRenderer, resource, mainFramebuffer);
            session.InitializeScene(request.DisplayPath);
            return session;
        }

        public static void SaveFramebufferPng(int framebuffer, int width, int height, string path)
        {
            var pixels = new byte[width * height * 4];
            GL.BindFramebuffer(FramebufferTarget.ReadFramebuffer, framebuffer);
            GL.ReadPixels(0, 0, width, height, PixelFormat.Bgra, PixelType.UnsignedByte, pixels);

            using var bitmap = new SKBitmap(width, height, SKColorType.Bgra8888, SKAlphaType.Unpremul);
            var dest = bitmap.GetPixels();
            var stride = width * 4;
            for (var y = 0; y < height; y++)
            {
                Marshal.Copy(pixels, (height - 1 - y) * stride, IntPtr.Add(dest, y * stride), stride);
            }

            using var image = SKImage.FromBitmap(bitmap);
            using var data = image.Encode(SKEncodedImageFormat.Png, 100);
            using var output = File.Create(path);
            data.SaveTo(output);
        }

        public void Render(int outputFramebuffer, int width, int height, ViewInput input)
        {
            Resize(width, height);

            var current = Stopwatch.GetTimestamp();
            var frameTime = MathF.Min(1f, (float)Stopwatch.GetElapsedTime(lastUpdate, current).TotalSeconds);
            lastUpdate = current;

            ApplyInput(input, frameTime);
            ApplyRootMotion();

            renderer.Uptime = (float)stopwatch.Elapsed.TotalSeconds;
            renderer.DeltaTime = frameTime;
            renderer.Camera.SetViewportSize(width, height);
            renderer.Camera.RecalculateMatrices();

            var updateContext = new Scene.UpdateContext
            {
                Camera = renderer.Camera,
                TextRenderer = textRenderer,
                Timestep = frameTime,
            };

            renderer.Update(updateContext);
            renderer.Render(mainFramebuffer);
            if (renderer.Scene.OcclusionDebugEnabled && renderer.Scene.OcclusionDebug != null)
            {
                renderer.Scene.OcclusionDebug.Render();
            }

            mainFramebuffer.Bind(FramebufferTarget.Framebuffer);
            if (staticOctreeVisible)
            {
                renderer.Scene.StaticOctree.DebugRenderer?.Render();
            }

            if (dynamicOctreeVisible)
            {
                renderer.Scene.DynamicOctree.DebugRenderer?.Render();
            }

            if (grid != null)
            {
                grid.Render();
            }

            var output = Framebuffer.WrapExisting(outputFramebuffer, width, height);
            renderer.PostprocessRender(mainFramebuffer, output);
        }

        public void ResetCamera()
        {
            FrameCamera();
        }

        public void SetMapCamera(int index)
        {
            if ((uint)index >= (uint)cameraMatrices.Count)
            {
                return;
            }

            SetCamera(cameraMatrices[index]);
        }

        public string? GetEntityDetails(int index)
        {
            if ((uint)index >= (uint)entities.Count)
            {
                return null;
            }

            var entity = entities[index];
            var output = new StringBuilder();
            output.AppendLine("Entity");
            output.AppendLine($"Class: {entity.GetStringProperty("classname", "<unknown>")}");
            var targetname = entity.GetStringProperty("targetname", string.Empty);
            if (!string.IsNullOrEmpty(targetname))
            {
                output.AppendLine($"Name: {targetname}");
            }

            output.AppendLine();
            foreach (var (key, value) in entity.OrderBy(static pair => pair.Key, StringComparer.OrdinalIgnoreCase))
            {
                output.AppendLine($"{key}: {value}");
            }

            if (entity.Connections is { Count: > 0 })
            {
                output.AppendLine();
                output.AppendLine("Connections:");
                foreach (var connection in entity.Connections)
                {
                    output.AppendLine(connection.ToString());
                }
            }

            return output.ToString();
        }

        public void FocusEntity(int index)
        {
            if ((uint)index >= (uint)entities.Count)
            {
                return;
            }

            var entity = entities[index];
            if (entity.GetStringProperty("classname", string.Empty) == "worldspawn")
            {
                return;
            }

            var node = renderer.Scene.Find(entity) ?? renderer.SkyboxScene?.Find(entity);
            if (node != null)
            {
                FocusBoundingBox(node.BoundingBox);
                return;
            }

            var position = EntityTransformHelper.CalculateTransformationMatrix(entity).Translation;
            cameraTarget = position;
            renderer.Camera.SetLocation(position + new Vector3(128, 64, 128));
            renderer.Camera.LookAt(position);
        }

        public void SetAnimation(string? name)
        {
            if (name == null)
            {
                foreach (var modelNode in GetModelNodes())
                {
                    modelNode.SetAnimation(null);
                }

                rootMotionInitialized = false;
                return;
            }

            SetAnimation(renderer.Scene, name);
            if (renderer.SkyboxScene != null)
            {
                SetAnimation(renderer.SkyboxScene, name);
            }

            ApplyAnimationPlayback();
            rootMotionInitialized = false;
        }

        public void SetAnimationPaused(bool paused)
        {
            animationPaused = paused;
            ApplyAnimationPlayback();
        }

        public void SetAnimationSpeed(float speed)
        {
            animationSpeed = speed;
            ApplyAnimationPlayback();
        }

        public void SetAnimationFrame(float fraction)
        {
            foreach (var modelNode in GetModelNodes())
            {
                var animation = modelNode.AnimationController.ActiveAnimation;
                if (animation == null)
                {
                    continue;
                }

                modelNode.AnimationController.Frame = (int)(Math.Clamp(fraction, 0f, 1f) * Math.Max(animation.FrameCount - 1, 0));
            }

            rootMotionInitialized = false;
        }

        public void SetRootMotionEnabled(bool enabled)
        {
            rootMotionEnabled = enabled;
            rootMotionInitialized = false;
        }

        public void SetModelStatsVisible(bool visible)
        {
            modelStatsVisible = visible;
            ModelStatsText = visible ? GetModelStatsText() : null;
        }

        public void SetMeshGroups(IReadOnlyCollection<string> groups)
        {
            activeMeshGroupNames.Clear();
            activeMeshGroupNames.UnionWith(groups);

            foreach (var modelNode in GetModelNodes())
            {
                modelNode.SetActiveMeshGroups(activeMeshGroupNames);
            }

            SetModelStatsVisible(modelStatsVisible);
        }

        public void SetMaterialGroup(string group)
        {
            activeMaterialGroupName = group;

            foreach (var modelNode in GetModelNodes())
            {
                if (modelNode.GetMaterialGroups().Contains(group, StringComparer.OrdinalIgnoreCase))
                {
                    modelNode.SetMaterialGroup(group);
                }
            }

            SetModelStatsVisible(modelStatsVisible);
        }

        public void SetLod(int? lod)
        {
            foreach (var modelNode in GetModelNodes())
            {
                modelNode.SetActiveLod(lod);
            }

            SetModelStatsVisible(modelStatsVisible);
        }

        public void SetHitboxSet(string? name)
        {
            foreach (var hitboxSetNode in renderer.Scene.AllNodes.OfType<HitboxSetSceneNode>())
            {
                hitboxSetNode.SetHitboxSet(name);
            }
        }

        public void SetSkeletonVisible(bool visible)
        {
            foreach (var skeletonNode in renderer.Scene.AllNodes.OfType<SkeletonSceneNode>())
            {
                skeletonNode.Enabled = visible;
            }
        }

        public void SetWorldLayers(IReadOnlyCollection<string> layers)
        {
            enabledWorldLayerNames.Clear();
            enabledWorldLayerNames.UnionWith(layers);
            renderer.Scene.SetEnabledLayers(enabledWorldLayerNames);
            renderer.SkyboxScene?.SetEnabledLayers(enabledWorldLayerNames);
            renderer.Scene.UpdateOctrees();
            renderer.SkyboxScene?.UpdateOctrees();
        }

        public void SetPhysicsGroups(IReadOnlyCollection<string> groups)
        {
            enabledPhysicsGroupNames.Clear();
            enabledPhysicsGroupNames.UnionWith(groups);

            foreach (var physNode in renderer.Scene.AllNodes.OfType<PhysSceneNode>())
            {
                physNode.Enabled = enabledPhysicsGroupNames.Contains(physNode.PhysGroupName);
            }

            renderer.Scene.UpdateOctrees();
        }

        public void SetWireframe(bool enabled)
        {
            renderer.IsWireframe = enabled;
        }

        public void SetRenderMode(string mode)
        {
            if (renderer.ViewBuffer?.Data == null)
            {
                return;
            }

            renderer.ViewBuffer.Data.RenderMode = RenderModes.GetShaderId(mode);
            renderer.Postprocess.Enabled = renderer.ViewBuffer.Data.RenderMode == 0;
            renderer.Scene.EnableCompaction = mode != "Meshlets";

            foreach (var node in renderer.Scene.AllNodes)
            {
                node.SetRenderMode(mode);
            }

            if (renderer.SkyboxScene != null)
            {
                foreach (var node in renderer.SkyboxScene.AllNodes)
                {
                    node.SetRenderMode(mode);
                }
            }
        }

        public void SetGridVisible(bool visible)
        {
            grid = visible ? (grid ?? new InfiniteGrid(renderer.Scene)) : null;
        }

        public void SetSkyboxVisible(bool visible)
        {
            renderer.ShowSkybox = visible;
        }

        public void SetFogVisible(bool visible)
        {
            renderer.Scene.FogEnabled = visible;
            if (renderer.SkyboxScene != null)
            {
                renderer.SkyboxScene.FogEnabled = visible;
            }
        }

        public void SetColorCorrectionEnabled(bool enabled)
        {
            renderer.Postprocess.ColorCorrectionEnabled = enabled;
        }

        public void SetOcclusionCullingEnabled(bool enabled)
        {
            renderer.Scene.EnableOcclusionCulling = enabled;
            if (renderer.SkyboxScene != null)
            {
                renderer.SkyboxScene.EnableOcclusionCulling = enabled;
            }
        }

        public void SetGpuCullingEnabled(bool enabled)
        {
            renderer.Scene.EnableIndirectDraws = enabled;
            if (renderer.SkyboxScene != null)
            {
                renderer.SkyboxScene.EnableIndirectDraws = enabled;
            }
        }

        public void SetDepthPrepassEnabled(bool enabled)
        {
            renderer.Scene.EnableDepthPrepass = enabled;
            if (renderer.SkyboxScene != null)
            {
                renderer.SkyboxScene.EnableDepthPrepass = enabled;
            }
        }

        public void SetExperimentalLightsEnabled(bool enabled)
        {
            if (renderer.ViewBuffer?.Data != null)
            {
                renderer.ViewBuffer.Data.ExperimentalLightsEnabled = enabled;
            }
        }

        public void SetToolMaterialsVisible(bool visible)
        {
            renderer.Scene.ShowToolsMaterials = visible;
            if (renderer.SkyboxScene != null)
            {
                renderer.SkyboxScene.ShowToolsMaterials = visible;
            }
        }

        public void SetOccludedBoundsVisible(bool visible)
        {
            renderer.Scene.OcclusionDebugEnabled = visible;
        }

        public void SetStaticOctreeVisible(bool visible)
        {
            staticOctreeVisible = visible;
            if (!visible)
            {
                return;
            }

            renderer.Scene.StaticOctree.DebugRenderer ??= new(renderer.Scene.StaticOctree, rendererContext, false);
            renderer.Scene.StaticOctree.DebugRenderer.StaticBuild();
        }

        public void SetDynamicOctreeVisible(bool visible)
        {
            dynamicOctreeVisible = visible;
            if (!visible)
            {
                return;
            }

            renderer.Scene.DynamicOctree.DebugRenderer ??= new(renderer.Scene.DynamicOctree, rendererContext, true);
        }

        public void Dispose()
        {
            mainFramebuffer.Delete();
            renderer.Dispose();
            resource.Dispose();
            rendererContext.Dispose();
            fileLoader.Dispose();
        }

        private static Resource ReadResource(RenderRequest request)
        {
            var resource = new Resource { FileName = request.DisplayPath };

            if (request.Package != null && request.Entry != null)
            {
                var stream = GameFileLoader.GetPackageEntryStream(request.Package, request.Entry);
                resource.Read(stream, verifyFileSize: false);
                return resource;
            }

            if (request.LoosePath == null)
            {
                throw new InvalidOperationException("No resource path was supplied.");
            }

            resource.Read(request.LoosePath);
            return resource;
        }

        private void InitializeScene(string displayPath)
        {
            textRenderer.Load();
            renderer.Postprocess.Load(1);
            renderer.Initialize();
            renderer.MainFramebuffer = mainFramebuffer;

            GLEnvironment.Initialize(rendererContext.Logger);
            GLEnvironment.SetDefaultRenderState();

            renderer.LoadRendererResources();
            LoadResource(displayPath);
            CollectAnimationNames();
            CollectMeshGroups();
            CollectMaterialGroups();
            CollectLods();
            CollectHitboxSets();
            CollectWorldControls();
            CollectRenderModes();

            renderer.Scene.Initialize();
            renderer.SkyboxScene?.Initialize();
            UpdateStatusText();

            if (!cameraSetByWorld)
            {
                FrameCamera();
            }
            else
            {
                SetTargetFromSceneCenter();
            }
        }

        private void LoadResource(string displayPath)
        {
            if (resource.ResourceType == ResourceType.Map)
            {
                LoadMap(displayPath);
            }
            else if (resource.ResourceType == ResourceType.WorldVisibility
                && resource.GetBlockByType(BlockType.VXVS) is VoxelVisibility voxelVisibility)
            {
                renderer.Scene.Add(new VisibilitySceneNode(renderer.Scene, voxelVisibility)
                {
                    LayerName = "Visibility clusters",
                }, false);
            }
            else
            {
                switch (resource.DataBlock)
                {
                    case Model model:
                        LoadDefaultLighting();
                        LoadModel(model);
                        break;
                    case Mesh mesh:
                        LoadDefaultLighting();
                        renderer.Scene.Add(new MeshSceneNode(renderer.Scene, mesh, 0), true);
                        break;
                    case Material:
                        if (resource.DataBlock is Material { ShaderName: "sky.vfx" })
                        {
                            renderer.Skybox2D = new SceneSkybox2D(rendererContext.MaterialLoader.LoadMaterial(resource));
                        }
                        else
                        {
                            LoadDefaultLighting();
                            LoadMaterialPreview();
                        }
                        break;
                    case PhysAggregateData phys:
                        LoadDefaultLighting();
                        foreach (var node in PhysSceneNode.CreatePhysSceneNodes(renderer.Scene, phys, displayPath))
                        {
                            renderer.Scene.Add(node, false);
                        }
                        break;
                    case ParticleSystem particleSystem:
                        LoadDefaultLighting();
                        renderer.ViewBuffer!.Data!.ExperimentalLightsEnabled = true;
                        renderer.Scene.LightingInfo.UseSceneBoundsForSunLightFrustum = false;
                        renderer.Scene.Add(new ParticleSceneNode(renderer.Scene, particleSystem, null, preview: true), true);
                        break;
                    case SmartProp smartProp:
                        LoadDefaultLighting();
                        LoadSmartProp(smartProp);
                        break;
                    case AnimationClip clip:
                        LoadAnimationClip(clip);
                        break;
                    case BinaryKV3 skeletonData when resource.ResourceType == ResourceType.NmSkeleton:
                        LoadSkeleton(Skeleton.FromSkeletonData(skeletonData.Data), null);
                        break;
                    case WorldResource world:
                        LoadWorld(world, resource.ExternalReferences);
                        break;
                    case WorldNode worldNode:
                        LoadDefaultLighting();
                        new WorldNodeLoader(rendererContext, worldNode, resource.ExternalReferences).Load(renderer.Scene);
                        break;
                    default:
                        throw new NotSupportedException($"Rendering {resource.ResourceType} is not wired in the native viewport yet.");
                }
            }

            var post = new ScenePostProcessVolume(renderer.Scene)
            {
                HasBloom = true,
                IsMaster = true,
            };
            renderer.Scene.PostProcessInfo.AddPostProcessVolume(post);
        }

        private void LoadModel(Model model)
        {
            var modelNode = new ModelSceneNode(renderer.Scene, model);
            renderer.Scene.Add(modelNode, true);
            DisableStandaloneOverlayMaterials(modelNode);

            if (model.Skeleton.Bones.Length > 0)
            {
                renderer.Scene.Add(new SkeletonSceneNode(renderer.Scene, modelNode.AnimationController, model.Skeleton), true);
            }

            if (model.HitboxSets is { Count: > 0 })
            {
                renderer.Scene.Add(new HitboxSetSceneNode(renderer.Scene, modelNode.AnimationController, model.HitboxSets), true);
            }

            var phys = model.GetEmbeddedPhys();
            phys ??= model.GetReferencedPhysNames()
                .Select(name => rendererContext.FileLoader.LoadFileCompiled(name)?.DataBlock)
                .OfType<PhysAggregateData>()
                .FirstOrDefault();

            if (phys == null)
            {
                return;
            }

            foreach (var physNode in PhysSceneNode.CreatePhysSceneNodes(renderer.Scene, phys, null))
            {
                physNode.Enabled = modelNode.RenderableMeshes.Count == 0;
                physNode.IsTranslucentRenderMode = false;
                renderer.Scene.Add(physNode, false);
            }
        }

        private void LoadAnimationClip(AnimationClip clip)
        {
            var skeletonResource = rendererContext.FileLoader.LoadFileCompiled(clip.SkeletonName);
            if (skeletonResource?.DataBlock is not BinaryKV3 skeletonData)
            {
                return;
            }

            LoadSkeleton(Skeleton.FromSkeletonData(skeletonData.Data), clip);
        }

        private void LoadSkeleton(Skeleton skeleton, AnimationClip? clip)
        {
            var animationController = new AnimationController(skeleton, []);
            if (clip != null)
            {
                animationController.SetAnimation(new Animation(clip));
            }

            var skeletonNode = new SkeletonSceneNode(renderer.Scene, animationController, skeleton)
            {
                Enabled = true,
            };

            renderer.Scene.Add(skeletonNode, true);
            skeletonNode.Update(new Scene.UpdateContext
            {
                Camera = renderer.Camera,
                TextRenderer = textRenderer,
                Timestep = 0f,
            });
        }

        private void LoadSmartProp(SmartProp smartProp)
        {
            foreach (var child in smartProp.Data.Root.GetArray("m_Children") ?? [])
            {
                LoadSmartPropChild(child);
            }
        }

        private void LoadSmartPropChild(ValveKeyValue.KVObject child)
        {
            switch (child.GetStringProperty("_class"))
            {
                case "CSmartPropElement_Model":
                    var modelName = child.GetStringProperty("m_sModelName");
                    if (string.IsNullOrEmpty(modelName))
                    {
                        return;
                    }

                    using (var resource = rendererContext.FileLoader.LoadFileCompiled(modelName))
                    {
                        if (resource?.DataBlock is Model model)
                        {
                            LoadModel(model);
                        }
                    }
                    break;
                case "CSmartPropElement_Group":
                case "CSmartPropElement_PickOne":
                    foreach (var nestedChild in child.GetArray("m_Children") ?? [])
                    {
                        LoadSmartPropChild(nestedChild);
                    }
                    break;
            }
        }

        private static void DisableStandaloneOverlayMaterials(ModelSceneNode modelNode)
        {
            if (modelNode.RenderableMeshes.Count != 1)
            {
                return;
            }

            var mesh = modelNode.RenderableMeshes[0];
            if (mesh.DrawCallsOverlay.Count == 0 || mesh.DrawCallsOpaque.Count != 0 || mesh.DrawCallsBlended.Count != 0)
            {
                return;
            }

            foreach (var drawCall in mesh.DrawCallsOverlay)
            {
                drawCall.Material.IsOverlay = false;
            }
        }

        private static void SetAnimation(Scene scene, string name)
        {
            foreach (var modelNode in scene.AllNodes.OfType<ModelSceneNode>())
            {
                if (modelNode.Animations.ContainsKey(name))
                {
                    modelNode.SetAnimationByName(name);
                }
            }
        }

        private void ApplyAnimationPlayback()
        {
            foreach (var modelNode in GetModelNodes())
            {
                modelNode.AnimationController.IsPaused = animationPaused;
                modelNode.AnimationController.FrametimeMultiplier = animationSpeed;
            }
        }

        private void ApplyRootMotion()
        {
            if (!rootMotionEnabled)
            {
                return;
            }

            var modelNode = renderer.Scene.AllNodes.OfType<ModelSceneNode>()
                .FirstOrDefault(static node => node.AnimationController.AnimationFrame is { });
            if (modelNode?.AnimationController.AnimationFrame is not Frame animationFrame)
            {
                return;
            }

            if (!rootMotionInitialized)
            {
                lastRootMotionPosition = animationFrame.Movement.Position;
                rootMotionInitialized = true;
                return;
            }

            var rootMotionDelta = animationFrame.Movement.Position - lastRootMotionPosition;
            if (rootMotionDelta == Vector3.Zero)
            {
                return;
            }

            modelNode.Transform = modelNode.Transform with
            {
                Translation = modelNode.Transform.Translation + rootMotionDelta,
            };
            renderer.Camera.Location += rootMotionDelta;
            cameraTarget += rootMotionDelta;
            lastRootMotionPosition = animationFrame.Movement.Position;
            renderer.Scene.MarkParentOctreeDirty(modelNode);
        }

        private void CollectAnimationNames()
        {
            animationNames.Clear();
            animationNames.AddRange(GetModelNodes()
                .SelectMany(static node => node.Animations.Keys)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Order(StringComparer.OrdinalIgnoreCase));
        }

        private void CollectMeshGroups()
        {
            meshGroupNames.Clear();
            meshGroupNames.AddRange(GetModelNodes()
                .SelectMany(static node => node.GetMeshGroups())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Order(StringComparer.OrdinalIgnoreCase));

            activeMeshGroupNames.Clear();
            activeMeshGroupNames.UnionWith(GetModelNodes().SelectMany(static node => node.GetActiveMeshGroups()));
        }

        private void CollectMaterialGroups()
        {
            materialGroupNames.Clear();
            materialGroupNames.AddRange(GetModelNodes()
                .SelectMany(static node => node.GetMaterialGroups())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Order(StringComparer.OrdinalIgnoreCase));
            activeMaterialGroupName = GetModelNodes().FirstOrDefault()?.ActiveMaterialGroup;
        }

        private void CollectLods()
        {
            lodNames.Clear();
            var lodInfo = GetModelNodes()
                .Select(static node => node.LodInfo)
                .FirstOrDefault(static lodInfo => lodInfo.HasDistinctLevels);
            if (lodInfo == null)
            {
                return;
            }

            lodNames.Add("Auto");
            for (var level = 0; level < lodInfo.LevelCount; level++)
            {
                lodNames.Add(lodInfo.AvailableLevels.Contains(level) ? $"LOD {level}" : $"LOD {level} (empty)");
            }
        }

        private void CollectHitboxSets()
        {
            hitboxSetNames.Clear();
            hitboxSetNames.AddRange(renderer.Scene.AllNodes
                .OfType<HitboxSetSceneNode>()
                .SelectMany(static node => node.HitboxSets)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Order(StringComparer.OrdinalIgnoreCase));
        }

        private void LoadMaterialPreview()
        {
            var material = rendererContext.MaterialLoader.LoadMaterial(resource);
            var planeMesh = MeshSceneNode.CreateMaterialPreviewQuad(renderer.Scene, material, new Vector2(32));
            renderer.Scene.Add(planeMesh, false);
        }

        private void LoadMap(string displayPath)
        {
            var loadedWorld = WorldLoader.LoadMap(displayPath, renderer.Scene);
            renderer.SkyboxScene = loadedWorld.SkyboxScene;
            renderer.Skybox2D = loadedWorld.Skybox2D;
            NavMeshSceneNode.AddNavNodesToScene(loadedWorld.NavMesh, renderer.Scene);

            if (loadedWorld.CameraMatrices.Count > 0)
            {
                cameraNames.AddRange(loadedWorld.CameraNames);
                cameraMatrices.AddRange(loadedWorld.CameraMatrices);
                SetCamera(cameraMatrices[0]);
            }

            SetDefaultWorldLayers(loadedWorld.DefaultEnabledLayers);
            SetEntitySummaries(loadedWorld.Entities);
        }

        private void LoadWorld(WorldResource world, ValveResourceFormat.Blocks.ResourceExtRefList? externalReferences)
        {
            var loadedWorld = new WorldLoader(world, renderer.Scene);
            loadedWorld.Load(externalReferences);
            renderer.SkyboxScene = loadedWorld.SkyboxScene;
            renderer.Skybox2D = loadedWorld.Skybox2D;
            NavMeshSceneNode.AddNavNodesToScene(loadedWorld.NavMesh, renderer.Scene);

            if (loadedWorld.CameraMatrices.Count > 0)
            {
                cameraNames.AddRange(loadedWorld.CameraNames);
                cameraMatrices.AddRange(loadedWorld.CameraMatrices);
                SetCamera(cameraMatrices[0]);
            }

            SetDefaultWorldLayers(loadedWorld.DefaultEnabledLayers);
            SetEntitySummaries(loadedWorld.Entities);
        }

        private void SetEntitySummaries(IEnumerable<EntityLump.Entity> sourceEntities)
        {
            entities.Clear();
            entities.AddRange(sourceEntities);
            entitySummaries.Clear();
            entitySummaries.AddRange(entities
                .Select(static entity =>
                {
                    var classname = entity.GetStringProperty("classname", "<unknown>");
                    var targetname = entity.GetStringProperty("targetname", string.Empty);
                    return string.IsNullOrEmpty(targetname) ? classname : $"{classname} - {targetname}";
                })
                .Take(1000));
        }

        private void SetCamera(Matrix4x4 matrix)
        {
            renderer.Camera.SetFromTransformMatrix(matrix);
            renderer.Camera.ClampRotation();
            renderer.Camera.RecalculateDirectionVectors();
            cameraSetByWorld = true;
            SetTargetFromSceneCenter();
        }

        private void SetDefaultWorldLayers(HashSet<string> layers)
        {
            enabledWorldLayerNames.Clear();
            enabledWorldLayerNames.UnionWith(layers);
            renderer.Scene.SetEnabledLayers(enabledWorldLayerNames);
            renderer.SkyboxScene?.SetEnabledLayers(enabledWorldLayerNames);
        }

        private void CollectWorldControls()
        {
            worldLayerNames.Clear();
            worldLayerNames.AddRange(renderer.Scene.AllNodes
                .Select(static node => node.LayerName)
                .OfType<string>()
                .Where(static name => !name.StartsWith("Internal -", StringComparison.Ordinal))
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal));

            if (worldLayerNames.Count > 0 && enabledWorldLayerNames.Count == 0)
            {
                enabledWorldLayerNames.UnionWith(worldLayerNames);
            }

            physicsGroupNames.Clear();
            physicsGroupNames.AddRange(renderer.Scene.AllNodes
                .OfType<PhysSceneNode>()
                .Select(static node => node.PhysGroupName)
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal));

            enabledPhysicsGroupNames.Clear();
            enabledPhysicsGroupNames.UnionWith(renderer.Scene.AllNodes
                .OfType<PhysSceneNode>()
                .Where(static node => node.Enabled)
                .Select(static node => node.PhysGroupName));
        }

        private void CollectRenderModes()
        {
            renderModeNames.Clear();
            renderModeNames.AddRange(renderer.Scene.AllNodes
                .Concat(renderer.SkyboxScene?.AllNodes ?? [])
                .SelectMany(static node => node.GetSupportedRenderModes())
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal));
        }

        private IEnumerable<ModelSceneNode> GetModelNodes()
        {
            foreach (var node in renderer.Scene.AllNodes.OfType<ModelSceneNode>())
            {
                yield return node;
            }

            if (renderer.SkyboxScene == null)
            {
                yield break;
            }

            foreach (var node in renderer.SkyboxScene.AllNodes.OfType<ModelSceneNode>())
            {
                yield return node;
            }
        }

        private void UpdateStatusText()
        {
            var nodes = renderer.Scene.AllNodes.ToList();
            var meshes = nodes
                .OfType<MeshCollectionNode>()
                .SelectMany(static node => node.RenderableMeshes)
                .ToList();
            var drawCalls = meshes.Sum(static mesh => mesh.DrawCalls.Count());

            StatusText = $"Loaded {nodes.Count:N0} nodes, {meshes.Count:N0} meshes, {drawCalls:N0} draw calls.";
        }

        private string? GetModelStatsText()
        {
            var modelNode = GetModelNodes().FirstOrDefault();
            if (modelNode == null)
            {
                return null;
            }

            var sb = new StringBuilder();
            sb.AppendLine(CultureInfo.InvariantCulture, $"Mesh Count: {modelNode.RenderableMeshes.Count}");

            foreach (var mesh in modelNode.RenderableMeshes)
            {
                var vertexTotal = 0L;
                var triangleTotal = 0L;
                var vertexBufferSize = 0L;
                var indexBufferSize = 0L;

                foreach (var draw in mesh.DrawCalls)
                {
                    vertexTotal += draw.VertexCount;
                    triangleTotal += draw.IndexCount / 3;
                    vertexBufferSize += draw.VertexCount * draw.VertexBuffers.Sum(static vb => vb.ElementSizeInBytes);
                    indexBufferSize += draw.IndexCount * draw.IndexSizeInBytes;
                }

                var size = mesh.BoundingBox.Max - mesh.BoundingBox.Min;
                sb.AppendLine(CultureInfo.InvariantCulture, $"{mesh.Name.Split(':')[^1]}");
                sb.AppendLine(CultureInfo.InvariantCulture, $"  Vertices:  {vertexTotal:N0} | {FormatBytes(vertexBufferSize)}");
                sb.AppendLine(CultureInfo.InvariantCulture, $"  Triangles: {triangleTotal:N0} | {FormatBytes(indexBufferSize)}");
                sb.AppendLine(CultureInfo.InvariantCulture, $"  Drawcalls: {mesh.DrawCalls.Count()}");
                sb.AppendLine(CultureInfo.InvariantCulture, $"  Size: X {size.X:0.##} | Y {size.Y:0.##} | Z {size.Z:0.##}");
            }

            return sb.ToString();
        }

        private static string FormatBytes(long value)
        {
            string[] units = ["B", "KiB", "MiB", "GiB"];
            var readable = (double)value;
            var unit = 0;
            while (readable >= 1024 && unit < units.Length - 1)
            {
                readable /= 1024;
                unit++;
            }

            return $"{readable:0.##} {units[unit]}";
        }

        private void LoadDefaultLighting()
        {
            var rendererAssembly = Assembly.GetAssembly(typeof(RendererContext)) ?? throw new InvalidOperationException("Failed to get renderer assembly.");
            using var stream = rendererAssembly.GetManifestResourceStream("Renderer.Resources.sky_furnace.vtex_c")
                ?? throw new InvalidOperationException("Failed to load default environment map.");
            using var ibl = new Resource { FileName = "sky_furnace.vtex_c" };
            ibl.Read(stream);
            VrfRenderer.LoadDefaultLighting(renderer.Scene, ibl);
        }

        private void FrameCamera()
        {
            var nodes = GetCameraFrameNodes();
            if (nodes.Count == 0)
            {
                renderer.Camera.SetLocation(new Vector3(256));
                renderer.Camera.LookAt(Vector3.Zero);
                return;
            }

            var bbox = nodes[0].BoundingBox;
            foreach (var node in nodes.Skip(1))
            {
                bbox = bbox.Union(node.BoundingBox);
            }

            FocusBoundingBox(bbox);
        }

        private void FocusBoundingBox(AABB bbox)
        {
            cameraTarget = bbox.Center;
            var size = bbox.Size;
            var distance = MathF.Max(MathF.Max(size.X, size.Y), size.Z) * 1.2f;
            distance = Math.Clamp(distance, 64f, 2000f);
            renderer.Camera.SetLocation(new Vector3(bbox.Center.X + distance, bbox.Center.Y + size.Y * 2f, bbox.Center.Z + distance));
            renderer.Camera.LookAt(bbox.Center);
        }

        private void SetTargetFromSceneCenter()
        {
            var nodes = GetCameraFrameNodes();
            if (nodes.Count == 0)
            {
                cameraTarget = renderer.Camera.Location + renderer.Camera.Forward * 1024f;
                return;
            }

            var bbox = nodes[0].BoundingBox;
            foreach (var node in nodes.Skip(1))
            {
                bbox = bbox.Union(node.BoundingBox);
            }

            cameraTarget = bbox.Center;
        }

        private List<SceneNode> GetCameraFrameNodes()
        {
            var visibleNodes = renderer.Scene.AllNodes
                .Where(static node => node.LayerEnabled)
                .ToList();

            return visibleNodes.Count > 0
                ? visibleNodes
                : renderer.Scene.AllNodes.ToList();
        }

        private void ApplyInput(ViewInput input, float frameTime)
        {
            var camera = renderer.Camera;
            camera.RecalculateDirectionVectors();

            var distance = MathF.Max(1f, Vector3.Distance(camera.Location, cameraTarget));
            var panScale = distance * 0.0015f;

            if (input.OrbitDrag && input.MouseDelta != Vector2.Zero)
            {
                ApplyLookDelta(camera, input.MouseDelta);
                camera.ClampRotation();
                camera.RecalculateDirectionVectors();
                camera.Location = cameraTarget - camera.Forward * distance;
            }
            else if (input.LookDrag && input.MouseDelta != Vector2.Zero)
            {
                ApplyLookDelta(camera, input.MouseDelta);
                camera.ClampRotation();
                camera.RecalculateDirectionVectors();
                cameraTarget = camera.Location + camera.Forward * distance;
            }
            else if (input.PanDrag && input.MouseDelta != Vector2.Zero)
            {
                var pan = (-camera.Right * input.MouseDelta.X + camera.Up * input.MouseDelta.Y) * panScale;
                cameraTarget += pan;
                camera.Location += pan;
            }

            if (input.WheelDelta != 0f)
            {
                var zoom = MathF.Pow(0.85f, input.WheelDelta);
                distance = Math.Clamp(distance * zoom, 1f, 1_000_000f);
                camera.Location = cameraTarget - camera.Forward * distance;
            }

            var movement = Vector3.Zero;
            if (input.Forward)
            {
                movement += camera.Forward;
            }
            if (input.Backward)
            {
                movement -= camera.Forward;
            }
            if (input.Right)
            {
                movement += camera.Right;
            }
            if (input.Left)
            {
                movement -= camera.Right;
            }
            if (input.Up)
            {
                movement += Vector3.UnitZ;
            }
            if (input.Down)
            {
                movement -= Vector3.UnitZ;
            }

            if (movement != Vector3.Zero)
            {
                movement = Vector3.Normalize(movement) * MathF.Max(64f, distance) * frameTime * (input.Fast ? 4f : 1f);
                camera.Location += movement;
                cameraTarget += movement;
            }
        }

        private static void ApplyLookDelta(Camera camera, Vector2 mouseDelta)
        {
            const float Sensitivity = 0.008f;
            camera.Yaw -= mouseDelta.X * Sensitivity;
            camera.Pitch -= mouseDelta.Y * Sensitivity;
        }

        private void Resize(int width, int height)
        {
            mainFramebuffer.Resize(width, height, 1);
            renderer.Camera.SetViewportSize(width, height);
        }

        private static Framebuffer CreateMainFramebuffer(int width, int height)
        {
            var framebuffer = Framebuffer.Prepare(nameof(NativeRenderControl),
                Math.Max(1, width),
                Math.Max(1, height),
                1,
                new(PixelInternalFormat.Rgba16f, PixelFormat.Rgba, PixelType.HalfFloat),
                Framebuffer.DepthAttachmentFormat.Depth32FStencil8);

            var status = framebuffer.Initialize();
            if (status != FramebufferErrorCode.FramebufferComplete)
            {
                throw new InvalidOperationException($"Framebuffer failed to initialize: {status}");
            }

            framebuffer.ClearMask |= ClearBufferMask.StencilBufferBit;
            return framebuffer;
        }
    }
}
