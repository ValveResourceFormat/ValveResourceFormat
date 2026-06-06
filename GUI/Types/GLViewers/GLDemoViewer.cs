using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Linq;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using GUI.Controls;
using GUI.Types.Viewers;
using GUI.Utils;
using OpenTK.Graphics.OpenGL;
using SkiaSharp;
using Svg.Skia;
using ValveResourceFormat.Blocks;
using ValveResourceFormat.DemoPlayback;
using ValveResourceFormat.Renderer;
using ValveResourceFormat.Renderer.Input;
using ValveResourceFormat.Renderer.SceneNodes;
using ValveResourceFormat.Renderer.Utils;
using ValveResourceFormat.ResourceTypes;

namespace GUI.Types.GLViewers
{
    class GLDemoViewer : GLWorldViewer
    {
        private const string DemoLayerName = "Demo Playback";
        private const int DemoBottomHudHeight = CsDemoBottomHudControl.PreferredHeight;
        private const string DeathMarkerPath = "panorama/images/hud/radar/mapoverview/icon-death.vsvg";
        private const string KillMarkerPath = "panorama/images/icons/ui/crosshair.vsvg";
        private const float ThirdPersonDefaultFollowDistance = 110f;
        private const float ThirdPersonMinFollowDistance = 45f;
        private const float ThirdPersonMaxFollowDistance = 320f;
        private const float ThirdPersonZoomStep = 18f;
        private const float ThirdPersonFollowTargetHeight = 54f;
        private const float ThirdPersonDefaultPitch = -10f * MathF.PI / 180f;

        private enum DemoCameraMode
        {
            Free,
            FirstPerson,
            ThirdPerson,
        }

        private readonly CsDemoPlayback playback;
        private readonly RadarOverview? radar;
        private readonly CancellationTokenSource cancellationTokenSource = new();
        private CsDemoPlayerSceneManager? playerSceneManager;
        private CsDemoEffectSceneManager? effectSceneManager;
        private Task<CsDemoFrame>? pendingFrameTask;
        private bool pendingFrameIsSeek;
        private CsDemoFrame interpolationPreviousFrame;
        private CsDemoFrame interpolationNextFrame;
        private CsDemoTopHudControl? topHud;
        private CsDemoBottomHudControl? bottomHud;
        private DemoHudOverlay? topOverlay;
        private SKBitmap? hudBitmap;
        private SKBitmap? deathMarkerBitmap;
        private SKBitmap? killMarkerBitmap;
        private string? lastHudSignature;
        private bool mouseConsumedByHud;
        private bool playing;
        private double playbackTick;
        private float speed = 1f;
        private ulong? selectedSteamId;
        private DemoCameraMode cameraMode = DemoCameraMode.Free;
        private float thirdPersonOrbitYawOffset;
        private float thirdPersonOrbitPitch = ThirdPersonDefaultPitch;
        private float thirdPersonFollowDistance = ThirdPersonDefaultFollowDistance;
        private int lastAppliedTick = -1;
        private string? lastError;
        private bool launchOptionsApplied;
        private bool launchCameraApplied;
        private bool hasLaunchCamera;
        private Vector3 launchCameraPos;
        private float launchCameraPitch;
        private float launchCameraYaw;
        private float lastFrameTime;
        private bool voxelBoxEnabled;
        private bool animDebugEnabled;
        private CsDemoPlayerAnimDebugTracker animDebugTracker = new();
        private float animDebugClock;

        public GLDemoViewer(VrfGuiContext vrfGuiContext, RendererContext rendererContext, CsDemoPlayback playback, World world, RadarOverview? radar = null, ResourceExtRefList? externalReferences = null)
            : base(vrfGuiContext, rendererContext, world, externalReferences)
        {
            this.playback = playback;
            this.radar = radar;
            playbackTick = playback.CurrentFrame.Tick;
            interpolationPreviousFrame = playback.CurrentFrame;
            interpolationNextFrame = playback.CurrentFrame;
        }

        public bool TryExecuteCommand(IReadOnlyList<string> args)
        {
            if (args.Count == 0)
            {
                return false;
            }

            AgentDebugLog.Write(
                "H14",
                "GUI/Types/GLViewers/GLDemoViewer.cs:TryExecuteCommand",
                "demo command received",
                new
                {
                    command = string.Join(" ", args),
                    currentTick = playback.CurrentFrame.Tick,
                    playbackTick,
                    playing,
                    cameraMode = cameraMode.ToString(),
                    selectedSteamId,
                });

            switch (args[0].ToLowerInvariant())
            {
                case "play":
                    SetPlaying(true);
                    return true;
                case "pause":
                    SetPlaying(false);
                    return true;
                case "toggle":
                    TogglePlayback();
                    return true;
                case "seek" when args.Count >= 2 && int.TryParse(args[1], out var tick):
                    SeekToTick(tick);
                    return true;
                case "speed" when args.Count >= 2 && float.TryParse(args[1], out var newSpeed):
                    SetSpeed(newSpeed);
                    return true;
                case "anim-debug" when args.Count >= 2:
                    SetAnimDebug(ParseOnOff(args[1]));
                    return true;
                case "follow" when args.Count >= 2 && ulong.TryParse(args[1], out var steamId):
                    if (args.Count >= 3)
                    {
                        return TrySetPlayerCameraCommand(steamId, args[2]);
                    }

                    SelectPlayerCamera(steamId);
                    return true;
                case "follow-view" when args.Count >= 2:
                case "camera-view" when args.Count >= 2:
                case "third-person-view" when args.Count >= 2:
                    return TrySetThirdPersonViewCommand(args[1]);
                case "free-camera":
                    selectedSteamId = null;
                    cameraMode = DemoCameraMode.Free;
                    UpdateDemoUi(playback.CurrentFrame);
                    return true;
                default:
                    return false;
            }
        }

        public override void Dispose()
        {
            cancellationTokenSource.Cancel();

            if (pendingFrameTask is { IsCompleted: false })
            {
                try
                {
                    pendingFrameTask.Wait(TimeSpan.FromMilliseconds(250));
                }
                catch (AggregateException)
                {
                    // Cancellation/fault is observed below on normal update path.
                }
            }

            cancellationTokenSource.Dispose();
            playback.Dispose();
            topHud?.Dispose();
            bottomHud?.Dispose();
            topOverlay?.Dispose();
            hudBitmap?.Dispose();
            deathMarkerBitmap?.Dispose();
            killMarkerBitmap?.Dispose();
            radar?.Dispose();
            playerSceneManager?.Dispose();
            effectSceneManager?.Dispose();

            base.Dispose();
        }

        protected override void AddUiControls()
        {
            Debug.Assert(UiControl != null);

            base.AddUiControls();

            InitializeHudControls();
            UpdateDemoUi(playback.CurrentFrame);
        }

        protected override bool SkipIntroCameraNudge =>
            !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("VRF_DEMO_MAP_CAMERA_NAME"))
            || !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("VRF_DEMO_MAP_CAMERA_INDEX"));

        private void InitializeHudControls()
        {
            Debug.Assert(UiControl != null);

            deathMarkerBitmap = LoadPanoramaSvg(DeathMarkerPath, 32);
            killMarkerBitmap = LoadPanoramaSvg(KillMarkerPath, 32);

            bottomHud = new CsDemoBottomHudControl
            {
                Summary = playback.Summary,
                Frame = playback.CurrentFrame,
                RoundNumber = GetRoundNumber(playback.CurrentFrame.Tick),
                Speed = speed,
                Playing = playing,
                SelectedSteamId = selectedSteamId,
                DeathMarkerImage = deathMarkerBitmap,
                KillMarkerImage = killMarkerBitmap,
            };
            bottomHud.TogglePlaybackRequested += TogglePlayback;
            bottomHud.SkipSecondsRequested += SkipSeconds;
            bottomHud.SkipRoundRequested += SkipRound;
            bottomHud.SpeedRequested += SetSpeed;
            bottomHud.SeekTickRequested += SeekToTick;
            bottomHud.VoxelBoxChanged += OnVoxelBoxChanged;
            bottomHud.AnimDebugChanged += OnAnimDebugChanged;

            topHud = new CsDemoTopHudControl
            {
                Summary = playback.Summary,
                Frame = playback.CurrentFrame,
                RoundNumber = GetRoundNumber(playback.CurrentFrame.Tick),
                SelectedSteamId = selectedSteamId,
                Radar = radar,
            };
            topHud.PlayerClicked += SelectPlayerCamera;

            // The HUD controls are not added to the form: the GL viewport fills the whole window
            // and the HUD is composited into the GL frame as semi-transparent overlays (see
            // RenderHudOverlay) so the map shows through, like the CS2 in-game demo viewer.
        }

        protected override void OnPaint(float frameTime)
        {
            base.OnPaint(frameTime);
            RenderHudOverlay();
        }

        private void RenderHudOverlay()
        {
            if (topHud == null || bottomHud == null || GLDefaultFramebuffer == null || GLControl == null)
            {
                return;
            }

            var width = GLControl.ClientSize.Width;
            var height = GLControl.ClientSize.Height;

            if (width <= 0 || height <= 0)
            {
                return;
            }

            topOverlay ??= new DemoHudOverlay(Scene.RendererContext);

            var frame = GetDisplayFrame();
            var roundNumber = GetRoundNumber(frame.Tick);

            topHud.Summary = playback.Summary;
            topHud.Frame = frame;
            topHud.RoundNumber = roundNumber;
            topHud.SelectedSteamId = selectedSteamId;
            bottomHud.Summary = playback.Summary;
            bottomHud.Frame = frame;
            bottomHud.RoundNumber = roundNumber;
            bottomHud.Speed = speed;
            bottomHud.Playing = playing;
            bottomHud.SelectedSteamId = selectedSteamId;
            bottomHud.CameraModeLabel = GetCameraModeLabel();
            bottomHud.VoxelBoxEnabled = voxelBoxEnabled;
            bottomHud.AnimDebugEnabled = animDebugEnabled;
            var blindAlpha = GetSelectedBlindAlpha(frame);

            var topHeight = topHud.Height;
            const int bottomHeight = DemoBottomHudHeight;

            var signature = string.Create(System.Globalization.CultureInfo.InvariantCulture,
                $"{width}x{height}|{frame.Tick}|{roundNumber}|{selectedSteamId}|{playing}|{speed}|{topHeight}|{GetCameraModeLabel()}|{blindAlpha}|{bottomHud.HoverSignature}|{bottomHud.DebugSignature}");

            if (signature != lastHudSignature || hudBitmap == null || animDebugEnabled)
            {
                lastHudSignature = signature;

                hudBitmap = EnsureHudBitmap(hudBitmap, width, height);
                using var canvas = new SKCanvas(hudBitmap);
                canvas.Clear(SKColors.Transparent);

                if (blindAlpha > 0)
                {
                    using var flashPaint = new SKPaint
                    {
                        Color = new SKColor(255, 255, 255, (byte)blindAlpha),
                        Style = SKPaintStyle.Fill,
                    };
                    canvas.DrawRect(0, 0, width, height, flashPaint);
                }

                topHud.Width = width;
                topHud.Height = topHeight;
                topHud.PaintHud(canvas, width, topHeight);

                canvas.Save();
                canvas.Translate(0, height - bottomHeight);
                bottomHud.Width = width;
                bottomHud.Height = bottomHeight;
                bottomHud.PaintHud(canvas, width, bottomHeight);
                canvas.Restore();

                if (animDebugEnabled)
                {
                    RenderAnimDebugOverlay(canvas, frame, width, height);
                }

                // Map sits on the topmost UI layer — painted after both bars so nothing can cover it.
                topHud.PaintMapOverlay(canvas);

                UploadBitmap(topOverlay, hudBitmap);
            }

            GLDefaultFramebuffer.Bind(FramebufferTarget.Framebuffer);
            GL.Viewport(0, 0, width, height);

            topOverlay.Render(0, 0, width, height);
        }

        private void RenderAnimDebugOverlay(SKCanvas canvas, CsDemoFrame frame, int width, int height)
        {
            if (playerSceneManager == null)
            {
                return;
            }

            animDebugClock += lastFrameTime;
            var debugData = playerSceneManager.GetAnimDebugData(frame, Renderer.Camera.Location, selectedSteamId);
            animDebugTracker.Update(debugData, animDebugClock);
            var panels = animDebugTracker.GetVisiblePanels(animDebugClock);
            CsDemoAnimDebugOverlay.Draw(canvas, Renderer.Camera, width, height, panels);
        }

        private void OnAnimDebugChanged(bool enabled)
        {
            animDebugEnabled = enabled;
            lastHudSignature = null;
        }

        private static SKBitmap EnsureHudBitmap(SKBitmap? bitmap, int width, int height)
        {
            if (bitmap != null && bitmap.Width == width && bitmap.Height == height)
            {
                return bitmap;
            }

            bitmap?.Dispose();
            return new SKBitmap(width, height, SKColorType.Bgra8888, SKAlphaType.Unpremul);
        }

        private static void UploadBitmap(DemoHudOverlay overlay, SKBitmap bitmap)
        {
            if (!bitmap.CanCopyTo(SKColorType.Bgra8888))
            {
                using var converted = bitmap.Copy(SKColorType.Bgra8888);
                UploadBitmap(overlay, converted);
                return;
            }

            var info = bitmap.Info;
            var bytes = new byte[info.RowBytes * info.Height];
            Marshal.Copy(bitmap.GetPixels(), bytes, 0, bytes.Length);
            overlay.Upload(bytes, info.Width, info.Height);
        }

        private SKBitmap? LoadPanoramaSvg(string filePath, int size)
        {
            var resource = GuiContext.LoadFileCompiled(filePath);
            if (resource?.DataBlock is not Panorama panorama)
            {
                Log.Warn(nameof(GLDemoViewer), $"Unable to load CS2 HUD marker '{filePath}_c'.");
                return null;
            }

            using var stream = new MemoryStream(panorama.Data);
            using var svg = new SKSvg();
            svg.Load(stream);
            return Themer.SvgToSkiaBitmap(svg, size, size);
        }

        protected override void OnKeyDown(object? sender, System.Windows.Forms.KeyEventArgs e)
        {
            switch (e.KeyCode)
            {
                case System.Windows.Forms.Keys.Space:
                    TogglePlayback();
                    break;
                case System.Windows.Forms.Keys.Left:
                    SkipSeconds(-15);
                    break;
                case System.Windows.Forms.Keys.Right:
                    SkipSeconds(15);
                    break;
                case System.Windows.Forms.Keys.Oemcomma:
                    SkipRound(-1);
                    break;
                case System.Windows.Forms.Keys.OemPeriod:
                    SkipRound(1);
                    break;
                case System.Windows.Forms.Keys.N:
                    ToggleAnimDebug();
                    break;
                default:
                    base.OnKeyDown(sender, e);
                    return;
            }

            e.Handled = true;
            e.SuppressKeyPress = true;
        }

        protected override void OnMouseDown(object? sender, MouseEventArgs e)
        {
            if (RouteHudClick(e))
            {
                mouseConsumedByHud = true;
                return;
            }

            mouseConsumedByHud = false;
            base.OnMouseDown(sender, e);
        }

        protected override void OnMouseUp(object? sender, MouseEventArgs e)
        {
            if (mouseConsumedByHud)
            {
                mouseConsumedByHud = false;
                return;
            }

            base.OnMouseUp(sender, e);
        }

        private void OnVoxelBoxChanged(bool enabled)
        {
            // Voxel overlay rendering is not implemented yet; toggle is UI-only for now.
            voxelBoxEnabled = enabled;
            lastHudSignature = null;
        }

        private void ToggleAnimDebug()
        {
            animDebugEnabled = !animDebugEnabled;
            if (bottomHud != null)
            {
                bottomHud.AnimDebugEnabled = animDebugEnabled;
            }

            lastHudSignature = null;
        }

        private bool TryGetViewportMouse(MouseEventArgs e, out int px, out int py, out int width, out int height)
        {
            px = 0;
            py = 0;
            width = 0;
            height = 0;

            if (GLControl == null || GLDefaultFramebuffer == null)
            {
                return false;
            }

            width = GLDefaultFramebuffer.Width;
            height = GLDefaultFramebuffer.Height;
            px = (int)(e.X * (float)width / Math.Max(1, GLControl.Width));
            py = (int)(e.Y * (float)height / Math.Max(1, GLControl.Height));
            return width > 0 && height > 0;
        }

        private bool RouteHudClick(MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left || topHud == null || bottomHud == null
                || GLControl == null || GLDefaultFramebuffer == null)
            {
                return false;
            }

            var width = GLDefaultFramebuffer.Width;
            var height = GLDefaultFramebuffer.Height;

            // Scale control-space click coordinates into framebuffer pixels (handles DPI scaling).
            var px = (int)(e.X * (float)width / Math.Max(1, GLControl.Width));
            var py = (int)(e.Y * (float)height / Math.Max(1, GLControl.Height));

            if (py < topHud.Height)
            {
                var topPoint = new Point(px, py);
                if (topHud.HandleClick(topPoint))
                {
                    return true;
                }

                // Consume clicks anywhere in the opaque top-HUD regions (score pill, map, hide button)
                // so they don't pass through to the 3D viewport (player selection, camera, etc.).
                if (topHud.IsBlockingClick(topPoint))
                {
                    return true;
                }
            }

            const int bottomHeight = DemoBottomHudHeight;
            if (py >= height - bottomHeight)
            {
                var bottomPoint = new Point(px, py - (height - bottomHeight));
                if (bottomHud.HandleClick(bottomPoint))
                {
                    return true;
                }

                // The whole bottom bar is opaque — consume every click in it.
                if (bottomHud.IsBlockingClick(bottomPoint))
                {
                    return true;
                }
            }

            return false;
        }

        protected override void OnMouseMove(object? sender, MouseEventArgs e)
        {
            RouteHudHover(e);

            base.OnMouseMove(sender, e);
        }

        private void RouteHudHover(MouseEventArgs e)
        {
            if (bottomHud == null || GLControl == null || GLDefaultFramebuffer == null)
            {
                return;
            }

            var width = GLDefaultFramebuffer.Width;
            var height = GLDefaultFramebuffer.Height;
            var px = (int)(e.X * (float)width / Math.Max(1, GLControl.Width));
            var py = (int)(e.Y * (float)height / Math.Max(1, GLControl.Height));
            var changed = py >= height - DemoBottomHudHeight
                ? bottomHud.HandleHover(new Point(px, py - (height - DemoBottomHudHeight)))
                : bottomHud.HandleHover(null);

            if (changed)
            {
                lastHudSignature = null;
            }
        }

        protected override void OnUpdate(float frameTime)
        {
            lastFrameTime = frameTime;
            base.OnUpdate(frameTime);
            ApplyLaunchOptions();
            ObservePendingFrameTask();

            if (playing && playback.Summary.TickCount > 0)
            {
                playbackTick = Math.Min(playback.Summary.TickCount, playbackTick + frameTime * CsDemoPlayback.TickRate * speed);

                if (playbackTick >= playback.Summary.TickCount)
                {
                    playing = false;
                }

                QueueFrameRequest((int)Math.Ceiling(playbackTick), seek: false);
            }

            var frame = GetDisplayFrame();
            EnsureDemoPlaybackLayerEnabled();
            ApplyFrame(frame);
            UpdateThirdPersonOrbitFromMouse();
            ApplySelectedCamera(frame);
            ApplyLaunchCamera();
            UpdateDemoUi(frame);

            if (lastError != null)
            {
                DrawLowerCornerText(lastError, Color32.Red);
            }
        }

        // Deterministic launch via environment variables.
        // VRF_DEMO_SEEK_TICK=<int> jumps to a known grenade-throw tick on the first frame;
        // VRF_DEMO_AUTOPLAY=1 starts playback automatically. Applied once, after UI init.
        private void ApplyLaunchOptions()
        {
            if (launchOptionsApplied)
            {
                return;
            }

            launchOptionsApplied = true;

            var seekTick = Environment.GetEnvironmentVariable("VRF_DEMO_SEEK_TICK");
            if (int.TryParse(seekTick, out var tick) && tick > 0)
            {
                SeekToTick(tick);
            }

            if (Environment.GetEnvironmentVariable("VRF_DEMO_AUTOPLAY") == "1")
            {
                playing = true;
                UpdateDemoUi(playback.CurrentFrame);
            }

            var follow = Environment.GetEnvironmentVariable("VRF_DEMO_FOLLOW_STEAMID");
            if (ulong.TryParse(follow, out var followSteamId) && followSteamId > 0)
            {
                SelectPlayerCamera(followSteamId);
            }

            // Free-camera look-at, for framing a specific logged grenade position from the harness.
            // VRF_DEMO_CAM_POS / VRF_DEMO_CAM_TARGET are "x,y,z" in Source world units.
            if (TryParseVector(Environment.GetEnvironmentVariable("VRF_DEMO_CAM_POS"), out var camPos)
                && TryParseVector(Environment.GetEnvironmentVariable("VRF_DEMO_CAM_TARGET"), out var camTarget))
            {
                var dir = Vector3.Normalize(camTarget - camPos);
                launchCameraPos = camPos;
                launchCameraYaw = MathF.Atan2(dir.Y, dir.X);
                launchCameraPitch = MathF.Asin(Math.Clamp(dir.Z, -1f, 1f));
                hasLaunchCamera = true;
            }

            // Map camera: VRF_DEMO_MAP_CAMERA_NAME + optional VRF_DEMO_MAP_CAMERA_OCCURRENCE (1-based among same name).
            // Example: point_camera occurrence 2 = second point_camera in the Map Camera dropdown.
            if (!launchCameraApplied && LoadedWorld != null && LoadedWorld.CameraMatrices.Count > 0)
            {
                var mapCameraName = Environment.GetEnvironmentVariable("VRF_DEMO_MAP_CAMERA_NAME");
                if (!string.IsNullOrWhiteSpace(mapCameraName))
                {
                    var occurrence = 1;
                    _ = int.TryParse(Environment.GetEnvironmentVariable("VRF_DEMO_MAP_CAMERA_OCCURRENCE"), out occurrence);
                    if (occurrence < 1)
                    {
                        occurrence = 1;
                    }

                    if (TrySetMapCameraByName(mapCameraName, occurrence))
                    {
                        launchCameraApplied = true;
                    }
                }
                else if (int.TryParse(Environment.GetEnvironmentVariable("VRF_DEMO_MAP_CAMERA_INDEX"), out var mapCameraComboIndex)
                    && mapCameraComboIndex > 0)
                {
                    SetMapCameraByIndex(mapCameraComboIndex - 1);
                    launchCameraApplied = true;
                }
            }
        }

        // Re-assert the harness launch camera every frame so it survives the world-load camera reset
        // (deterministic framing for screenshots). User follow-camera selection takes priority.
        private void ApplyLaunchCamera()
        {
            if (hasLaunchCamera && selectedSteamId == null)
            {
                SetDemoCamera(launchCameraPos, launchCameraPitch, launchCameraYaw);
            }
        }

        private static bool TryParseVector(string? text, out Vector3 value)
        {
            value = default;
            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            var parts = text.Split(',');
            if (parts.Length != 3
                || !float.TryParse(parts[0], out var x)
                || !float.TryParse(parts[1], out var y)
                || !float.TryParse(parts[2], out var z))
            {
                return false;
            }

            value = new Vector3(x, y, z);
            return true;
        }

        private void TogglePlayback()
        {
            lastError = null;

            if (playbackTick >= playback.Summary.TickCount)
            {
                SeekToTick(0);
            }

            playing = !playing;
            UpdateDemoUi(playback.CurrentFrame);
        }

        private void SetPlaying(bool enabled)
        {
            lastError = null;

            if (enabled && playbackTick >= playback.Summary.TickCount)
            {
                SeekToTick(0);
            }

            playing = enabled;
            RenderLoopThread.RequestRender();
            UpdateDemoUi(playback.CurrentFrame);

            AgentDebugLog.Write(
                "H14",
                "GUI/Types/GLViewers/GLDemoViewer.cs:SetPlaying",
                "play state changed",
                new
                {
                    enabled,
                    currentTick = playback.CurrentFrame.Tick,
                    playbackTick,
                    summaryTickCount = playback.Summary.TickCount,
                    pendingTaskRunning = pendingFrameTask is { IsCompleted: false },
                });
        }

        private void SkipSeconds(int seconds)
        {
            SeekToTick(playback.CurrentFrame.Tick + seconds * CsDemoPlayback.TickRate);
        }

        private void SkipRound(int direction)
        {
            if (playback.Summary.Rounds.Count == 0)
            {
                return;
            }

            var currentTick = playback.CurrentFrame.Tick;
            var currentIndex = playback.Summary.Rounds
                .Select((round, index) => (round, index))
                .LastOrDefault(item => item.round.StartTick <= currentTick)
                .index;
            var targetIndex = Math.Clamp(currentIndex + direction, 0, playback.Summary.Rounds.Count - 1);

            SeekToTick(playback.Summary.Rounds[targetIndex].StartTick);
        }

        private void SetSpeed(float newSpeed)
        {
            speed = Math.Clamp(newSpeed, 0.25f, 8f);
            UpdateDemoUi(playback.CurrentFrame);
        }

        private void SetAnimDebug(bool enabled)
        {
            animDebugEnabled = enabled;
            if (bottomHud != null)
            {
                bottomHud.AnimDebugEnabled = enabled;
            }

            lastHudSignature = null;
        }

        private static bool ParseOnOff(string value)
            => value.Equals("1", StringComparison.OrdinalIgnoreCase)
                || value.Equals("on", StringComparison.OrdinalIgnoreCase)
                || value.Equals("true", StringComparison.OrdinalIgnoreCase);

        private void SeekToTick(int tick)
        {
            lastError = null;
            playbackTick = Math.Clamp(tick, 0, playback.Summary.TickCount);
            interpolationPreviousFrame = playback.CurrentFrame;
            interpolationNextFrame = playback.CurrentFrame;
            // #region agent log
            AgentDebugLog.Write(
                "H2,H5",
                "GUI/Types/GLViewers/GLDemoViewer.cs:SeekToTick",
                "demo seek requested",
                new
                {
                    requestedTick = tick,
                    playbackTick,
                    currentFrameTick = playback.CurrentFrame.Tick,
                    lastAppliedTick,
                    playing,
                    cameraMode = cameraMode.ToString(),
                    pendingTaskRunning = pendingFrameTask is { IsCompleted: false },
                });
            // #endregion
            QueueFrameRequest((int)playbackTick, seek: true);
            UpdateDemoUi(playback.CurrentFrame);
        }

        private void SelectPlayerCamera(ulong steamId)
        {
            if (selectedSteamId != steamId)
            {
                selectedSteamId = steamId;
                cameraMode = DemoCameraMode.FirstPerson;
            }
            else
            {
                cameraMode = cameraMode switch
                {
                    DemoCameraMode.FirstPerson => DemoCameraMode.ThirdPerson,
                    DemoCameraMode.ThirdPerson => DemoCameraMode.Free,
                    _ => DemoCameraMode.FirstPerson,
                };

                if (cameraMode == DemoCameraMode.Free)
                {
                    selectedSteamId = null;
                }
            }

            UpdateDemoUi(playback.CurrentFrame);

            // #region agent log
            AgentDebugLog.Write(
                "H6",
                "GUI/Types/GLViewers/GLDemoViewer.cs:SelectPlayerCamera",
                "camera mode changed",
                new
                {
                    steamId,
                    cameraMode = cameraMode.ToString(),
                    note = cameraMode == DemoCameraMode.FirstPerson
                        ? "FP mode moves camera only; player model uses thirdperson mesh groups without FP arms"
                        : null,
                });
            // #endregion
        }

        private bool TrySetPlayerCameraCommand(ulong steamId, string view)
        {
            if (view.Equals("first", StringComparison.OrdinalIgnoreCase)
                || view.Equals("first-person", StringComparison.OrdinalIgnoreCase)
                || view.Equals("fp", StringComparison.OrdinalIgnoreCase))
            {
                selectedSteamId = steamId;
                cameraMode = DemoCameraMode.FirstPerson;
                UpdateDemoUi(playback.CurrentFrame);
                return true;
            }

            if (view.Equals("third", StringComparison.OrdinalIgnoreCase)
                || view.Equals("third-person", StringComparison.OrdinalIgnoreCase)
                || view.Equals("tp", StringComparison.OrdinalIgnoreCase))
            {
                selectedSteamId = steamId;
                cameraMode = DemoCameraMode.ThirdPerson;
                ResetThirdPersonView();
                UpdateDemoUi(playback.CurrentFrame);
                return true;
            }

            if (!TryGetThirdPersonViewYawOffset(view, out var yawOffset))
            {
                return false;
            }

            selectedSteamId = steamId;
            cameraMode = DemoCameraMode.ThirdPerson;
            thirdPersonOrbitYawOffset = yawOffset;
            thirdPersonOrbitPitch = ThirdPersonDefaultPitch;
            UpdateDemoUi(playback.CurrentFrame);

            AgentDebugLog.Write(
                "H6,H14",
                "GUI/Types/GLViewers/GLDemoViewer.cs:TrySetPlayerCameraCommand",
                "follow camera command applied",
                new
                {
                    steamId,
                    view,
                    yawOffset,
                    cameraMode = cameraMode.ToString(),
                    currentTick = playback.CurrentFrame.Tick,
                });

            return true;
        }

        private bool TrySetThirdPersonViewCommand(string view)
        {
            if (!TryGetThirdPersonViewYawOffset(view, out var yawOffset))
            {
                return false;
            }

            thirdPersonOrbitYawOffset = yawOffset;
            thirdPersonOrbitPitch = ThirdPersonDefaultPitch;

            if (selectedSteamId != null)
            {
                cameraMode = DemoCameraMode.ThirdPerson;
            }

            UpdateDemoUi(playback.CurrentFrame);
            return true;
        }

        private static bool TryGetThirdPersonViewYawOffset(string view, out float yawOffset)
        {
            yawOffset = 0f;

            switch (view.ToLowerInvariant())
            {
                case "back":
                case "behind":
                case "rear":
                case "reset":
                    yawOffset = 0f;
                    return true;
                case "front":
                    yawOffset = MathF.PI;
                    return true;
                case "left":
                case "side-left":
                    yawOffset = MathF.PI / 2f;
                    return true;
                case "right":
                case "side":
                case "side-right":
                    yawOffset = -MathF.PI / 2f;
                    return true;
                default:
                    return false;
            }
        }

        private void ResetThirdPersonView()
        {
            thirdPersonOrbitYawOffset = 0f;
            thirdPersonOrbitPitch = ThirdPersonDefaultPitch;
            thirdPersonFollowDistance = ThirdPersonDefaultFollowDistance;
        }

        private void QueueFrameRequest(int tick, bool seek)
        {
            if (pendingFrameTask is { IsCompleted: false })
            {
                // #region agent log
                AgentDebugLog.Write(
                    "H5",
                    "GUI/Types/GLViewers/GLDemoViewer.cs:QueueFrameRequestSkipped",
                    "frame request dropped because prior request still running",
                    new { tick, seek, currentFrameTick = playback.CurrentFrame.Tick });
                // #endregion
                return;
            }

            var targetTick = Math.Clamp(tick, 0, playback.Summary.TickCount);

            if (targetTick == playback.CurrentFrame.Tick)
            {
                // #region agent log
                AgentDebugLog.Write(
                    "H5",
                    "GUI/Types/GLViewers/GLDemoViewer.cs:QueueFrameRequestSkipped",
                    "frame request dropped because target equals CurrentFrame tick",
                    new { tick, seek, targetTick, currentFrameTick = playback.CurrentFrame.Tick });
                // #endregion
                return;
            }

            pendingFrameIsSeek = seek;
            pendingFrameTask = seek
                ? playback.SeekToTickAsync(targetTick, cancellationTokenSource.Token)
                : playback.AdvanceToTickAsync(targetTick, cancellationTokenSource.Token);
        }

        private void ObservePendingFrameTask()
        {
            var task = pendingFrameTask;

            if (task == null || !task.IsCompleted)
            {
                return;
            }

            pendingFrameTask = null;

            if (task.IsCanceled)
            {
                return;
            }

            if (task.IsFaulted)
            {
                var exception = task.Exception?.GetBaseException();
                lastError = exception?.Message ?? "Demo playback failed.";
                playing = false;
                Log.Error(nameof(GLDemoViewer), exception?.ToString() ?? lastError);
                return;
            }

            StoreInterpolationFrame(task.Result, pendingFrameIsSeek);
        }

        private void ApplyFrame(CsDemoFrame frame)
        {
            var postSeek = lastAppliedTick == -1;
            if (frame.Tick == lastAppliedTick && !playing)
            {
                // #region agent log
                AgentDebugLog.Write(
                    "H2,H8",
                    "GUI/Types/GLViewers/GLDemoViewer.cs:ApplyFrameSkipped",
                    "apply frame skipped because tick unchanged while paused",
                    new
                    {
                        frameTick = frame.Tick,
                        lastAppliedTick,
                        playing,
                        cameraMode = cameraMode.ToString(),
                    });
                // #endregion
                return;
            }

            playerSceneManager ??= new CsDemoPlayerSceneManager(
                Scene,
                GuiContext,
                playback.Summary.MapName,
                RendererContext.Logger);
            if (postSeek)
            {
                playerSceneManager.LogSeekState(frame.Tick);
            }

            playerSceneManager.ApplyFrame(frame, playing);

            effectSceneManager ??= new CsDemoEffectSceneManager(Scene, GuiContext);
            effectSceneManager.ApplyFrame(frame, playing ? speed : 0f);

            lastAppliedTick = frame.Tick;

            // #region agent log
            AgentDebugLog.Write(
                "H2,H8",
                "GUI/Types/GLViewers/GLDemoViewer.cs:ApplyFrame",
                "apply frame completed",
                new
                {
                    frameTick = frame.Tick,
                    playing,
                    postSeek,
                    cameraMode = cameraMode.ToString(),
                    selectedSteamId,
                    playerCount = frame.Players.Count,
                    animEventCount = frame.PlayerAnimEvents.Count,
                });
            // #endregion
        }

        private void StoreInterpolationFrame(CsDemoFrame frame, bool seek)
        {
            if (seek || interpolationNextFrame.Tick == frame.Tick)
            {
                interpolationPreviousFrame = frame;
                interpolationNextFrame = frame;
                lastAppliedTick = -1;
                // #region agent log
                AgentDebugLog.Write(
                    "H2",
                    "GUI/Types/GLViewers/GLDemoViewer.cs:StoreInterpolationFrame",
                    "interpolation reset after seek or same tick",
                    new { seek, frameTick = frame.Tick, playing });
                // #endregion
                return;
            }

            if (frame.Tick > interpolationNextFrame.Tick)
            {
                interpolationPreviousFrame = interpolationNextFrame;
                interpolationNextFrame = frame;
                return;
            }

            interpolationPreviousFrame = frame;
            interpolationNextFrame = frame;
            lastAppliedTick = -1;
        }

        private CsDemoFrame GetDisplayFrame()
        {
            if (!playing || interpolationNextFrame.Tick <= interpolationPreviousFrame.Tick)
            {
                return playback.CurrentFrame;
            }

            var alpha = (float)Math.Clamp(
                (playbackTick - interpolationPreviousFrame.Tick) / Math.Max(1, interpolationNextFrame.Tick - interpolationPreviousFrame.Tick),
                0,
                1);

            return InterpolateFrame(interpolationPreviousFrame, interpolationNextFrame, alpha);
        }

        private static CsDemoFrame InterpolateFrame(CsDemoFrame from, CsDemoFrame to, float alpha)
        {
            var toPlayers = to.Players.ToDictionary(static player => (player.Slot, player.SteamId));
            var players = from.Players
                .Select(player =>
                {
                    if (!toPlayers.TryGetValue((player.Slot, player.SteamId), out var next)
                        || !player.IsAlive
                        || !next.IsAlive)
                    {
                        return player;
                    }

                    return player with
                    {
                        Position = Vector3.Lerp(player.Position, next.Position, alpha),
                        Pitch = LerpDegrees(player.Pitch, next.Pitch, alpha),
                        Yaw = LerpDegrees(player.Yaw, next.Yaw, alpha),
                        Velocity = Vector3.Lerp(player.Velocity, next.Velocity, alpha),
                        OnGround = alpha < 0.5f ? player.OnGround : next.OnGround,
                        CrouchBlend = float.Lerp(player.CrouchBlend, next.CrouchBlend, alpha),
                        IsWalking = alpha < 0.5f ? player.IsWalking : next.IsWalking,
                        IsScoped = alpha < 0.5f ? player.IsScoped : next.IsScoped,
                        IsDefusing = alpha < 0.5f ? player.IsDefusing : next.IsDefusing,
                        ShotsFired = alpha < 0.5f ? player.ShotsFired : next.ShotsFired,
                        MovementInput = Vector2.Lerp(player.MovementInput, next.MovementInput, alpha),
                    };
                })
                .ToArray();

            var toEntities = to.WorldEntities.ToDictionary(static entity => entity.EntityIndex);
            var entities = from.WorldEntities
                .Select(entity =>
                {
                    if (!toEntities.TryGetValue(entity.EntityIndex, out var next))
                    {
                        return entity;
                    }

                    return entity with
                    {
                        Position = Vector3.Lerp(entity.Position, next.Position, alpha),
                        Pitch = LerpDegrees(entity.Pitch, next.Pitch, alpha),
                        Yaw = LerpDegrees(entity.Yaw, next.Yaw, alpha),
                        Roll = LerpDegrees(entity.Roll, next.Roll, alpha),
                        ModelIdentity = next.ModelIdentity,
                    };
                })
                .ToArray();

            return new CsDemoFrame(
                (int)Math.Round(from.Tick + ((to.Tick - from.Tick) * alpha)),
                players,
                entities,
                to.WorldEffects,
                to.PlayerEffects,
                to.PlayerAnimEvents);
        }

        private static float LerpDegrees(float from, float to, float alpha)
        {
            var delta = to - from;

            while (delta > 180)
            {
                delta -= 360;
            }

            while (delta < -180)
            {
                delta += 360;
            }

            return from + (delta * alpha);
        }

        private void ApplySelectedCamera(CsDemoFrame frame)
        {
            if (selectedSteamId == null || cameraMode == DemoCameraMode.Free)
            {
                return;
            }

            var player = frame.Players.FirstOrDefault(player => player.SteamId == selectedSteamId && player.IsAlive);
            if (player == null)
            {
                return;
            }

            var yaw = NormalizeRadians(float.DegreesToRadians(player.Yaw));
            var pitch = Math.Clamp(-float.DegreesToRadians(player.Pitch), -MathF.PI / 2f + 0.01f, MathF.PI / 2f - 0.01f);
            var eye = player.Position + new Vector3(0, 0, 64);

            if (cameraMode == DemoCameraMode.FirstPerson)
            {
                SetDemoCamera(eye, pitch, yaw);
                return;
            }

            var orbitYaw = NormalizeRadians(yaw + thirdPersonOrbitYawOffset);
            var target = player.Position + new Vector3(0, 0, ThirdPersonFollowTargetHeight);
            var (pitchSin, pitchCos) = MathF.SinCos(thirdPersonOrbitPitch);
            var forward = new Vector3(MathF.Cos(orbitYaw) * pitchCos, MathF.Sin(orbitYaw) * pitchCos, pitchSin);
            SetDemoCamera(target - (forward * thirdPersonFollowDistance), thirdPersonOrbitPitch, orbitYaw);
        }

        protected override void OnMouseWheel(object? sender, MouseEventArgs e)
        {
            // In third-person follow mode the scroll wheel zooms the camera toward/away
            // from the player instead of changing the free-camera move speed.
            if (cameraMode == DemoCameraMode.ThirdPerson)
            {
                var steps = e.Delta / 120f;
                thirdPersonFollowDistance = Math.Clamp(
                    thirdPersonFollowDistance - (steps * ThirdPersonZoomStep),
                    ThirdPersonMinFollowDistance,
                    ThirdPersonMaxFollowDistance);
                return;
            }

            base.OnMouseWheel(sender, e);
        }

        private void UpdateThirdPersonOrbitFromMouse()
        {
            if (cameraMode != DemoCameraMode.ThirdPerson
                || (CurrentlyPressedKeys & TrackedKeys.MouseLeftOrRight) == 0
                || LastMouseDelta == Point.Empty)
            {
                return;
            }

            var fovRatio = RendererContext.FieldOfView / float.RadiansToDegrees(2f * MathF.Atan(3f / 4f));
            var radiansPerPixel = float.DegreesToRadians(0.022f * Settings.Config.MouseSensitivity * fovRatio);

            thirdPersonOrbitYawOffset = NormalizeRadians(thirdPersonOrbitYawOffset - (LastMouseDelta.X * radiansPerPixel));
            thirdPersonOrbitPitch = Math.Clamp(
                thirdPersonOrbitPitch - (LastMouseDelta.Y * radiansPerPixel),
                -70f * MathF.PI / 180f,
                40f * MathF.PI / 180f);
        }

        private void SetDemoCamera(Vector3 location, float pitch, float yaw)
        {
            Input.Camera.SetLocationPitchYaw(location, pitch, yaw);
            Renderer.Camera.SetLocationPitchYaw(location, pitch, yaw);
            Input.ForceUpdate = true;
        }

        private static float NormalizeRadians(float radians)
        {
            while (radians > MathF.PI)
            {
                radians -= MathF.Tau;
            }

            while (radians < -MathF.PI)
            {
                radians += MathF.Tau;
            }

            return radians;
        }

        protected override void OnPicked(object? sender, PickingTexture.PickingResponse pickingResponse)
        {
            base.OnPicked(sender, pickingResponse);

            if (pickingResponse.Intent != PickingTexture.PickingIntent.Select)
            {
                return;
            }

            var pixelInfo = pickingResponse.PixelInfo;
            if (pixelInfo.ObjectId == 0 || pixelInfo.Unused2 != 0 || pixelInfo.IsSkybox > 0)
            {
                return;
            }

            var sceneNode = Scene.Find(pixelInfo.ObjectId);
            if (sceneNode == null)
            {
                return;
            }

            foreach (var player in playback.CurrentFrame.Players)
            {
                if (playerSceneManager?.TryGetNode(player.Slot, out var playerNode) != true
                    || !ReferenceEquals(playerNode, sceneNode))
                {
                    continue;
                }

                SelectPlayerCamera(player.SteamId);
                return;
            }
        }

        private void UpdateDemoUi(CsDemoFrame frame)
        {
            UpdateHud(frame);
        }

        private void UpdateHud(CsDemoFrame frame)
        {
            if (topHud == null || bottomHud == null)
            {
                return;
            }

            void Update()
            {
                if (topHud.IsDisposed || bottomHud.IsDisposed)
                {
                    return;
                }

                var roundNumber = GetRoundNumber(frame.Tick);
                topHud.Summary = playback.Summary;
                topHud.Frame = frame;
                topHud.RoundNumber = roundNumber;
                topHud.SelectedSteamId = selectedSteamId;
                topHud.Invalidate();

                bottomHud.Summary = playback.Summary;
                bottomHud.Frame = frame;
                bottomHud.RoundNumber = roundNumber;
                bottomHud.Speed = speed;
                bottomHud.Playing = playing;
                bottomHud.SelectedSteamId = selectedSteamId;
                bottomHud.DeathMarkerImage = deathMarkerBitmap;
                bottomHud.KillMarkerImage = killMarkerBitmap;
                bottomHud.CameraModeLabel = GetCameraModeLabel();
                bottomHud.VoxelBoxEnabled = voxelBoxEnabled;
                bottomHud.AnimDebugEnabled = animDebugEnabled;
                bottomHud.Invalidate();
            }

            if (bottomHud.InvokeRequired)
            {
                try
                {
                    bottomHud.BeginInvoke(Update);
                }
                catch (InvalidOperationException)
                {
                    // Control is being disposed.
                }
            }
            else
            {
                Update();
            }
        }

        private int GetRoundNumber(int tick)
        {
            if (playback.Summary.Rounds.Count == 0)
            {
                return 1;
            }

            return playback.Summary.Rounds
                .LastOrDefault(round => round.StartTick <= tick)
                ?.RoundNumber ?? 1;
        }

        private string GetCameraModeLabel()
        {
            return cameraMode switch
            {
                DemoCameraMode.FirstPerson => "FIRST PERSON",
                DemoCameraMode.ThirdPerson => "THIRD PERSON",
                _ => "FREE CAM",
            };
        }

        private int GetSelectedBlindAlpha(CsDemoFrame frame)
        {
            if (selectedSteamId == null || cameraMode != DemoCameraMode.FirstPerson)
            {
                return 0;
            }

            var blind = frame.PlayerEffects
                .Where(effect => effect.Kind == CsDemoPlayerEffectKind.Blind && effect.PlayerSteamId == selectedSteamId)
                .OrderByDescending(static effect => effect.StartTick)
                .FirstOrDefault();

            if (blind == null)
            {
                return 0;
            }

            var duration = Math.Max(1, blind.EndTick - blind.StartTick);
            var age = Math.Clamp((frame.Tick - blind.StartTick) / (float)duration, 0f, 1f);
            var fade = MathF.Pow(1f - age, 1.65f);

            return Math.Clamp((int)(235 * fade), 0, 235);
        }

        private static Color32 TeamColor(CsDemoTeam team)
        {
            return team switch
            {
                CsDemoTeam.Terrorist => new Color32(226, 152, 52),
                CsDemoTeam.CounterTerrorist => new Color32(70, 150, 230),
                CsDemoTeam.Spectator => new Color32(160, 160, 160),
                _ => new Color32(220, 220, 220),
            };
        }

        protected override bool ShowPausedOverlay => false;

    }
}
