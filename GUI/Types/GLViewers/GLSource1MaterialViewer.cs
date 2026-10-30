using GUI.Utils;
using ValveResourceFormat.Renderer;
using ValveResourceFormat.Renderer.SceneNodes;

namespace GUI.Types.GLViewers
{
    /// <summary>
    /// Previews a Source 1 material on a quad and a sphere.
    /// </summary>
    class GLSource1MaterialViewer(VrfGuiContext vrfGuiContext, RendererContext rendererContext, string materialPath)
        : GLSingleNodeViewer(vrfGuiContext, rendererContext)
    {
        protected override void LoadScene()
        {
            base.LoadScene();

            // Without a sky ambient the side away from the sun is black
            Scene.LightingInfo.SetHemisphereAmbientLighting(new Vector3(0.5f), new Vector3(0.25f));

            var material = Renderer.RendererContext.MaterialLoader.GetMaterial(materialPath, Scene.RenderAttributes);
            material.Shader.EnsureLoaded();
            material.IsOverlay = false;

            // The preview meshes follow the Source 2 tangent convention, with the bitangent along decreasing V
            material.FloatParams["g_flBitangentSign"] = -1f;

            var quad = MeshSceneNode.CreateMaterialPreviewQuad(Scene, material, new Vector2(32));
            quad.Transform = Matrix4x4.CreateRotationZ(float.DegreesToRadians(90f))
                * Matrix4x4.CreateRotationY(float.DegreesToRadians(90f))
                * Matrix4x4.CreateTranslation(0f, -20f, 0f);
            Scene.Add(quad, false);

            var sphere = MeshSceneNode.CreateMaterialPreviewSphere(Scene, material, 12f);
            sphere.Transform = Matrix4x4.CreateTranslation(0f, 16f, 0f);
            Scene.Add(sphere, false);
        }

        public override void PostSceneLoad()
        {
            base.PostSceneLoad();

            Input.OrbitModeAlways = true;
            Input.OrbitTarget = Vector3.Zero;
        }

        protected override void OnFirstPaint()
        {
            // Slightly from above, a camera at exactly the height of the grid draws nothing but the grid
            Input.Camera.FrameObjectFromAngle(Vector3.Zero, 0, 72, 32, float.DegreesToRadians(180f), float.DegreesToRadians(5f));
            Input.ForceUpdate = true;
        }
    }
}
