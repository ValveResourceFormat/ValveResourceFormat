using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using GUI.Types.GLViewers;
using GUI.Types.Renderer;
using GUI.Utils;
using OpenTK.Graphics.OpenGL;
using OpenTK.Windowing.Desktop;
using SkiaSharp;
using static GUI.Types.Renderer.PickingTexture;

#nullable disable

namespace GUI.Controls
{
    partial class GLViewerControl : IDisposable
    {
        static readonly TimeSpan FpsUpdateTimeSpan = TimeSpan.FromSeconds(0.1);

        protected RendererControl UiControl;
        private OpenTK.Windowing.Desktop.NativeWindow GLNativeWindow;
        public GLControl GLControl { get; private set; }

        public struct RenderEventArgs
        {
            public float FrameTime { get; set; }
        }

        public float Uptime { get; private set; }
        public Camera Camera { get; protected set; }
        public Types.Renderer.TextRenderer TextRenderer { get; protected set; }


        public event EventHandler<RenderEventArgs> GLPaint;

        protected virtual void OnGLLoad() { }

        protected readonly PostProcessRenderer postProcessRenderer;

        protected Form FullScreenForm { get; private set; }
        protected PickingTexture Picker { get; set; }

        bool MouseOverRenderArea;
        Point MouseDelta;
        Point MousePreviousPosition;
        Point InitialMousePosition;
        protected TrackedKeys CurrentlyPressedKeys;
        protected Point LastMouseDelta { get; private set; }

        private long lastUpdate;
        private long lastFpsUpdate;
        private string lastFps;
        private readonly float[] frameTimes = new float[30];
        private int frameTimeNextId;
        private int frametimeQuery1;
        private int frametimeQuery2;

#if DEBUG
        private ShaderLoader ShaderLoader;
#endif

        public GLViewerControl(VrfGuiContext guiContext)
        {
            Camera = new Camera();

            TextRenderer = new(guiContext, Camera);
            postProcessRenderer = new(guiContext);

#if DEBUG
            ShaderLoader = guiContext.ShaderLoader;
            ShaderLoader.EnableHotReload();
#endif
        }

        public virtual Control InitializeUiControls()
        {
            GLNativeWindow.MakeCurrent();

            GLControl = new GLControl()
            {
                Dock = DockStyle.Fill
            };

            GLControl.Paint += OnPaint;
            GLControl.Resize += OnResize;
            GLControl.MouseEnter += OnMouseEnter;
            GLControl.MouseLeave += OnMouseLeave;
            GLControl.MouseUp += OnMouseUp;
            GLControl.MouseDown += OnMouseDown;
            GLControl.MouseMove += OnMouseMove;
            GLControl.MouseWheel += OnMouseWheel;
            GLControl.PreviewKeyDown += OnPreviewKeyDown;
            GLControl.KeyDown += OnKeyDown;
            GLControl.KeyUp += OnKeyUp;
            GLControl.GotFocus += OnGotFocus;
            GLControl.LostFocus += OnLostFocus;
            GLControl.VisibleChanged += OnVisibleChanged;
            Program.MainForm.Activated += OnAppActivated;

            UiControl = new()
            {
                Dock = DockStyle.Fill
            };
            UiControl.GLControlContainer.Controls.Add(GLControl);
            GLControl.AttachNativeWindow(GLNativeWindow);

#if DEBUG
            ShaderLoader.ShaderHotReload.SetControl(GLControl);
            CodeHotReloadService.CodeHotReloaded += OnCodeHotReloaded;

            var button = new Button
            {
                Text = "Reload shaders",
                AutoSize = true,
            };
            button.Click += OnButtonClick;

            void OnButtonClick(object s, EventArgs e)
            {
                ShaderLoader.ReloadAllShaders();
            }

            UiControl.AddControl(button);
#endif

            OnResize();

            // Bind paint event at the end of the processing loop so that first paint event has correctly sized gl control
            OnFirstPaint();

            return UiControl;
        }

        private void OnPreviewKeyDown(object sender, PreviewKeyDownEventArgs e)
        {
            // Sink all inputs into gl control to prevent shortcuts like Ctrl+W and just pressing Alt going to parent
            e.IsInputKey = true;
        }

        protected virtual void OnKeyDown(object sender, KeyEventArgs e)
        {
            CurrentlyPressedKeys |= RemapKey(e.KeyCode);

            e.Handled = true;
            e.SuppressKeyPress = true;

            if (e.KeyData == (Keys.Control | Keys.C))
            {
                var title = Program.MainForm.Text;
                Program.MainForm.Text = "Source 2 Viewer - Copying image to clipboard…";

                using var bitmap = ReadPixelsToBitmap();
                ClipboardSetImage(bitmap);

                Program.MainForm.Text = title;

                return;
            }

            if ((e.KeyCode == Keys.Escape || e.KeyCode == Keys.F11) && FullScreenForm != null)
            {
                FullScreenForm.Close();

                return;
            }

            if (e.KeyCode == Keys.F11)
            {
                var currentScreen = Screen.FromControl(Program.MainForm);

                FullScreenForm = new Form
                {
                    Text = "Source 2 Viewer Fullscreen",
                    Icon = Program.MainForm.Icon,
                    ControlBox = false,
                    FormBorderStyle = FormBorderStyle.None,
                    StartPosition = FormStartPosition.Manual,
                    Location = currentScreen.Bounds.Location,
                    Size = currentScreen.Bounds.Size,
                    WindowState = FormWindowState.Maximized
                };
                FullScreenForm.Controls.Add(GLControl);
                FullScreenForm.Show();
                FullScreenForm.Focus();
                FullScreenForm.FormClosed += OnFullScreenFormClosed;
            }
        }

        protected virtual SKBitmap ReadPixelsToBitmap()
        {
            var bitmap = new SKBitmap(GLDefaultFramebuffer.Width, GLDefaultFramebuffer.Height, SKColorType.Bgra8888, SKAlphaType.Opaque);
            var pixels = bitmap.GetPixels(out var length);

            BlitFramebufferToScreen();

            GLDefaultFramebuffer.Bind(FramebufferTarget.ReadFramebuffer);
            GL.ReadPixels(0, 0, GLDefaultFramebuffer.Width, GLDefaultFramebuffer.Height, PixelFormat.Bgra, PixelType.UnsignedByte, pixels);

            // Flip y
            using var canvas = new SKCanvas(bitmap);
            canvas.Scale(1, -1, 0, bitmap.Height / 2f);
            canvas.DrawBitmap(bitmap, new SKPoint());

            return bitmap;
        }

        private void OnKeyUp(object sender, KeyEventArgs e)
        {
            CurrentlyPressedKeys &= ~RemapKey(e.KeyCode);
        }

        private void OnFullScreenFormClosed(object sender, EventArgs e)
        {
            UiControl.GLControlContainer.Controls.Add(GLControl);
            GLControl.Focus();

            var form = (Form)sender;
            form.FormClosed -= OnFullScreenFormClosed;

            FullScreenForm = null;
        }

        public virtual void Dispose()
        {
            GLControl.Paint -= OnPaint;
            GLControl.Resize -= OnResize;
            GLControl.MouseEnter -= OnMouseEnter;
            GLControl.MouseLeave -= OnMouseLeave;
            GLControl.MouseUp -= OnMouseUp;
            GLControl.MouseDown -= OnMouseDown;
            GLControl.MouseMove -= OnMouseMove;
            GLControl.MouseWheel -= OnMouseWheel;
            GLControl.PreviewKeyDown -= OnPreviewKeyDown;
            GLControl.KeyDown -= OnKeyDown;
            GLControl.KeyUp -= OnKeyUp;
            GLControl.GotFocus -= OnGotFocus;
            GLControl.LostFocus -= OnLostFocus;
            GLControl.VisibleChanged -= OnVisibleChanged;
            Program.MainForm.Activated -= OnAppActivated;
            FullScreenForm?.Dispose();
            UiControl.Dispose();
            GLNativeWindow.Dispose();

#if DEBUG
            CodeHotReloadService.CodeHotReloaded -= OnCodeHotReloaded;
#endif
        }

        private void OnVisibleChanged(object sender, EventArgs e)
        {
            if (GLControl.Visible)
            {
                OnResize();

                if (Form.ActiveForm != null)
                {
                    GLControl.Focus();
                    GLControl.Invalidate();
                }
            }
        }

        private void OnMouseLeave(object sender, EventArgs e)
        {
            MouseOverRenderArea = false;
        }

        private void OnMouseEnter(object sender, EventArgs e)
        {
            MouseOverRenderArea = true;
        }

        protected virtual void OnMouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left && e.Button != MouseButtons.Right)
            {
                return;
            }

            GLControl.Focus();

            InitialMousePosition = new Point(e.X, e.Y);
            MouseDelta = Point.Empty;
            MousePreviousPosition = GLControl.PointToScreen(InitialMousePosition);

            if (e.Button == MouseButtons.Left)
            {
                CurrentlyPressedKeys |= TrackedKeys.MouseLeft;

                if (e.Clicks == 2)
                {
                    var intent = Control.ModifierKeys.HasFlag(Keys.Control)
                        ? PickingIntent.Open
                        : PickingIntent.Details;
                    Picker?.RequestNextFrame(e.X, e.Y, intent);
                }
            }
            else if (e.Button == MouseButtons.Right)
            {
                CurrentlyPressedKeys |= TrackedKeys.MouseRight;
            }
        }

        protected virtual void OnMouseUp(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                CurrentlyPressedKeys &= ~TrackedKeys.MouseLeft;

                if (InitialMousePosition == new Point(e.X, e.Y))
                {
                    Picker?.RequestNextFrame(e.X, e.Y, PickingIntent.Select);
                }
            }
            else if (e.Button == MouseButtons.Right)
            {
                // right click context menu?
                CurrentlyPressedKeys &= ~TrackedKeys.MouseRight;
            }

            if ((CurrentlyPressedKeys & TrackedKeys.MouseLeftOrRight) == 0)
            {
                MouseDelta = Point.Empty;
            }
        }

        protected virtual void OnMouseMove(object sender, MouseEventArgs e)
        {
            if ((CurrentlyPressedKeys & TrackedKeys.MouseLeftOrRight) == 0)
            {
                return;
            }

            var position = GLControl.PointToScreen(new Point(e.X, e.Y));
            var topLeft = GLControl.PointToScreen(Point.Empty);
            var bottomRight = topLeft + GLControl.Size;

            if (FullScreenForm != null)
            {
                // Windows has a 1px edge on bottom and right of the screen where cursor can't reach
                // (assuming that there is no secondary screen past these edges)
                bottomRight.X -= 1;
                bottomRight.Y -= 1;
            }

            var positionWrapped = position;

            if (position.X <= topLeft.X)
            {
                MouseDelta.X--;
                positionWrapped.X = bottomRight.X - 1;
            }
            else if (position.X >= bottomRight.X)
            {
                MouseDelta.X++;
                positionWrapped.X = topLeft.X + 1;
            }

            if (position.Y <= topLeft.Y)
            {
                MouseDelta.Y--;
                positionWrapped.Y = bottomRight.Y - 1;
            }
            else if (position.Y >= bottomRight.Y)
            {
                MouseDelta.Y++;
                positionWrapped.Y = topLeft.Y + 1;
            }

            if (positionWrapped != position)
            {
                // When wrapping cursor, add only 1px delta movement above
                MousePreviousPosition = positionWrapped;
                Cursor.Position = positionWrapped;
                return;
            }

            MouseDelta.X += position.X - MousePreviousPosition.X;
            MouseDelta.Y += position.Y - MousePreviousPosition.Y;
            MousePreviousPosition = position;
        }

        protected virtual void OnMouseWheel(object sender, MouseEventArgs e)
        {
            var modifier = Camera.OnMouseWheel(e.Delta);

            if (Camera.OrbitMode)
            {
                SetMoveSpeedOrZoomLabel($"Orbit distance: {modifier:0.0} (scroll to change)");
            }
            else
            {
                SetMoveSpeedOrZoomLabel($"Move speed: {modifier:0.0}x (scroll to change)");
            }
        }

        protected void SetMoveSpeedOrZoomLabel(string text) => UiControl.SetMoveSpeed(text);

        private static void OnDebugMessage(DebugSource source, DebugType type, int id, DebugSeverity severity, int length, IntPtr pMessage, IntPtr pUserParam)
        {
            var severityStr = severity.ToString().Replace("DebugSeverity", string.Empty, StringComparison.Ordinal);
            var sourceStr = source.ToString().Replace("DebugSource", string.Empty, StringComparison.Ordinal);
            var typeStr = type.ToString().Replace("DebugType", string.Empty, StringComparison.Ordinal);
            var message = System.Runtime.InteropServices.Marshal.PtrToStringUTF8(pMessage, length);
            var error = $"[{severityStr} {sourceStr} {typeStr}] {message}";

            switch (type)
            {
                case DebugType.DebugTypeError: Log.Error("OpenGL", error); break;
                default: Log.Debug("OpenGL", error); break;
            }

#if DEBUG
            if (type == DebugType.DebugTypeError)
            {
                Debugger.Break();
            }
#endif
        }

        private static readonly DebugProc OpenGLDebugMessageDelegate = OnDebugMessage;

        public Framebuffer GLDefaultFramebuffer;
        public Framebuffer MainFramebuffer;
        private int MaxSamples;
        private int NumSamples => Math.Max(1, Math.Min(Settings.Config.AntiAliasingSamples, MaxSamples));

        private bool loaded;

        public void InitializeLoad()
        {
            // Create the GLFW window on the UI thread even though this method may be called from a
            // background thread. This is necessary because of Win32 window thread-affinity rules.
            //
            // The goal is to load GL resources on a background thread to avoid blocking the UI during
            // file loading, but we also need to render into a WinForms control which requires the OpenGL
            // window to be reparented as a child of that control.
            //
            // Win32 has strict rules about window ownership. A window can only be manipulated (resized,
            // reparented, styled) by the thread that created it. Calling SetParent or SetWindowLongPtr
            // from a different thread causes deadlocks because these APIs send synchronous window messages
            // that must be processed by the owning thread. If that thread is blocked, the messages never
            // get processed and the calling thread freezes.
            //
            // We tried creating the window on the background thread and reparenting it from the UI thread,
            // but the Win32 APIs would block waiting for the background thread to process messages while
            // the background thread was busy loading resources. We also tried having the background thread
            // invoke back to the UI thread to get the parent handle and then do the reparenting, but this
            // caused a deadlock where each thread was waiting for the other. Creating a separate render
            // thread had the same thread-affinity issues. Using two windows with shared contexts mostly
            // worked but caused black screens because each window has its own default framebuffer and we
            // were getting the wrong one.
            //
            // The solution is to create the window on the UI thread but transfer the OpenGL context to
            // the background thread for loading. Note that OpenGL contexts cannot exist without a window
            // because they require a drawing surface allocated by the OS. OpenGL contexts have different
            // rules than windows though. A context can only be current on one thread at a time, but it
            // can be made current on different threads at different times. You release it from one thread
            // with MakeNoneCurrent and then make it current on another thread with MakeCurrent.
            //
            // So the sequence is: UI thread creates the window (via Invoke if needed) which makes it owned
            // by the UI thread so it can be reparented later. The OpenGL context is automatically made
            // current on the UI thread. We release the context with MakeNoneCurrent so it's not bound to
            // any thread. The background thread makes the context current and loads all the GL resources
            // like textures, shaders, and VBOs. This is the slow part that we don't want blocking the UI.
            // When loading is done the background thread releases the context. Finally the UI thread makes
            // the context current again when creating the WinForms controls and can safely reparent the window
            // since it owns it and render since it has the context.
            //
            // This works because window operations happen on the thread that created the window (UI thread)
            // but context operations can happen on any thread as long as the context is only current on one
            // thread at a time. Resources created in a context are available regardless of which thread
            // makes the context current.
            Program.MainForm.Invoke(() =>
            {
                Debug.Assert(GLNativeWindow is null);

                var settings = new NativeWindowSettings()
                {
                    APIVersion = GLEnvironment.RequiredVersion,
                    Flags = GLEnvironment.Flags,
                    RedBits = 8,
                    GreenBits = 8,
                    BlueBits = 8,
                    AlphaBits = 0,
                    DepthBits = 0,
                    StencilBits = 0,
                    AutoLoadBindings = true,
                    StartFocused = false,
                    StartVisible = false,
                    WindowBorder = OpenTK.Windowing.Common.WindowBorder.Hidden,
                    WindowState = OpenTK.Windowing.Common.WindowState.Normal,
                };
                GLNativeWindow = new(settings);

                GLNativeWindow.Context.MakeNoneCurrent();
            });

            Debug.Assert(GLNativeWindow is not null);

            GLNativeWindow.MakeCurrent();
            GLNativeWindow.Context.SwapInterval = Settings.Config.Vsync;

            GL.Enable(EnableCap.DebugOutput);
            GL.DebugMessageCallback(OpenGLDebugMessageDelegate, IntPtr.Zero);

#if DEBUG
            GL.Enable(EnableCap.DebugOutputSynchronous);

            // Filter out performance warnings
            GL.DebugMessageControl(DebugSourceControl.DebugSourceApi, DebugTypeControl.DebugTypeOther, DebugSeverityControl.DebugSeverityNotification, 0, Array.Empty<int>(), false);

            // Filter out debug group push/pops
            GL.DebugMessageControl(DebugSourceControl.DebugSourceApplication, DebugTypeControl.DontCare, DebugSeverityControl.DebugSeverityNotification, 0, Array.Empty<int>(), false);
#else
            // Only log high severity messages in release builds
            GL.DebugMessageControl(DebugSourceControl.DontCare, DebugTypeControl.DontCare, DebugSeverityControl.DontCare, 0, Array.Empty<int>(), false);
            GL.DebugMessageControl(DebugSourceControl.DontCare, DebugTypeControl.DontCare, DebugSeverityControl.DebugSeverityHigh, 0, Array.Empty<int>(), true);
#endif

            GLEnvironment.Initialize();
            MaxSamples = GL.GetInteger(GetPName.MaxSamples);
            GLDefaultFramebuffer = Framebuffer.GetGLDefaultFramebuffer();

            GL.CreateQueries(QueryTarget.TimeElapsed, 1, out frametimeQuery1);
            GL.CreateQueries(QueryTarget.TimeElapsed, 1, out frametimeQuery2);

            // Needed to fix crash on certain drivers
            GL.BeginQuery(QueryTarget.TimeElapsed, frametimeQuery2);
            GL.EndQuery(QueryTarget.TimeElapsed);

            // Application semantics / default state
            GL.Enable(EnableCap.TextureCubeMapSeamless);
            GL.Enable(EnableCap.CullFace);
            GL.CullFace(TriangleFace.Back);
            GL.Enable(EnableCap.DepthTest);

            // reverse z
            GL.ClipControl(ClipOrigin.LowerLeft, ClipDepthMode.ZeroToOne);
            GL.DepthFunc(DepthFunction.Greater);
            GL.ClearDepth(0.0f);

            // Parallel shader compilation, 0xFFFFFFFF requests an implementation-specific maximum
            if (GLEnvironment.ParallelShaderCompileSupport == GLEnvironment.ParallelShaderCompileType.Khr)
            {
                GL.Khr.MaxShaderCompilerThreads(uint.MaxValue);
            }
            else if (GLEnvironment.ParallelShaderCompileSupport == GLEnvironment.ParallelShaderCompileType.Arb)
            {
                GL.Arb.MaxShaderCompilerThreads(uint.MaxValue);
            }

            TextRenderer.Load();
            postProcessRenderer.Load();

            try
            {
                // Framebuffer used to draw geometry
                MainFramebuffer = Framebuffer.Prepare(nameof(MainFramebuffer),
                    1024,
                    768,
                    NumSamples,
                    new(PixelInternalFormat.Rgba16f, PixelFormat.Rgba, PixelType.HalfFloat),
                    Framebuffer.DepthAttachmentFormat.Depth32FStencil8
                );

                MainFramebuffer.ClearMask |= ClearBufferMask.StencilBufferBit;

                OnGLLoad();
            }
            catch (Exception)
            {
#if false // TODO
                var control = CodeTextBox.CreateFromException(exception);
                UiControl.GLControlContainer.Controls.Clear();
                UiControl.GLControlContainer.Controls.Add(control);
#endif

                throw;
            }

            GLNativeWindow.Context.MakeNoneCurrent();

            loaded = true;
            lastUpdate = Stopwatch.GetTimestamp();
        }

        private void OnPaint(object sender, EventArgs e)
        {
            if (!loaded)
            {
                return;
            }

            Application.DoEvents();

            if (GLControl.IsDisposed || !GLControl.Visible)
            {
                return;
            }

            GLNativeWindow.MakeCurrent();

            if (MainFramebuffer.InitialStatus != FramebufferErrorCode.FramebufferComplete)
            {
                return;
            }

            var isActiveForm = Form.ActiveForm != null;

            var isTextureViewer = this is GLTextureViewer;
            var currentTime = Stopwatch.GetTimestamp();
            var elapsed = Stopwatch.GetElapsedTime(lastUpdate, currentTime);
            lastUpdate = currentTime;

            // Clamp frametime because it is possible to go past 1 second when gl control is paused which may cause issues in things like particle rendering
            var frameTime = MathF.Min(1f, (float)elapsed.TotalSeconds);
            Uptime += frameTime;

            if (MouseOverRenderArea && !isTextureViewer)
            {
                var pressedKeys = CurrentlyPressedKeys;
                var modifierKeys = Control.ModifierKeys;

                if ((modifierKeys & Keys.Shift) > 0)
                {
                    pressedKeys |= TrackedKeys.Shift;
                }

                if ((modifierKeys & Keys.Alt) > 0)
                {
                    pressedKeys |= TrackedKeys.Alt;
                }

                Camera.Tick(frameTime, pressedKeys, MouseDelta);
                LastMouseDelta = MouseDelta;
                MouseDelta = Point.Empty;
            }

            Camera.RecalculateMatrices(Uptime);

            GL.BeginQuery(QueryTarget.TimeElapsed, frametimeQuery1);

            GLPaint?.Invoke(this, new RenderEventArgs { FrameTime = frameTime });

            GL.EndQuery(QueryTarget.TimeElapsed);

            if (Settings.Config.DisplayFps != 0)
            {
                currentTime = Stopwatch.GetTimestamp();
                var fpsElapsed = Stopwatch.GetElapsedTime(lastFpsUpdate, currentTime);

                frameTimes[frameTimeNextId++] = frameTime;
                frameTimeNextId %= frameTimes.Length;

                if (fpsElapsed >= FpsUpdateTimeSpan)
                {
                    var frametimeQuery = frametimeQuery2;
                    frametimeQuery2 = frametimeQuery1;
                    frametimeQuery1 = frametimeQuery;

                    GL.GetQueryObject(frametimeQuery, GetQueryObjectParam.QueryResultNoWait, out long gpuTime);
                    var gpuFrameTime = gpuTime / 1_000_000f;

                    var fps = 1f / (frameTimes.Sum() / frameTimes.Length);
                    var cpuFrameTime = Stopwatch.GetElapsedTime(lastUpdate, currentTime).TotalMilliseconds;

                    lastFpsUpdate = currentTime;
                    lastFps = $"FPS: {fps,-3:0}  CPU: {cpuFrameTime,-4:0.0}ms  GPU: {gpuFrameTime,-4:0.0}ms";
                }
            }

            BlitFramebufferToScreen();

            if (Settings.Config.DisplayFps != 0 && isActiveForm && !isTextureViewer)
            {
                TextRenderer.AddText(new Types.Renderer.TextRenderer.TextRenderRequest
                {
                    X = 2f,
                    Y = MainFramebuffer.Height - 4f,
                    Scale = 14f,
                    Text = lastFps
                });
            }

            TextRenderer.Render();

            GLNativeWindow.Context.SwapBuffers();
            Picker?.TriggerEventIfAny();

            if (isActiveForm)
            {
                // Infinite loop of invalidates causes a bug with message box dialogs not actually appearing in front,
                // requiring user to press Alt key for it to appear. Checking for active form also pauses rendering while
                // the app is not focused. We don't have a reference to the file save/open dialog, thus ActiveForm will be null.
                //
                // Repro: open a renderer tab, right click on tab to export, save with name that would cause "file already exists" popup.
                //
                GLControl.Invalidate();
            }
        }

        private void BlitFramebufferToScreen()
        {
            if (MainFramebuffer == GLDefaultFramebuffer)
            {
                return; // not required
            }

            MainFramebuffer.Bind(FramebufferTarget.ReadFramebuffer);
            GLDefaultFramebuffer.Bind(FramebufferTarget.DrawFramebuffer);

            FramebufferBlit(MainFramebuffer, GLDefaultFramebuffer);
        }

        /// <summary>
        /// Multisampling resolve, postprocess the image & convert to gamma.
        /// </summary>
        protected void FramebufferBlit(Framebuffer inputFramebuffer, Framebuffer outputFramebuffer, bool flipY = false)
        {
            using var _ = new GLDebugGroup("Post Processing");

            Debug.Assert(inputFramebuffer.NumSamples > 0);
            Debug.Assert(outputFramebuffer.NumSamples == 0);

            postProcessRenderer.Render(colorBuffer: inputFramebuffer, flipY);
        }

        protected virtual void OnResize(object sender, EventArgs e)
        {
            if (MainFramebuffer is null)
            {
                return;
            }

            OnResize();
            GLControl.Invalidate();
        }

        protected virtual void OnResize()
        {
            if (!loaded)
            {
                return;
            }

            var (w, h) = (GLControl.Width, GLControl.Height);

            if (w <= 0 || h <= 0)
            {
                return;
            }

            GLNativeWindow.MakeCurrent();

            GLDefaultFramebuffer.Resize(w, h);

            if (MainFramebuffer != GLDefaultFramebuffer)
            {
                MainFramebuffer.Resize(w, h, NumSamples);
            }

            if (MainFramebuffer.InitialStatus == FramebufferErrorCode.FramebufferUndefined)
            {
                var status = MainFramebuffer.Initialize();

                if (status != FramebufferErrorCode.FramebufferComplete)
                {
                    Log.Error(nameof(GLViewerControl), $"Framebuffer failed to initialize with error: {status}");
                    Log.Info(nameof(GLViewerControl), "Falling back to default framebuffer.");

                    MainFramebuffer.Delete();
                    MainFramebuffer = GLDefaultFramebuffer;
                    GL.Enable(EnableCap.FramebufferSrgb);
                }
            }

            Camera.SetViewportSize(w, h);
            Picker?.Resize(w, h);
        }


        protected virtual void OnFirstPaint()
        {
            //
        }

        private void OnAppActivated(object sender, EventArgs e)
        {
            GLControl.Invalidate();
        }

        private void OnGotFocus(object sender, EventArgs e)
        {
            if (MainFramebuffer is null || !MainFramebuffer.HasValidDimensions())
            {
                return;
            }

            lastUpdate = Stopwatch.GetTimestamp();
            OnResize();
            GLControl.Invalidate();
        }

        private void OnLostFocus(object sender, EventArgs e)
        {
            CurrentlyPressedKeys = TrackedKeys.None;
            MouseDelta = Point.Empty;
        }

        private static TrackedKeys RemapKey(Keys key) => key switch
        {
            Keys.W => TrackedKeys.Forward,
            Keys.A => TrackedKeys.Left,
            Keys.S => TrackedKeys.Back,
            Keys.D => TrackedKeys.Right,
            Keys.Q => TrackedKeys.Up,
            Keys.Z => TrackedKeys.Down,
            Keys.ControlKey => TrackedKeys.Control,
            Keys.LShiftKey => TrackedKeys.Shift,
            Keys.LMenu => TrackedKeys.Alt,
            _ => TrackedKeys.None,
        };

        private static void ClipboardSetImage(SKBitmap bitmap)
        {
            var data = new DataObject();

            using var bitmapWindows = bitmap.ToBitmap();
            data.SetData(DataFormats.Bitmap, true, bitmapWindows);

            using var pngStream = new MemoryStream();
            using var pixels = bitmap.PeekPixels();
            var png = pixels.Encode(pngStream, new SKPngEncoderOptions(SKPngEncoderFilterFlags.Sub, zLibLevel: 1));

            bitmapWindows.Save(pngStream, System.Drawing.Imaging.ImageFormat.Png);
            data.SetData("PNG", false, pngStream);

            Clipboard.SetDataObject(data, copy: true);
        }

#if DEBUG
        private void OnCodeHotReloaded(object sender, EventArgs e)
        {
            GLControl.Invalidate();
        }
#endif
    }
}
