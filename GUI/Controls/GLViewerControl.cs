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
using WinFormsMouseEventArgs = System.Windows.Forms.MouseEventArgs;
using static GUI.Types.Renderer.PickingTexture;

namespace GUI.Controls
{
    internal partial class GLViewerControl : UserControl
    {
        private const long TicksPerSecond = 10_000_000;
        private static readonly float TickFrequency = TicksPerSecond / Stopwatch.Frequency;

        public GLControl GLControl { get; }
        public IGLViewer GLViewer { get; }

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

        long lastFpsUpdate;
        int frames;

        Vector2 initialMousePosition;

        public GLViewerControl(IGLViewer glViewer)
        {
            InitializeComponent();
            Dock = DockStyle.Fill;

            GLViewer = glViewer;
            Camera = new Camera();

            // Initialize GL control
            var flags = GraphicsContextFlags.ForwardCompatible;

#if DEBUG
            flags |= GraphicsContextFlags.Debug;
#endif

            GLControl = new GLControl(new GraphicsMode(32, 24, 0, 8), 3, 3, flags);
            GLControl.Load += OnLoad;
            GLControl.Resize += OnResize;
            GLControl.MouseEnter += OnMouseEnter;
            GLControl.MouseLeave += OnMouseLeave;
            GLControl.MouseUp += OnMouseUp;
            GLControl.MouseDown += OnMouseDown;
            GLControl.MouseWheel += OnMouseWheel;
            GLControl.VisibleChanged += OnVisibleChanged;

            GLControl.Dock = DockStyle.Fill;
            glControlContainer.Controls.Add(GLControl);
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

        private void OnLoad(object sender, EventArgs e)
        {
            GLControl.MakeCurrent();

            CheckOpenGL();

            try
            {
                GLLoad?.Invoke(this, e);
            }
            catch (Exception exception)
            {
                var control = new MonospaceTextBox
                {
                    Text = exception.ToString(),
                    Dock = DockStyle.Fill
                };

                glControlContainer.Controls.Clear();
                glControlContainer.Controls.Add(control);

                throw;
            }

            HandleResize();
            GLPostLoad?.Invoke(this);
            GLPostLoad = null;

            RenderLoopThread.RegisterInstance();

            if (GLControl.Visible)
            {
                RenderLoopThread.SetCurrentGLControl(this);
            }
        }

        public void Draw(long currentTime, long elapsed)
        {
            var frameTime = elapsed * TickFrequency / TicksPerSecond;

            Camera.HandleInput(Mouse.GetState(), Keyboard.GetState());
            Camera.Tick(frameTime);

            GL.ClearColor(Settings.BackgroundColor);
            GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);

            GLPaint?.Invoke(this, new RenderEventArgs { FrameTime = frameTime, Camera = Camera });

            GLControl.SwapBuffers();

            frames++;

            var fpsElapsed = (currentTime - lastFpsUpdate) * TickFrequency;

            if (fpsElapsed >= TicksPerSecond)
            {
                SetFps(frames);
                lastFpsUpdate = currentTime;
                frames = 0;
            }
        }

        private void OnDisposing()
        {
            GLControl.Load -= OnLoad;
            GLControl.Resize -= OnResize;
            GLControl.MouseEnter -= OnMouseEnter;
            GLControl.MouseLeave -= OnMouseLeave;
            GLControl.MouseUp -= OnMouseUp;
            GLControl.MouseDown -= OnMouseDown;
            GLControl.MouseWheel -= OnMouseWheel;
            GLControl.VisibleChanged -= OnVisibleChanged;

            RenderLoopThread.UnsetCurrentGLControl(this);
            RenderLoopThread.UnregisterInstance();
        }

        private void OnVisibleChanged(object sender, EventArgs e)
        {
            if (GLControl.Visible)
            {
                HandleResize();

                RenderLoopThread.SetCurrentGLControl(this);
            }
        }

        private void OnResize(object sender, EventArgs e)
        {
            HandleResize();
        }

        private void HandleResize()
        {
            GLControl.MakeCurrent();
            Camera.SetViewportSize(GLControl.Width, GLControl.Height);
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
                MaterialLoader.MaxTextureMaxAnisotropy = GL.GetInteger((GetPName)ExtTextureFilterAnisotropic.MaxTextureMaxAnisotropyExt);
            }
            else
            {
                Console.Error.WriteLine("GL_EXT_texture_filter_anisotropic is not supported");
            }
        }
    }
}
