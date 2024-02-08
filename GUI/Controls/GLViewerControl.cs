using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using GUI.Types.Renderer;
using GUI.Utils;
using OpenTK;
using OpenTK.Graphics;
using OpenTK.Graphics.OpenGL;
using SkiaSharp;
using static GUI.Types.Renderer.PickingTexture;
using WinFormsMouseEventArgs = System.Windows.Forms.MouseEventArgs;

namespace GUI.Controls
{
    partial class GLViewerControl : ControlPanelView
    {
        protected override Panel ControlsPanel => controlsPanel;
        static readonly TimeSpan FpsUpdateTimeSpan = TimeSpan.FromSeconds(0.1);

        public GLControl GLControl { get; }

        public struct RenderEventArgs
        {
            public float FrameTime { get; set; }
        }

        public Camera Camera { get; protected set; }

        public event EventHandler<RenderEventArgs> GLPaint;
        public event EventHandler GLLoad;
        public Action<GLViewerControl> GLPostLoad { get; set; }
        private static bool hasCheckedOpenGL;

        private readonly Types.Renderer.TextRenderer textRenderer;

        protected Form FullScreenForm { get; private set; }
        protected PickingTexture Picker { get; set; }

        bool MouseOverRenderArea;
        Point MouseDelta;
        Point MousePreviousPosition;
        Point InitialMousePosition;
        TrackedKeys CurrentlyPressedKeys;

        private long lastUpdate;
        private long lastFpsUpdate;
        private string lastFps;
        private readonly float[] frameTimes = new float[30];
        private int frameTimeNextId;
        private int frametimeQuery1;
        private int frametimeQuery2;

        public GLViewerControl(VrfGuiContext guiContext)
        {
            InitializeComponent();

            Camera = new Camera();

            // Initialize GL control
            var flags = GraphicsContextFlags.ForwardCompatible;

#if DEBUG
            flags |= GraphicsContextFlags.Debug;
#endif

            GLControl = new GLControl(new GraphicsMode(32, 1, 0, 0, 0, 2), 4, 6, flags);
            GLControl.Load += OnLoad;
            GLControl.Paint += OnPaint;
            GLControl.Resize += OnResize;
            GLControl.MouseEnter += OnMouseEnter;
            GLControl.MouseLeave += OnMouseLeave;
            GLControl.MouseUp += OnMouseUp;
            GLControl.MouseDown += OnMouseDown;
            GLControl.MouseMove += OnMouseMove;
            GLControl.MouseWheel += OnMouseWheel;
            GLControl.KeyDown += OnKeyDown;
            GLControl.KeyUp += OnKeyUp;
            GLControl.GotFocus += OnGotFocus;
            GLControl.LostFocus += OnLostFocus;
            GLControl.VisibleChanged += OnVisibleChanged;
            Disposed += OnDisposed;

            GLControl.Dock = DockStyle.Fill;
            glControlContainer.Controls.Add(GLControl);

            textRenderer = new(guiContext);

#if DEBUG
            guiContext.ShaderLoader.EnableHotReload(GLControl);

            var button = new Button
            {
                Text = "Reload shaders",
                AutoSize = true,
            };
            button.Click += OnButtonClick;

            void OnButtonClick(object s, EventArgs e)
            {
                guiContext.ShaderLoader.ReloadAllShaders();
            }

            AddControl(button);
#endif
        }

        protected virtual void OnKeyDown(object sender, KeyEventArgs e)
        {
            CurrentlyPressedKeys |= RemapKey(e.KeyCode);

            if (e.KeyData == (Keys.Control | Keys.C))
            {
                var title = Program.MainForm.Text;
                Program.MainForm.Text = "Source 2 Viewer - Copying image to clipboard…";

                using var bitmap = new SKBitmap(GLDefaultFramebuffer.Width, GLDefaultFramebuffer.Height, SKColorType.Bgra8888, SKAlphaType.Opaque);
                var pixels = bitmap.GetPixels(out var length);

                GL.Flush();
                GL.Finish();

                GLDefaultFramebuffer.Bind(FramebufferTarget.ReadFramebuffer);
                GL.ReadBuffer(ReadBufferMode.ColorAttachment0);

                GL.ReadPixels(0, 0, GLDefaultFramebuffer.Width, GLDefaultFramebuffer.Height, PixelFormat.Bgra, PixelType.UnsignedByte, pixels);

                // Flip y
                using var canvas = new SKCanvas(bitmap);
                canvas.Scale(1, -1, 0, bitmap.Height / 2f);
                canvas.DrawBitmap(bitmap, new SKPoint());

                using var bitmapWindows = bitmap.ToBitmap();
                Clipboard.SetImage(bitmapWindows);

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
                FullScreenForm = new Form
                {
                    Text = "Source 2 Viewer Fullscreen",
                    Icon = Program.MainForm.Icon,
                    ControlBox = false,
                    FormBorderStyle = FormBorderStyle.None,
                    WindowState = FormWindowState.Maximized
                };
                FullScreenForm.Controls.Add(GLControl);
                FullScreenForm.Show();
                FullScreenForm.Focus();
                FullScreenForm.FormClosed += OnFullScreenFormClosed;
            }
        }

        private void OnKeyUp(object sender, KeyEventArgs e)
        {
            CurrentlyPressedKeys &= ~RemapKey(e.KeyCode);
        }

        private void OnFullScreenFormClosed(object sender, EventArgs e)
        {
            glControlContainer.Controls.Add(GLControl);
            GLControl.Focus();

            var form = (Form)sender;
            form.FormClosed -= OnFullScreenFormClosed;

            FullScreenForm = null;
        }

        private void OnDisposed(object sender, EventArgs e)
        {
            GLControl.Load -= OnLoad;
            GLControl.Paint -= OnPaint;
            GLControl.Resize -= OnResize;
            GLControl.MouseEnter -= OnMouseEnter;
            GLControl.MouseLeave -= OnMouseLeave;
            GLControl.MouseUp -= OnMouseUp;
            GLControl.MouseDown -= OnMouseDown;
            GLControl.MouseMove -= OnMouseMove;
            GLControl.MouseWheel -= OnMouseWheel;
            GLControl.KeyDown -= OnKeyDown;
            GLControl.KeyUp -= OnKeyUp;
            GLControl.GotFocus -= OnGotFocus;
            GLControl.LostFocus -= OnLostFocus;
            GLControl.VisibleChanged -= OnVisibleChanged;
            Disposed -= OnDisposed;
        }

        private void OnVisibleChanged(object sender, EventArgs e)
        {
            if (GLControl.Visible)
            {
                GLControl.Focus();
                HandleResize();
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

        protected virtual void OnMouseDown(object sender, WinFormsMouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left && e.Button != MouseButtons.Right)
            {
                return;
            }

            InitialMousePosition = new Point(e.X, e.Y);
            MouseDelta = Point.Empty;
            MousePreviousPosition = GLControl.PointToScreen(InitialMousePosition);

            if (e.Button == MouseButtons.Left)
            {
                CurrentlyPressedKeys |= TrackedKeys.MouseLeft;

                if (e.Clicks == 2)
                {
                    var intent = ModifierKeys.HasFlag(Keys.Control)
                        ? PickingIntent.Open
                        : PickingIntent.Details;
                    Picker?.RequestNextFrame(e.X, e.Y, intent);
                }
            }
            /* TODO: phase this obscure bind out */
            else if (e.Button == MouseButtons.Right)
            {
                CurrentlyPressedKeys |= TrackedKeys.MouseRight;

                if (e.Clicks == 2)
                {
                    Picker?.RequestNextFrame(e.X, e.Y, PickingIntent.Open);
                }
            }
        }

        protected virtual void OnMouseUp(object sender, WinFormsMouseEventArgs e)
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

        protected virtual void OnMouseMove(object sender, WinFormsMouseEventArgs e)
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
                // Windows has a 1px edge on bottom of the screen where cursor can't reach
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

        protected virtual void OnMouseWheel(object sender, WinFormsMouseEventArgs e)
        {
            var modifier = Camera.ModifySpeed(e.Delta > 0);

            SetMoveSpeedOrZoomLabel($"Move speed: {modifier:0.0}x (scroll to change)");
        }

        protected void SetMoveSpeedOrZoomLabel(string text) => moveSpeed.Text = text;

#if DEBUG
        private static void OnDebugMessage(DebugSource source, DebugType type, int id, DebugSeverity severity, int length, IntPtr pMessage, IntPtr pUserParam)
        {
            if (source == DebugSource.DebugSourceApplication && severity == DebugSeverity.DebugSeverityNotification)
            {
                return;
            }

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

            if (type == DebugType.DebugTypeError)
            {
                Debugger.Break();
            }
        }

        private static readonly DebugProc OpenGLDebugMessageDelegate = OnDebugMessage;
#endif

        public Framebuffer GLDefaultFramebuffer;
        public Framebuffer MainFramebuffer;
        private int MaxSamples;
        private int NumSamples => Math.Max(1, Math.Min(Settings.Config.AntiAliasingSamples, MaxSamples));

        private void OnLoad(object sender, EventArgs e)
        {
            GLControl.MakeCurrent();
            GLControl.VSync = Settings.Config.Vsync != 0;

            CheckOpenGL();
            MaxSamples = GL.GetInteger(GetPName.MaxSamples);
            GLDefaultFramebuffer = Framebuffer.GetGLDefaultFramebuffer();

            frametimeQuery1 = GL.GenQuery();
            frametimeQuery2 = GL.GenQuery();

            GL.BeginQuery(QueryTarget.TimeElapsed, frametimeQuery2);
            GL.EndQuery(QueryTarget.TimeElapsed);

            textRenderer.Load();

            // Application semantics / default state
            GL.Enable(EnableCap.TextureCubeMapSeamless);
            GL.Enable(EnableCap.CullFace);
            GL.CullFace(CullFaceMode.Back);
            GL.Enable(EnableCap.DepthTest);

            // reverse z
            GL.ClipControl(ClipOrigin.LowerLeft, ClipDepthMode.ZeroToOne);
            GL.DepthFunc(DepthFunction.Greater);
            GL.ClearDepth(0.0f);

#if DEBUG
            GL.Enable(EnableCap.DebugOutput);
            GL.Enable(EnableCap.DebugOutputSynchronous);
            GL.DebugMessageControl(DebugSourceControl.DebugSourceApi, DebugTypeControl.DebugTypeOther, DebugSeverityControl.DebugSeverityNotification, 0, Array.Empty<int>(), false);
            GL.DebugMessageCallback(OpenGLDebugMessageDelegate, IntPtr.Zero);
#endif

            try
            {
                MainFramebuffer = Framebuffer.Prepare(GLControl.Width,
                    GLControl.Height,
                    NumSamples,
                    new(PixelInternalFormat.R11fG11fB10f, PixelFormat.Rgb, PixelType.UnsignedInt),
                    Framebuffer.DepthAttachmentFormat.Depth32F
                );

                GLLoad?.Invoke(this, e);
            }
            catch (Exception exception)
            {
                var control = new CodeTextBox(exception.ToString());
                glControlContainer.Controls.Clear();
                glControlContainer.Controls.Add(control);

                throw;
            }

            HandleResize();
            GLPostLoad?.Invoke(this);
            GLPostLoad = null;
        }

        private void OnPaint(object sender, EventArgs e)
        {
            Application.DoEvents();
            Draw();
        }

        private void Draw()
        {
            if (!GLControl.Visible || GLControl.IsDisposed || !GLControl.Context.IsCurrent)
            {
                return;
            }

            if (MainFramebuffer.InitialStatus != FramebufferErrorCode.FramebufferComplete)
            {
                return;
            }

            var currentTime = Stopwatch.GetTimestamp();
            var elapsed = Stopwatch.GetElapsedTime(lastUpdate, currentTime);
            lastUpdate = currentTime;

            var frameTime = (float)elapsed.TotalSeconds;

            if (MouseOverRenderArea && this is not GLTextureViewer)
            {
                var pressedKeys = CurrentlyPressedKeys;
                var modifierKeys = ModifierKeys;

                if ((modifierKeys & Keys.Shift) > 0)
                {
                    pressedKeys |= TrackedKeys.Shift;
                }

                if ((modifierKeys & Keys.Alt) > 0)
                {
                    pressedKeys |= TrackedKeys.Alt;
                }

                Camera.Tick(frameTime, pressedKeys, MouseDelta);
                MouseDelta = Point.Empty;
            }

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

                using (new GLDebugGroup("Text Render"))
                {
                    textRenderer.RenderText(MainFramebuffer.Width, MainFramebuffer.Height, 2f, MainFramebuffer.Height - 4f, 14f, System.Numerics.Vector4.UnitW, lastFps);
                }
            }

            // blit to the default opengl framebuffer used by the control
            if (MainFramebuffer != GLDefaultFramebuffer)
            {
                using (new GLDebugGroup("Blit Framebuffer"))
                {
                    MainFramebuffer.Bind(FramebufferTarget.ReadFramebuffer);
                    GL.ReadBuffer(ReadBufferMode.ColorAttachment0);

                    GLDefaultFramebuffer.Bind(FramebufferTarget.DrawFramebuffer);
                    GL.DrawBuffer(DrawBufferMode.Back);

                    var (w, h) = (GLControl.Width, GLControl.Height);
                    GL.BlitFramebuffer(0, 0, w, h, 0, 0, w, h, ClearBufferMask.ColorBufferBit, BlitFramebufferFilter.Nearest);
                }
            }

            if (MainFramebuffer != GLDefaultFramebuffer)
            {

                GLDefaultFramebuffer.Bind(FramebufferTarget.Framebuffer);
            }

            GLControl.SwapBuffers();
            Picker?.TriggerEventIfAny();
            GLControl.Invalidate();
        }

        protected virtual void OnResize(object sender, EventArgs e)
        {
            if (MainFramebuffer is null)
            {
                return;
            }

            HandleResize();
            Draw();
        }

        private void HandleResize()
        {
            var (w, h) = (GLControl.Width, GLControl.Height);

            if (w <= 0 || h <= 0)
            {
                return;
            }

            GLDefaultFramebuffer.Resize(w, h);
            MainFramebuffer.Resize(w, h, NumSamples);

            if (MainFramebuffer.InitialStatus == FramebufferErrorCode.FramebufferUndefined)
            {
                var status = MainFramebuffer.Initialize();

                if (status != FramebufferErrorCode.FramebufferComplete)
                {
                    Log.Error(nameof(GLViewerControl), $"Framebuffer failed to bind with error: {status}");
                    Log.Info(nameof(GLViewerControl), "Falling back to default framebuffer.");

                    DisposeFramebuffer();
                    MainFramebuffer = GLDefaultFramebuffer;
                }
            }

            Camera.SetViewportSize(w, h);
            Picker?.Resize(w, h);
        }

        private void DisposeFramebuffer()
        {
            GLDefaultFramebuffer?.Dispose();
            MainFramebuffer?.Dispose();
        }

        private void OnGotFocus(object sender, EventArgs e)
        {
            if (!MainFramebuffer.HasValidDimensions())
            {
                return;
            }

            GLControl.MakeCurrent();
            HandleResize();
            Draw();
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
            Keys.LShiftKey => TrackedKeys.Shift,
            Keys.LMenu => TrackedKeys.Alt,
            _ => TrackedKeys.None,
        };

        private static void CheckOpenGL()
        {
            if (hasCheckedOpenGL)
            {
                return;
            }

            hasCheckedOpenGL = true;

            Log.Debug(nameof(GLViewerControl), $"OpenGL version: {GL.GetString(StringName.Version)}");
            Log.Debug(nameof(GLViewerControl), $"OpenGL vendor: {GL.GetString(StringName.Vendor)}");
            Log.Debug(nameof(GLViewerControl), $"OpenGL renderer: {GL.GetString(StringName.Renderer)}");
            Log.Debug(nameof(GLViewerControl), $"GLSL version: {GL.GetString(StringName.ShadingLanguageVersion)}");

            MaterialLoader.MaxTextureMaxAnisotropy = GL.GetFloat((GetPName)ExtTextureFilterAnisotropic.MaxTextureMaxAnisotropyExt);
        }
    }
}
