using System.Diagnostics;
using GUI.Utils;
using OpenTK.Graphics.OpenGL;
using SkiaSharp;
using ValveResourceFormat;
using ValveResourceFormat.Renderer;

namespace GUI.Types.GLViewers
{
    /// <summary>
    /// GL Render control with model controls (render mode, animation panels).
    /// </summary>
    class GLSingleNodeViewer : GLSceneViewer, IDisposable
    {
        private Framebuffer? SaveAsFbo;

        public GLSingleNodeViewer(VrfGuiContext vrfGuiContext, RendererContext rendererContext)
            : base(vrfGuiContext, rendererContext, Frustum.CreateEmpty())
        {
            //
        }

        protected override void AddUiControls()
        {
            AddRenderModeAndWireframeControls();
            AddBaseGridControl();

            Scene.ShowToolsMaterials = true;

            base.AddUiControls();
        }

        private void AddRenderModeAndWireframeControls()
        {
            if (this is GLMaterialViewer)
            {
                return;
            }

            Debug.Assert(UiControl != null);

            using (UiControl.BeginGroup("Render"))
            {
                AddRenderModeSelectionControl();
                AddWireframeToggleControl();
            }
        }

        public override void PreSceneLoad()
        {
            base.PreSceneLoad();
            base.LoadDefaultLighting();
        }

        protected override void LoadScene()
        {
        }

        protected override void OnPaint(float frameTime)
        {
            base.OnPaint(frameTime);
        }

        protected override void OnPicked(object? sender, PickingTexture.PickingResponse pickingResponse)
        {
            //
        }

        // Render only the main scene nodes into a transparent framebuffer
        protected override SKBitmap? ReadPixelsToBitmap()
        {
            if (MainFramebuffer is null)
            {
                return null;
            }

            using var lockedGl = MakeCurrent();

            var (w, h) = (MainFramebuffer.Width, MainFramebuffer.Height);

            MainFramebuffer.Bind(FramebufferTarget.Framebuffer);
            GL.ClearColor(new OpenTK.Mathematics.Color4(0, 0, 0, 0));
            GL.Clear(MainFramebuffer.ClearMask);

            Renderer.DrawMainScene();

            if (SaveAsFbo is null)
            {
                SaveAsFbo = Framebuffer.Prepare(nameof(SaveAsFbo), w, h, 0, new(PixelInternalFormat.Rgba8, PixelFormat.Bgra, PixelType.UnsignedByte), null);
                SaveAsFbo.ClearColor = new OpenTK.Mathematics.Color4(0, 0, 0, 0);
                SaveAsFbo.Initialize();
            }
            else
            {
                SaveAsFbo.Resize(w, h);
            }

            SaveAsFbo.BindAndClear();
            Renderer.PostprocessRender(MainFramebuffer, SaveAsFbo, flipY: true);

            GL.Flush();
            GL.Finish();

            SaveAsFbo.Bind(FramebufferTarget.ReadFramebuffer);
            GL.ReadBuffer(ReadBufferMode.ColorAttachment0);

            var bitmap = new SKBitmap(w, h, SKColorType.Bgra8888, SKAlphaType.Unpremul);
            var pixels = bitmap.GetPixels(out var length);

            GL.ReadPixels(0, 0, w, h, PixelFormat.Bgra, PixelType.UnsignedByte, pixels);

            return bitmap;
        }
    }
}
