using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using GUI.Controls;
using GUI.Utils;
using OpenTK.Graphics.OpenGL;
using OpenTK.Windowing.Common;
using OpenTK.Windowing.Desktop;
using ValveResourceFormat.Renderer;

namespace GUI.Types.GLViewers;

internal abstract class GLBaseControl : IDisposable
{
    protected RendererControl? UiControl;

    protected OpenTK.Windowing.Desktop.NativeWindow? GLNativeWindow;
    public GLControl? GLControl { get; private set; }

    protected Form? FullScreenForm { get; private set; }
    public bool IsFullScreen => FullScreenForm != null;

    public bool MouseOverRenderArea;
    public Point MouseDelta;
    protected Point MousePreviousPosition;
    protected Point InitialMousePosition;
    protected TrackedKeys CurrentlyPressedKeys;
    public Point LastMouseDelta { get; protected set; }

    private bool mouseVisibilityChange;
    public bool GrabbedMouse
    {
        get;
        set
        {
            if (field != value)
            {
                mouseVisibilityChange = true;
            }

            field = value;
        }
    }

#if DEBUG
    public ShaderHotReload ShaderHotReload;
    public static ContextFlags Flags => ContextFlags.ForwardCompatible | ContextFlags.Debug;
#else
    public static ContextFlags Flags => ContextFlags.ForwardCompatible;
#endif

    protected readonly Lock glLock = new();

    private int MaxSamples;
    protected int NumSamples => Math.Max(1, Math.Min(Settings.Config.AntiAliasingSamples, MaxSamples));

    private bool FirstPaint = true;
    public long LastUpdate { get; protected set; }
    public bool Paused = true;
    protected long lastFpsUpdate;

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "CA2213:Disposable fields should be disposed", Justification = "RendererContext is disposed in Dispose method")]
    protected RendererContext RendererContext;

    protected Framebuffer? GLDefaultFramebuffer;
    protected Framebuffer? MainFramebuffer;

    public GLBaseControl(RendererContext rendererContext)
    {
        LastUpdate = Stopwatch.GetTimestamp();
        RendererContext = rendererContext;

#if DEBUG
        ShaderHotReload = new ShaderHotReload(this, rendererContext.ShaderLoader);
#endif
    }

    public Control InitializeUiControls(bool isPreview = false)
    {
        GLControl = new GLControl(glLock)
        {
            Dock = DockStyle.Fill
        };

        GLControl.Paint += OnGlControlPaint;
        GLControl.SizeChanged += OnSizeChanged;
        GLControl.MouseEnter += OnMouseEnter;
        GLControl.MouseLeave += OnMouseLeave;
        GLControl.MouseUp += OnMouseUp;
        GLControl.MouseDown += OnMouseDown;
        GLControl.MouseMove += OnMouseMove;
        GLControl.MouseWheel += OnMouseWheel;
        GLControl.PreviewKeyDown += OnPreviewKeyDown;
        GLControl.KeyDown += OnKeyDown;
        GLControl.KeyUp += OnKeyUp;
        GLControl.LostFocus += OnLostFocus;

        UiControl = new(isPreview)
        {
            Dock = DockStyle.Fill
        };

        UiControl.GLControlContainer.Controls.Add(GLControl);
        GLControl.AttachNativeWindow(GLNativeWindow!);

#if DEBUG
        ShaderHotReload.SetSynchronizingObject(GLControl);
#endif

        UiControl.SuspendLayout();

#if DEBUG // We want reload shaders to be the top most button
        var button = new ThemedButton
        {
            Text = "Reload shaders",
            AutoSize = true,
        };
        button.Click += OnButtonClick;

        void OnButtonClick(object? s, EventArgs e)
        {
            ShaderHotReload.ReloadShaders();
        }

        UiControl.AddControl(button);
#endif

        AddUiControls();

        UiControl.ResumeLayout();

        return UiControl;
    }

    public void InitializeRenderLoop(bool renderImmediately = false)
    {
        RenderLoopThread.RegisterInstance();

        if (renderImmediately)
        {
            RenderLoopThread.SetCurrentGLControl(this);
        }
    }

    protected virtual void AddUiControls()
    {
        // Implemented in derived classes
    }

    private void OnPreviewKeyDown(object? sender, PreviewKeyDownEventArgs e)
    {
        // Sink all inputs into gl control to prevent shortcuts like Ctrl+W and just pressing Alt going to parent
        e.IsInputKey = true;
    }

    protected virtual void OnKeyDown(object? sender, KeyEventArgs e)
    {
        CurrentlyPressedKeys |= RemapKey(e.KeyCode);

        e.Handled = true;
        e.SuppressKeyPress = true;

        if (e.KeyData == (Keys.Control | Keys.C))
        {
            var title = Program.MainForm.Text;
            Program.MainForm.Text = "Source 2 Viewer - Copying image to clipboard…";
            Application.DoEvents(); // Force the updated text to show up

            using var bitmap = ReadPixelsToBitmap();
            if (bitmap != null)
            {
                Log.Error(nameof(GLBaseControl), "Failed to copy image to clipboard, bitmat was null");
                ClipboardSetImage(bitmap);
            }

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
            GLControl?.Invalidate();
            FullScreenForm.FormClosed += OnFullScreenFormClosed;
        }
    }

    private void OnKeyUp(object? sender, KeyEventArgs e)
    {
        CurrentlyPressedKeys &= ~RemapKey(e.KeyCode);
    }

    protected virtual void OnResize(int w, int h)
    {
        if (w <= 0 || h <= 0)
        {
            return;
        }

        if (GLDefaultFramebuffer is null || MainFramebuffer is null)
        {
            return;
        }

        GLDefaultFramebuffer.Resize(w, h);

        if (MainFramebuffer != GLDefaultFramebuffer)
        {
            MainFramebuffer.Resize(w, h, NumSamples);
        }
    }

    private void OnLostFocus(object? sender, EventArgs e)
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
        Keys.Up => TrackedKeys.Forward,
        Keys.Down => TrackedKeys.Back,
        Keys.Left => TrackedKeys.Left,
        Keys.Right => TrackedKeys.Right,
        Keys.ControlKey => TrackedKeys.Control,
        Keys.ShiftKey or Keys.LShiftKey => TrackedKeys.Shift,
        Keys.Menu or Keys.LMenu => TrackedKeys.Alt,
        Keys.Space => TrackedKeys.Space,
        Keys.X => TrackedKeys.X,
        Keys.Escape => TrackedKeys.Escape,
        _ => TrackedKeys.None,
    };

    private void OnFullScreenFormClosed(object? sender, EventArgs e)
    {
        UiControl?.GLControlContainer.Controls.Add(GLControl);
        GLControl?.Focus();

        var form = (Form?)sender;
        form?.FormClosed -= OnFullScreenFormClosed;

        FullScreenForm = null;
    }

    public virtual void Dispose()
    {
        using var lockedGl = glLock.EnterScope();

        if (GLControl is not null)
        {
            RenderLoopThread.UnsetCurrentGLControl(this);
            RenderLoopThread.UnregisterInstance();

            GLControl.Paint -= OnGlControlPaint;
            GLControl.SizeChanged -= OnSizeChanged;
            GLControl.MouseEnter -= OnMouseEnter;
            GLControl.MouseLeave -= OnMouseLeave;
            GLControl.MouseUp -= OnMouseUp;
            GLControl.MouseDown -= OnMouseDown;
            GLControl.MouseMove -= OnMouseMove;
            GLControl.MouseWheel -= OnMouseWheel;
            GLControl.PreviewKeyDown -= OnPreviewKeyDown;
            GLControl.KeyDown -= OnKeyDown;
            GLControl.KeyUp -= OnKeyUp;
            GLControl.LostFocus -= OnLostFocus;

            UiControl?.Dispose();
        }

#if DEBUG
        ShaderHotReload?.Dispose();
#endif

        FullScreenForm?.Dispose();
        GLNativeWindow?.Dispose();
        RendererContext.Dispose();
    }

    private void OnMouseLeave(object? sender, EventArgs e)
    {
        MouseOverRenderArea = false;
    }

    private void OnMouseEnter(object? sender, EventArgs e)
    {
        MouseOverRenderArea = true;
    }

    protected virtual void OnMouseDown(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left && e.Button != MouseButtons.Right)
        {
            return;
        }

        InitialMousePosition = new Point(e.X, e.Y);
        MouseDelta = Point.Empty;

        if (GLControl != null)
        {
            GLControl.Focus();
            MousePreviousPosition = GLControl.PointToScreen(InitialMousePosition);
        }

        if (e.Button == MouseButtons.Left)
        {
            CurrentlyPressedKeys |= TrackedKeys.MouseLeft;
        }
        else if (e.Button == MouseButtons.Right)
        {
            CurrentlyPressedKeys |= TrackedKeys.MouseRight;
        }
    }

    protected virtual void OnMouseUp(object? sender, MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left)
        {
            CurrentlyPressedKeys &= ~TrackedKeys.MouseLeft;
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

    protected virtual void OnMouseMove(object? sender, MouseEventArgs e)
    {
        if (mouseVisibilityChange)
        {
            mouseVisibilityChange = false;
            Action changeVisibility = GrabbedMouse ? Cursor.Hide : Cursor.Show;
            changeVisibility();
        }

        if (!GrabbedMouse && (CurrentlyPressedKeys & TrackedKeys.MouseLeftOrRight) == 0)
        {
            return;
        }

        if (GLControl == null)
        {
            return;
        }

        var position = GLControl.PointToScreen(new Point(e.X, e.Y));
        var topLeft = GLControl.PointToScreen(Point.Empty);
        var bottomRight = topLeft + GLControl.Size;

        // Windows has a 1px edge on bottom and right of the screen where cursor can't reach
        // (assuming that there is no secondary screen past these edges)
        bottomRight.X -= 1;
        bottomRight.Y -= 1;

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

        if (GrabbedMouse)
        {
            var centerPoint = new Point(GLControl.Width / 2, GLControl.Height / 2);
            var screenCenter = GLControl.PointToScreen(centerPoint);
            MousePreviousPosition = screenCenter;
            Cursor.Position = screenCenter;
        }
    }

    protected virtual void OnMouseWheel(object? sender, MouseEventArgs e)
    {
        // Track mouse wheel state
        if (e.Delta > 0)
        {
            CurrentlyPressedKeys |= TrackedKeys.MouseWheelUp;
        }
        else if (e.Delta < 0)
        {
            CurrentlyPressedKeys |= TrackedKeys.MouseWheelDown;
        }
    }

    private void OnGlControlPaint(object? sender, EventArgs e)
    {
        RenderLoopThread.SetCurrentGLControl(this);
    }

    protected bool ShouldResize;

    protected virtual void OnSizeChanged(object? sender, EventArgs e)
    {
        ShouldResize = GLControl is not null && GLControl.Width > 0 && GLControl.Height > 0;
    }

    protected virtual void OnFirstPaint()
    {
        var current = Stopwatch.GetTimestamp();
        var elapsed = Stopwatch.GetElapsedTime(LastUpdate, current);
        LastUpdate = current;

        Log.Debug(nameof(GLBaseControl), $"First Paint: {elapsed}");
    }

    protected virtual void OnUpdate(float frameTime)
    {
        //
    }

    protected virtual void OnPaint(float frameTime)
    {
        //
    }

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
                Flags = Flags,
                RedBits = 8,
                GreenBits = 8,
                BlueBits = 8,
                AlphaBits = 0,
                DepthBits = 0,
                StencilBits = 0,
                StartFocused = false,
                StartVisible = false,
                ClientSize = new(4, 4),
                AutoLoadBindings = false,
                AutoIconify = false,
                WindowBorder = OpenTK.Windowing.Common.WindowBorder.Hidden,
                WindowState = OpenTK.Windowing.Common.WindowState.Normal,
                Title = "Source 2 Viewer OpenGL",
            };
            GLNativeWindow = new(settings);

            GLNativeWindow.Context.MakeNoneCurrent();
        });

        Debug.Assert(GLNativeWindow is not null);

        using var lockedGl = MakeCurrent();

        GLNativeWindow.Context.SwapInterval = Settings.Config.Vsync;

        if (!loadedBindings)
        {
            LoadOpenGLBindings();
            loadedBindings = true;
        }

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

        GLEnvironment.Initialize(VrfGuiContext.Logger);
        GLEnvironment.SetDefaultRenderState();

        MaxSamples = GL.GetInteger(GetPName.MaxSamples);
        GLDefaultFramebuffer = Framebuffer.GLDefaultFramebuffer;

        // Framebuffer used to draw geometry
        MainFramebuffer = Framebuffer.Prepare(nameof(MainFramebuffer),
            4, 4,
            NumSamples,
            new(PixelInternalFormat.Rgba16f, PixelFormat.Rgba, PixelType.HalfFloat),
            Framebuffer.DepthAttachmentFormat.Depth32FStencil8
        );

        var status = MainFramebuffer.Initialize();

        if (status != FramebufferErrorCode.FramebufferComplete)
        {
            Log.Error(nameof(GLBaseControl), $"Framebuffer failed to initialize with error: {status}");
            Log.Info(nameof(GLBaseControl), "Falling back to default framebuffer.");

            MainFramebuffer.Delete();
            MainFramebuffer = GLDefaultFramebuffer;
            GL.Enable(EnableCap.FramebufferSrgb);
        }

        MainFramebuffer.ClearMask |= ClearBufferMask.StencilBufferBit;

        OnGLLoad();
    }

    protected virtual void OnGLLoad()
    {
        //
    }

    protected void SetMoveSpeedOrZoomLabel(string text) => UiControl?.SetMoveSpeed(text);

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
        if (type == DebugType.DebugTypeError && source != DebugSource.DebugSourceShaderCompiler)
        {
            Debugger.Break();
        }
#endif
    }

    protected static readonly DebugProc OpenGLDebugMessageDelegate = OnDebugMessage;

    public void Draw(bool isPaused)
    {
        using var lockedGl = glLock.EnterScope();

        if (GLNativeWindow == null || !GLNativeWindow.Exists)
        {
            Log.Debug(nameof(GLBaseControl), "Attempted to draw onto destroyed GL Native Window.");
            RenderLoopThread.UnsetCurrentGLControl(this);
            return;
        }

        try
        {
            GLNativeWindow.Context.MakeCurrent();
        }
        catch (OpenTK.Windowing.GraphicsLibraryFramework.GLFWException e)
        {
            // 'The requested transformation operation is not supported.' when resizing the app
            // 'The handle is invalid.' when changing tab visibility
            Log.Debug(nameof(GLFWGraphicsContext), e.Message);
            return;
        }

        if (ShouldResize)
        {
            OnResize(GLNativeWindow.Size.X, GLNativeWindow.Size.Y);
            ShouldResize = false;
        }

        if (FirstPaint)
        {
            OnFirstPaint();
            FirstPaint = false;
        }

        var wasPaused = Paused;
        var resumingRender = wasPaused && !isPaused;
        Paused = isPaused;

        var currentTime = Stopwatch.GetTimestamp();
        var elapsed = resumingRender
            ? TimeSpan.Zero
            : Stopwatch.GetElapsedTime(LastUpdate, currentTime);

        // Clamp frametime so it does not cause issues in things like particle rendering
        var frameTime = MathF.Min(1f, (float)elapsed.TotalSeconds);
        LastUpdate = currentTime;
        OnUpdate(frameTime);

        OnPaint(frameTime);

        GLNativeWindow.Context.SwapBuffers();

        GLNativeWindow.Context.MakeNoneCurrent();
    }

    protected virtual void BlitFramebufferToScreen()
    {
        //
    }

    public GLLockScope MakeCurrent()
    {
        if (GLNativeWindow == null)
        {
            throw new InvalidOperationException("Cannot acquire GLLockScope without a valid GLNativeWindow.");
        }

        return new GLLockScope(glLock, GLNativeWindow.Context);
    }

    static bool loadedBindings;
    private static void LoadOpenGLBindings()
    {
        var provider = new OpenTK.Windowing.GraphicsLibraryFramework.GLFWBindingsContext();
        GL.LoadBindings(provider);
    }

    protected virtual SkiaSharp.SKBitmap? ReadPixelsToBitmap()
    {
        if (GLDefaultFramebuffer is null)
        {
            return null;
        }

        var bitmap = new SkiaSharp.SKBitmap(GLDefaultFramebuffer.Width, GLDefaultFramebuffer.Height, SkiaSharp.SKColorType.Bgra8888, SkiaSharp.SKAlphaType.Opaque);
        var pixels = bitmap.GetPixels(out var length);

        using var lockedGl = MakeCurrent();

        BlitFramebufferToScreen();

        GLDefaultFramebuffer.Bind(FramebufferTarget.ReadFramebuffer);
        GL.ReadPixels(0, 0, GLDefaultFramebuffer.Width, GLDefaultFramebuffer.Height, PixelFormat.Bgra, PixelType.UnsignedByte, pixels);

        // Flip y
        using var canvas = new SkiaSharp.SKCanvas(bitmap);
        canvas.Scale(1, -1, 0, bitmap.Height / 2f);
        canvas.DrawBitmap(bitmap, new SkiaSharp.SKPoint());

        return bitmap;
    }

    private static void ClipboardSetImage(SkiaSharp.SKBitmap bitmap)
    {
        var data = new DataObject();

        using var bitmapWindows = bitmap.ToBitmap();
        data.SetData(DataFormats.Bitmap, true, bitmapWindows);

        using var pngStream = new MemoryStream();
        using var pixels = bitmap.PeekPixels();
        var png = pixels.Encode(pngStream, new SkiaSharp.SKPngEncoderOptions(SkiaSharp.SKPngEncoderFilterFlags.Sub, zLibLevel: 1));

        bitmapWindows.Save(pngStream, System.Drawing.Imaging.ImageFormat.Png);
        data.SetData("PNG", false, pngStream);

        Clipboard.SetDataObject(data, copy: true);
    }
}
