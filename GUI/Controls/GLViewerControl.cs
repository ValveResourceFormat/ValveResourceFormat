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

namespace GUI.Controls
{
    internal partial class GLViewerControl : UserControl
    {
        public GLControl GLControl { get; }

        private readonly List<Label> labels = new List<Label>();
        private readonly List<UserControl> selectionBoxes = new List<UserControl>();
        private readonly List<Control> otherControls = new List<Control>();

        public class RenderEventArgs
        {
            public float FrameTime { get; set; }
            public Camera Camera { get; set; }
        }

        public Camera Camera { get; }

        public event EventHandler<RenderEventArgs> GLPaint;
        public event EventHandler GLLoad;

        private readonly Stopwatch stopwatch;

        private static bool hasCheckedOpenGL;

        public GLViewerControl()
        {
            InitializeComponent();
            Dock = DockStyle.Fill;

            Camera = new Camera();

            stopwatch = new Stopwatch();

            // Initialize GL control
#if DEBUG
            GLControl = new GLControl(new GraphicsMode(32, 24, 0, 8), 3, 3, GraphicsContextFlags.Debug);
#else
            GLControl = new GLControl(new GraphicsMode(32, 24, 0, 8), 3, 3, GraphicsContextFlags.Default);
#endif
            GLControl.Load += OnLoad;
            GLControl.Paint += OnPaint;
            GLControl.Resize += OnResize;
            GLControl.MouseEnter += OnMouseEnter;
            GLControl.MouseLeave += OnMouseLeave;
            GLControl.GotFocus += OnGotFocus;
            GLControl.VisibleChanged += OnVisibleChanged;
            GLControl.Disposed += OnDisposed;

            GLControl.Dock = DockStyle.Fill;
            glControlContainer.Controls.Add(GLControl);
        }

        private void SetFps(double fps)
        {
            fpsLabel.Text = $"FPS: {Math.Round(fps).ToString(CultureInfo.InvariantCulture)}";
        }

        public Label AddLabel(string text)
        {
            var label = new Label();
            label.Text = text;
            label.AutoSize = true;

            controlsPanel.Controls.Add(label);

            labels.Add(label);

            RecalculatePositions();

            return label;
        }

        public void AddControl(Control control)
        {
            controlsPanel.Controls.Add(control);
            otherControls.Add(control);
            RecalculatePositions();
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
            otherControls.Add(checkbox);

            RecalculatePositions();

            return checkbox.CheckBox;
        }

        public ComboBox AddSelection(string name, Action<string, int> changeCallback)
        {
            var selectionControl = new GLViewerSelectionControl(name);

            controlsPanel.Controls.Add(selectionControl);
            selectionBoxes.Add(selectionControl);

            selectionControl.PerformAutoScale();

            RecalculatePositions();

            selectionControl.ComboBox.SelectionChangeCommitted += (_, __) =>
            {
                selectionControl.Refresh();
                changeCallback(selectionControl.ComboBox.SelectedItem as string, selectionControl.ComboBox.SelectedIndex);

                GLControl.Focus();
            };

            return selectionControl.ComboBox;
        }

        public CheckedListBox AddMultiSelection(string name, Action<IEnumerable<string>> changeCallback)
        {
            var selectionControl = new GLViewerMultiSelectionControl(name);

            controlsPanel.Controls.Add(selectionControl);
            selectionBoxes.Add(selectionControl);

            selectionControl.PerformAutoScale();

            RecalculatePositions();

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

        public GLViewerTrackBarControl AddTrackBar(string name, Action<int> changeCallback)
        {
            var trackBar = new GLViewerTrackBarControl(name);
            trackBar.TrackBar.ValueChanged += (_, __) =>
            {
                if (trackBar.IgnoreValueChanged)
                {
                    return;
                }
                changeCallback(trackBar.TrackBar.Value);

                GLControl.Focus();
            };

            controlsPanel.Controls.Add(trackBar);
            otherControls.Add(trackBar);

            RecalculatePositions();

            return trackBar;
        }

        public void RecalculatePositions()
        {
            var y = 25;

            foreach (var label in labels)
            {
                label.Location = new Point(0, y);
                y += label.Height;
            }

            foreach (var selection in selectionBoxes)
            {
                selection.Location = new Point(0, y);
                y += selection.Height;
            }

            foreach (var control in otherControls)
            {
                control.Location = new Point(0, y);
                control.Width = glControlContainer.Location.X;
                y += control.Height;
            }
        }

        private void OnDisposed(object sender, EventArgs e)
        {
            GLControl.Load -= OnLoad;
            GLControl.Paint -= OnPaint;
            GLControl.Resize -= OnResize;
            GLControl.MouseEnter -= OnMouseEnter;
            GLControl.MouseLeave -= OnMouseLeave;
            GLControl.GotFocus -= OnGotFocus;
            GLControl.VisibleChanged -= OnVisibleChanged;
            GLControl.Disposed -= OnDisposed;
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

        private void OnLoad(object sender, EventArgs e)
        {
            GLControl.MakeCurrent();

            CheckOpenGL();

            stopwatch.Start();

            GLLoad?.Invoke(this, e);

            HandleResize();
            Draw();
        }

        private void OnPaint(object sender, EventArgs e)
        {
            Draw();
        }

        private void Draw()
        {
            if (!GLControl.Visible)
            {
                return;
            }

            var elapsed = stopwatch.ElapsedMilliseconds;

            if (elapsed < 1)
            {
                GLControl.SwapBuffers();
                GLControl.Invalidate();

                return;
            }

            var frameTime = elapsed / 1000f;
            stopwatch.Restart();

            Camera.Tick(frameTime);
            Camera.HandleInput(Mouse.GetState(), Keyboard.GetState());

            SetFps(1f / frameTime);

            GL.ClearColor(Settings.BackgroundColor);
            GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);

            GLPaint?.Invoke(this, new RenderEventArgs { FrameTime = frameTime, Camera = Camera });

            GLControl.SwapBuffers();
            GLControl.Invalidate();
        }

        private void OnResize(object sender, EventArgs e)
        {
            HandleResize();
            Draw();
        }

        private void HandleResize()
        {
            Camera.SetViewportSize(GLControl.Width, GLControl.Height);
            RecalculatePositions();
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
                MaterialLoader.MaxTextureMaxAnisotropy = GL.GetInteger((GetPName)ExtTextureFilterAnisotropic.MaxTextureMaxAnisotropyExt);
            }
            else
            {
                Console.Error.WriteLine("GL_EXT_texture_filter_anisotropic is not supported");
            }
        }
    }
}
