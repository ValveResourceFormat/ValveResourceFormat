using System.Diagnostics;
using ValveResourceFormat;
using ValveResourceFormat.Renderer.SceneEnvironment;
using ValveResourceFormat.Renderer.SceneNodes;
using ValveResourceFormat.ResourceTypes;

namespace GUI.Types.PackageViewer.ThumbnailRenderers;

internal class ThumbnailMaterialRenderer : ThumbnailRenderer
{
    private SceneSkybox2D? skybox;

    public override void SetResource(Resource resource)
    {
        Debug.Assert(SceneRenderer != null);

        SceneRenderer.Scene.ShowToolsMaterials = true;
        var renderMat = SceneRenderer.RendererContext.MaterialLoader.LoadMaterial(resource);
        renderMat.Shader.EnsureLoaded();
        renderMat.IsOverlay = false;

        // Render sky materials as a full-screen sky, like the skybox viewer.
        if (resource.DataBlock is Material { ShaderName: "sky.vfx" })
        {
            skybox ??= new SceneSkybox2D(renderMat);
            skybox.Material = renderMat;
            SceneRenderer.Skybox2D = skybox;
            SceneRenderer.Camera.Pitch = float.DegreesToRadians(20);
            SceneRenderer.Camera.Yaw = float.DegreesToRadians(180);
            return;
        }

        // Reset the skybox; it isn't cleared between reused thumbnails.
        SceneRenderer.Skybox2D = SceneRenderer.BaseBackground;

        var planeMesh = MeshSceneNode.CreateMaterialPreviewQuad(SceneRenderer.Scene, renderMat, new Vector2(32));

        var isHorizontalPlaneMaterial = renderMat.IsCs2Water;
        if (isHorizontalPlaneMaterial)
        {
            planeMesh.Transform = Matrix4x4.CreateRotationZ(float.DegreesToRadians(90f));

            SceneRenderer.Scene.LightingInfo.LightingData.LightToWorld[0] = Matrix4x4.CreateRotationY(float.DegreesToRadians(71))
                                                             * Matrix4x4.CreateRotationZ(float.DegreesToRadians(-196));

            SceneRenderer.Camera.FrameObjectFromAngle(Vector3.Zero, 32, 32, 0, 0, float.DegreesToRadians(-90f));
        }
        else
        {
            planeMesh.Transform *= Matrix4x4.CreateRotationY(float.DegreesToRadians(90f)) * Matrix4x4.CreateRotationX(float.DegreesToRadians(90f));

            SceneRenderer.Scene.LightingInfo.LightingData.LightToWorld[0] = Matrix4x4.CreateRotationY(float.DegreesToRadians(-22))
                                                             * Matrix4x4.CreateRotationZ(float.DegreesToRadians(205));

            SceneRenderer.Camera.FrameObjectFromAngle(Vector3.Zero, 0, 32, 32, float.DegreesToRadians(180f), 0);
        }

        SceneRenderer.Scene.Add(planeMesh, false);
    }
}
