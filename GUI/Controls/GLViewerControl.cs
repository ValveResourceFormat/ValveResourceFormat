using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;
using GUI.Types.Renderer;
using GUI.Utils;
using OpenTK;
using OpenTK.Graphics.OpenGL;
using OpenTK.Input;

namespace GUI.Controls
{
    internal partial class GLViewerControl : UserControl
    {
        public GLControl GLControl { get; }

        private List<Label> labels = new List<Label>();
        private List<GLViewerSelectionControl> selectionBoxes = new List<GLViewerSelectionControl>();

        public class RenderEventArgs
        {
            public float FrameTime { get; set; }
            public Camera Camera { get; set; }
        }

        public Camera Camera { get; set; }

        public event EventHandler<RenderEventArgs> GLPaint;
        public event EventHandler GLLoad;

        private readonly Stopwatch stopwatch;

        public GLViewerControl()
        {
            InitializeComponent();
            Dock = DockStyle.Fill;

            Camera = new Camera();

            stopwatch = new Stopwatch();

            // Initialize GL control
#if DEBUG
            GLControl = new OpenTK.GLControl(new OpenTK.Graphics.GraphicsMode(32, 24, 0, 8), 3, 3, OpenTK.Graphics.GraphicsContextFlags.Debug);
#else
            GLControl = new OpenTK.GLControl(new OpenTK.Graphics.GraphicsMode(32, 24, 0, 8), 3, 3, OpenTK.Graphics.GraphicsContextFlags.Default);
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

        public void SetFps(double fps)
        {
            fpsLabel.Text = $"FPS: {Math.Round(fps).ToString(CultureInfo.InvariantCulture)}";
        }

        public Label AddLabel(string text)
        {
            var label = new Label();
            label.Text = text;

            Controls.Add(label);

            labels.Add(label);

            RecalculatePositions();

            return label;
        }

        public ComboBox AddSelection(string name, Action<string, int> changeCallback)
        {
            var selectionControl = new GLViewerSelectionControl(name);

            Controls.Add(selectionControl);

            selectionBoxes.Add(selectionControl);

            RecalculatePositions();

            selectionControl.ComboBox.SelectionChangeCommitted += (_, __) =>
            {
                selectionControl.Refresh();
                changeCallback(selectionControl.ComboBox.SelectedItem as string, selectionControl.ComboBox.SelectedIndex);
            };

            return selectionControl.ComboBox;
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
        }

        private void GLViewerControl_Paint(object sender, PaintEventArgs e)
        {
            foreach (var label in labels)
            {
                label.Refresh();
            }

            foreach (var selectionBox in selectionBoxes)
            {
                selectionBox.Refresh();
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

            Console.WriteLine("OpenGL version: " + GL.GetString(StringName.Version));
            Console.WriteLine("OpenGL vendor: " + GL.GetString(StringName.Vendor));
            Console.WriteLine("GLSL version: " + GL.GetString(StringName.ShadingLanguageVersion));

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
            if (GLControl.Visible)
            {
                var frameTime = stopwatch.ElapsedMilliseconds / 1000f;
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

        private void CheckOpenGL()
        {
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
