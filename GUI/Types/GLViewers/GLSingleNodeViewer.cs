using GUI.Types.Renderer;
using GUI.Utils;
using OpenTK.Graphics.OpenGL;
using SkiaSharp;
using ValveResourceFormat;

#nullable disable

namespace GUI.Types.GLViewers
{
    /// <summary>
    /// GL Render control with model controls (render mode, animation panels).
    /// </summary>
    class GLSingleNodeViewer : GLSceneViewer, IDisposable
    {
        private Framebuffer SaveAsFbo;

        public GLSingleNodeViewer(VrfGuiContext vrfGuiContext, RendererContext rendererContext)
            : base(vrfGuiContext, rendererContext, Frustum.CreateEmpty())
        {
            //
        }

        protected override void AddUiControls()
        {
            AddRenderModeSelectionControl();
            AddBaseGridControl();

            base.AddUiControls();
        }

        public override void PreSceneLoad()
        {
            base.PreSceneLoad();
            LoadDefaultEnvironmentMap();
        }

        protected override void LoadScene()
        {
        }

        private void LoadDefaultEnvironmentMap()
        {
            using var stream = Program.Assembly.GetManifestResourceStream("GUI.Utils.industrial_sunset_puresky.vtex_c");

            using var resource = new Resource()
            {
                FileName = "vrf_default_cubemap.vtex_c"
            };
            resource.Read(stream);

            var texture = Scene.RendererContext.MaterialLoader.LoadTexture(resource);
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
        Vector4 defaultSunColor = new Vector4(255, 247, 235, 700) / 255.0f;

        protected Vector2 sunAngles;

        protected override void OnPaint(RenderEventArgs e)
        {
            Input.EnableMouseLook = true;
            if ((CurrentlyPressedKeys & TrackedKeys.Control) != 0)
            {
                var delta = new Vector2(LastMouseDelta.Y, LastMouseDelta.X);

                sunAngles += delta;
                Scene.AdjustEnvMapSunAngle(Matrix4x4.CreateRotationZ(-delta.Y / 80f));
                UpdateSunAngles();
                Scene.UpdateBuffers();
                Input.EnableMouseLook = false;
            }

            base.OnPaint(e);
        }

        protected void UpdateSunAngles()
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
            using var lockedGl = MakeCurrent();

            var (w, h) = (MainFramebuffer.Width, MainFramebuffer.Height);

            MainFramebuffer.Bind(FramebufferTarget.Framebuffer);
            GL.ClearColor(new OpenTK.Mathematics.Color4(0, 0, 0, 0));
            GL.Clear(MainFramebuffer.ClearMask);

            DrawMainScene();

            if (SaveAsFbo == null)
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
            FramebufferBlit(MainFramebuffer, SaveAsFbo, flipY: true);

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
