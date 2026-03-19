using System.Diagnostics;
using ValveResourceFormat;
using ValveResourceFormat.Renderer.SceneNodes;

namespace GUI.Types.PackageViewer.ThumbnailRenderers;

internal class ThumbnailMaterialRenderer : ThumbnailRenderer
{
    public override void SetResource(Resource resource)
    {
        Debug.Assert(SceneRenderer != null);

        SceneRenderer.Scene.ShowToolsMaterials = true;
        var renderMat = SceneRenderer.RendererContext.MaterialLoader.LoadMaterial(resource);
        renderMat.Shader.EnsureLoaded();
        renderMat.IsOverlay = false;

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
