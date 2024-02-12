using System.Reflection;
using GUI.Utils;
using OpenTK.Graphics.OpenGL;
using SkiaSharp;
using ValveResourceFormat;

namespace GUI.Types.Renderer
{
    /// <summary>
    /// GL Render control with model controls (render mode, animation panels).
    /// </summary>
    class GLSingleNodeViewer : GLSceneViewer, IDisposable
    {
        private Framebuffer SaveAsFbo;

        public GLSingleNodeViewer(VrfGuiContext guiContext)
            : base(guiContext, Frustum.CreateEmpty())
        {
            //
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                SaveAsFbo?.Dispose();
            }

            base.Dispose(disposing);
        }

        protected override void InitializeControl()
        {
            AddRenderModeSelectionControl();
            AddBaseGridControl();
        }

        public override void PreSceneLoad()
        {
            base.PreSceneLoad();
            LoadDefaultEnviromentMap();
        }

        protected override void LoadScene()
        {
            MainFramebuffer.ChangeFormat(new(PixelInternalFormat.Rgba8, PixelFormat.Rgba, PixelType.UnsignedInt), MainFramebuffer.DepthFormat);
        }

        private void LoadDefaultEnviromentMap()
        {
            var assembly = Assembly.GetExecutingAssembly();
            using var stream = assembly.GetManifestResourceStream("GUI.Utils.industrial_sunset_puresky.vtex_c");

            using var resource = new Resource()
            {
                FileName = "vrf_default_cubemap.vtex_c"
            };
            resource.Read(stream);

            var texture = Scene.GuiContext.MaterialLoader.LoadTexture(resource);
            var environmentMap = new SceneEnvMap(Scene, new AABB(new Vector3(float.MinValue), new Vector3(float.MaxValue)))
            {
                Transform = Matrix4x4.Identity,
                EdgeFadeDists = Vector3.Zero,
                HandShake = 0,
                ProjectionMode = 0,
                EnvMapTexture = texture,
            };

            Scene.LightingInfo.AddEnvironmentMap(environmentMap);
        }

        protected override void OnPaint(object sender, RenderEventArgs e)
        {
            Scene.LightingInfo.LightingData.SunLightPosition = Camera.ViewProjectionMatrix;
            Scene.LightingInfo.LightingData.SunLightColor = Vector4.One;

            base.OnPaint(sender, e);
        }

        protected override void OnPicked(object sender, PickingTexture.PickingResponse pickingResponse)
        {
            //
        }

        // Render only the main scene nodes into a transparent framebuffer
        protected override SKBitmap ReadPixelsToBitmap()
        {
            var (w, h) = (MainFramebuffer.Width, MainFramebuffer.Height);

            MainFramebuffer.Bind(FramebufferTarget.Framebuffer);
            GL.ClearColor(new OpenTK.Graphics.Color4(0, 0, 0, 0));
            GL.Clear(MainFramebuffer.ClearMask);

            DrawMainScene();

            if (SaveAsFbo == null)
            {
                SaveAsFbo = Framebuffer.Prepare(w, h, 0, new(PixelInternalFormat.Rgba8, PixelFormat.Bgra, PixelType.UnsignedByte), MainFramebuffer.DepthFormat);
                SaveAsFbo.ClearColor = new OpenTK.Graphics.Color4(0, 0, 0, 0);
                SaveAsFbo.Initialize();
            }
            else
            {
                SaveAsFbo.Resize(w, h);
            }

            SaveAsFbo.Clear();
            GL.BlitNamedFramebuffer(MainFramebuffer.FboHandle, SaveAsFbo.FboHandle, 0, h, w, 0, 0, 0, w, h, ClearBufferMask.ColorBufferBit, BlitFramebufferFilter.Nearest);

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
