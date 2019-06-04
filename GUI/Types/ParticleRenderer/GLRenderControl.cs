using System;
using System.Windows.Forms;
using GUI.Types.Renderer;
using OpenTK;
using OpenTK.Graphics;
using OpenTK.Graphics.OpenGL;

namespace GUI.Types.ParticleRenderer
{
    internal class GLRenderControl
    {
        public Camera Camera { get; set; }

        public Control Control { get; }

        private Label fpsLabel;
        private GLControl glControl;

        public GLRenderControl()
        {
            Camera = new Camera(-Vector3.One, Vector3.One);
            Control = InitializeControl();
        }

        protected virtual Control InitializeControl()
        {
            var panel = new Panel
            {
                Dock = DockStyle.Fill,
            };

            fpsLabel = new Label
            {
                Anchor = AnchorStyles.Top | AnchorStyles.Left,
                AutoSize = true,
                Dock = DockStyle.Top,
            };
            panel.Controls.Add(fpsLabel);

#if DEBUG
            glControl = new GLControl(new GraphicsMode(32, 24, 0, 8), 3, 3, GraphicsContextFlags.Debug);
#else
            meshControl = new GLControl(new GraphicsMode(32, 24, 0, 8), 3, 3, GraphicsContextFlags.Default);
#endif
            glControl.Dock = DockStyle.Fill;
            glControl.AutoSize = true;
            glControl.Load += OnLoad;
            glControl.Paint += OnPaint;
            glControl.Resize += OnResize;
            //glControl.MouseEnter += MeshControl_MouseEnter;
            //glControl.MouseLeave += MeshControl_MouseLeave;
            glControl.GotFocus += OnGotFocus;

            panel.Controls.Add(glControl);
            return panel;
        }

        private void OnLoad(object sender, EventArgs e)
        {
            HandleResize();
            Draw();
        }

        private void OnPaint(object sender, EventArgs e)
        {
            Draw();
        }

        private void Draw()
        {
            GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);

            glControl.SwapBuffers();
            glControl.Invalidate();
        }

        private void OnResize(object sender, EventArgs e)
        {
            HandleResize();
            Draw();
        }

        private void HandleResize()
        {
            Camera.SetViewportSize(Control.Width, Control.Height);
        }

        private void OnGotFocus(object sender, EventArgs e)
        {
            glControl.MakeCurrent();
            Draw();
        }
    }
}
