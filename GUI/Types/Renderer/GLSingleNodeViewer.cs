using System.Numerics;
using System.Reflection;
using GUI.Utils;
using ValveResourceFormat;

namespace GUI.Types.Renderer
{
    /// <summary>
    /// GL Render control with model controls (render mode, animation panels).
    /// </summary>
    class GLSingleNodeViewer : GLSceneViewer
    {
        public GLSingleNodeViewer(VrfGuiContext guiContext)
            : base(guiContext, Frustum.CreateEmpty())
        {
            //
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
            //
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
    }
}
