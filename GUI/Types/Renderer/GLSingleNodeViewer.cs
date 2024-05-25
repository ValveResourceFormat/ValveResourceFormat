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
            base.Dispose(disposing);

            if (disposing)
            {
                SaveAsFbo?.Dispose();
            }
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
            Scene.LightingInfo.UseSceneBoundsForSunLightFrustum = true;

            sunAngles = defaultSunAngles;
            Scene.LightingInfo.LightingData.LightColor_Brightness[0] = defaultSunColor;
            UpdateSunAngles();
        }

        Vector2 defaultSunAngles = new(80f, 170f);
        Vector4 defaultSunColor = new Vector4(255, 247, 235, 400) / 255.0f;

        readonly Camera previousCamera = new();
        Vector2 sunAngles;

        protected override void OnPaint(object sender, RenderEventArgs e)
        {
            if ((CurrentlyPressedKeys & TrackedKeys.Control) != 0)
            {
                var delta = new Vector2(Camera.Pitch, Camera.Yaw) - new Vector2(previousCamera.Pitch, previousCamera.Yaw);
                delta.Y *= -1f;
                delta *= 150f;

                Camera.CopyFrom(previousCamera);
                sunAngles += delta;
                Scene.LightingInfo.LightingData.EnvMapWorldToLocal[0] *= Matrix4x4.CreateRotationZ(-delta.Y / 80f);
                UpdateSunAngles();
                Scene.UpdateBuffers();
            }

            base.OnPaint(sender, e);
            previousCamera.CopyFrom(Camera);
        }

        private void UpdateSunAngles()
        {
            Scene.LightingInfo.LightingData.LightToWorld[0] = Matrix4x4.CreateRotationY(sunAngles.X * MathF.PI / 180f)
                                                             * Matrix4x4.CreateRotationZ(sunAngles.Y * MathF.PI / 180f);
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
            DrawMainScene(SaveAsFbo);

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
