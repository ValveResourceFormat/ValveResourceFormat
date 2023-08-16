using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;
using GUI.Types.Renderer;
using GUI.Utils;
using OpenTK;
using OpenTK.Graphics;
using OpenTK.Graphics.OpenGL;
using OpenTK.Input;
using static GUI.Types.Renderer.PickingTexture;
using WinFormsMouseEventArgs = System.Windows.Forms.MouseEventArgs;

namespace GUI.Controls
{
    partial class GLViewerControl : UserControl
    {
        private const long TicksPerSecond = 10_000_000;
        private static readonly float TickFrequency = TicksPerSecond / Stopwatch.Frequency;

        public GLControl GLControl { get; }

        private int currentControlsHeight = 35;

        public class RenderEventArgs
        {
            public float FrameTime { get; set; }
            public Camera Camera { get; set; }
        }

        public Camera Camera { get; }

        public event EventHandler<RenderEventArgs> GLPaint;
        public event EventHandler GLLoad;
        public Action<GLViewerControl> GLPostLoad { get; set; }
        private static bool hasCheckedOpenGL;

        protected Form FullScreenForm { get; private set; }
        long lastFpsUpdate;
        long lastUpdate;
        int frames;

        Vector2 initialMousePosition;

        public GLViewerControl()
        {
            InitializeComponent();
            Dock = DockStyle.Fill;

            Camera = new Camera();

            // Initialize GL control
            var flags = GraphicsContextFlags.ForwardCompatible;

#if DEBUG
            flags |= GraphicsContextFlags.Debug;
#endif

            GLControl = new GLControl(new GraphicsMode(32, 24, 0, Settings.Config.AntiAliasingSamples), 4, 6, flags);
            GLControl.Load += OnLoad;
            GLControl.Paint += OnPaint;
            GLControl.Resize += OnResize;
            GLControl.MouseEnter += OnMouseEnter;
            GLControl.MouseLeave += OnMouseLeave;
            GLControl.MouseUp += OnMouseUp;
            GLControl.MouseDown += OnMouseDown;
            GLControl.MouseWheel += OnMouseWheel;
            GLControl.KeyUp += OnKeyUp;
            GLControl.GotFocus += OnGotFocus;
            GLControl.VisibleChanged += OnVisibleChanged;
            Disposed += OnDisposed;

            GLControl.Dock = DockStyle.Fill;
            glControlContainer.Controls.Add(GLControl);
        }

        private void OnKeyUp(object sender, KeyEventArgs e)
        {
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

        private void OnFullScreenFormClosed(object sender, EventArgs e)
        {
            glControlContainer.Controls.Add(GLControl);
            GLControl.Focus();

            var form = (Form)sender;
            form.FormClosed -= OnFullScreenFormClosed;

            FullScreenForm = null;
        }

        private void SetFps(int fps)
        {
            fpsLabel.Text = fps.ToString(CultureInfo.InvariantCulture);
        }

        public void AddControl(Control control)
        {
            controlsPanel.Controls.Add(control);
            SetControlLocation(control);
        }

        public CheckBox AddCheckBox(string name, bool defaultChecked, Action<bool> changeCallback)
        {
            var checkbox = new GLViewerCheckboxControl(name, defaultChecked);
            checkbox.CheckBox.CheckedChanged += (_, __) =>
            {
                changeCallback(checkbox.CheckBox.Checked);

                GLControl.Focus();
            };

            controlsPanel.Controls.Add(checkbox);

            SetControlLocation(checkbox);

            return checkbox.CheckBox;
        }

        public ComboBox AddSelection(string name, Action<string, int> changeCallback)
        {
            var selectionControl = new GLViewerSelectionControl(name);

            controlsPanel.Controls.Add(selectionControl);

            SetControlLocation(selectionControl);

            selectionControl.ComboBox.SelectedIndexChanged += (_, __) =>
            {
                selectionControl.Refresh();
                changeCallback(selectionControl.ComboBox.SelectedItem as string, selectionControl.ComboBox.SelectedIndex);

                GLControl.Focus();
            };

            return selectionControl.ComboBox;
        }

        public CheckedListBox AddMultiSelection(string name, Action<CheckedListBox> initializeCallback, Action<IEnumerable<string>> changeCallback)
        {
            var selectionControl = new GLViewerMultiSelectionControl(name);

            if (initializeCallback != null)
            {
                initializeCallback(selectionControl.CheckedListBox);
            }

            controlsPanel.Controls.Add(selectionControl);

            SetControlLocation(selectionControl);

            selectionControl.CheckedListBox.ItemCheck += (_, __) =>
            {
                // ItemCheck is called before CheckedItems is updated
                BeginInvoke((MethodInvoker)(() =>
                {
                    selectionControl.Refresh();
                    changeCallback(selectionControl.CheckedListBox.CheckedItems.OfType<string>());

                    GLControl.Focus();
                }));
            };

            return selectionControl.CheckedListBox;
        }

        public GLViewerTrackBarControl AddTrackBar(Action<int> changeCallback)
        {
            var trackBar = new GLViewerTrackBarControl();
            trackBar.TrackBar.Scroll += (_, __) =>
            {
                changeCallback(trackBar.TrackBar.Value);
            };

            controlsPanel.Controls.Add(trackBar);

            SetControlLocation(trackBar);

            return trackBar;
        }

        public void SetControlLocation(Control control)
        {
            control.Location = new Point(0, currentControlsHeight);
            currentControlsHeight += control.Height;
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
            GLControl.MouseWheel -= OnMouseWheel;
            GLControl.KeyUp -= OnKeyUp;
            GLControl.GotFocus -= OnGotFocus;
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
            Camera.MouseOverRenderArea = false;
        }

        private void OnMouseEnter(object sender, EventArgs e)
        {
            Camera.MouseOverRenderArea = true;
        }

        private void OnMouseDown(object sender, WinFormsMouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                initialMousePosition = new Vector2(e.X, e.Y);
                if (e.Clicks == 2)
                {
                    Camera.Picker?.Request.NextFrame(e.X, e.Y, PickingIntent.Details);
                }
            }
            else if (e.Button == MouseButtons.Right)
            {
                initialMousePosition = new Vector2(e.X, e.Y);
                if (e.Clicks == 2)
                {
                    Camera.Picker?.Request.NextFrame(e.X, e.Y, PickingIntent.Open);
                }
            }
        }

        private void OnMouseUp(object sender, WinFormsMouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left || initialMousePosition != new Vector2(e.X, e.Y))
            {
                return;
            }

            Camera.Picker?.Request.NextFrame(e.X, e.Y, PickingIntent.Select);
        }

        private void OnMouseWheel(object sender, WinFormsMouseEventArgs e)
        {
            var modifier = Camera.ModifySpeed(e.Delta > 0);

            moveSpeed.Text = $"Move speed: {modifier:0.0}x (scroll to change)";
        }

#if DEBUG
        private static void OnDebugMessage(DebugSource source, DebugType type, int id, DebugSeverity severity, int length, IntPtr pMessage, IntPtr pUserParam)
        {
            var message = System.Runtime.InteropServices.Marshal.PtrToStringUTF8(pMessage, length);

            Console.WriteLine($"[OpenGL {severity} {source} {type}] {message}");

            if (type == DebugType.DebugTypeError || severity == DebugSeverity.DebugSeverityHigh || severity == DebugSeverity.DebugSeverityMedium)
            {
                //Debugger.Break();
            }
        }

        private static readonly DebugProc OpenGLDebugMessageDelegate = OnDebugMessage;
#endif

        private void OnLoad(object sender, EventArgs e)
        {
            GLControl.MakeCurrent();

            CheckOpenGL();

#if DEBUG
            GL.Enable(EnableCap.DebugOutput);
            GL.Enable(EnableCap.DebugOutputSynchronous);
            GL.DebugMessageControl(DebugSourceControl.DebugSourceApi, DebugTypeControl.DebugTypeOther, DebugSeverityControl.DebugSeverityNotification, 0, Array.Empty<int>(), false);
            GL.DebugMessageCallback(OpenGLDebugMessageDelegate, IntPtr.Zero);
#endif

            try
            {
                GLLoad?.Invoke(this, e);
            }
            catch (Exception exception)
            {
                var control = new MonospaceTextBox
                {
                    Text = exception.ToString().ReplaceLineEndings(),
                    Dock = DockStyle.Fill
                };

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

            var currentTime = Stopwatch.GetTimestamp();
            var elapsed = currentTime - lastUpdate;
            lastUpdate = currentTime;

            if (elapsed <= TickFrequency)
            {
                GLControl.Invalidate();

                return;
            }

            var frameTime = elapsed * TickFrequency / TicksPerSecond;

            Camera.HandleInput(Mouse.GetState(), Keyboard.GetState());
            Camera.Tick(frameTime);

            GL.ClearColor(Settings.BackgroundColor);
            GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);

            GLPaint?.Invoke(this, new RenderEventArgs { FrameTime = frameTime, Camera = Camera });

            GLControl.SwapBuffers();
            GLControl.Invalidate();

            frames++;

            var fpsElapsed = (currentTime - lastFpsUpdate) * TickFrequency;

            if (fpsElapsed >= TicksPerSecond)
            {
                SetFps(frames);
                lastFpsUpdate = currentTime;
                frames = 0;
            }
        }

        private void OnResize(object sender, EventArgs e)
        {
            HandleResize();
            Draw();
        }

        private void HandleResize()
        {
            Camera.SetViewportSize(GLControl.Width, GLControl.Height);
        }

        private void OnGotFocus(object sender, EventArgs e)
        {
            GLControl.MakeCurrent();
            HandleResize();
            Draw();
        }

        private static void CheckOpenGL()
        {
            if (hasCheckedOpenGL)
            {
                return;
            }

            hasCheckedOpenGL = true;

            Console.WriteLine("OpenGL version: " + GL.GetString(StringName.Version));
            Console.WriteLine("OpenGL vendor: " + GL.GetString(StringName.Vendor));
            Console.WriteLine("OpenGL renderer: " + GL.GetString(StringName.Renderer));
            Console.WriteLine("GLSL version: " + GL.GetString(StringName.ShadingLanguageVersion));

            var extensions = new HashSet<string>();
            var count = GL.GetInteger(GetPName.NumExtensions);
            for (var i = 0; i < count; i++)
            {
                var extension = GL.GetString(StringNameIndexed.Extensions, i);
                if (!extensions.Contains(extension))
                {
                    extensions.Add(extension);
                }
            }

            if (extensions.Contains("GL_EXT_texture_filter_anisotropic"))
            {
                MaterialLoader.MaxTextureMaxAnisotropy = GL.GetFloat((GetPName)ExtTextureFilterAnisotropic.MaxTextureMaxAnisotropyExt);
            }
            else
            {
                Console.Error.WriteLine("GL_EXT_texture_filter_anisotropic is not supported");
            }
        }
    }
}
