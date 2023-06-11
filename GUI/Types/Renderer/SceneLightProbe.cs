using System.Numerics;

namespace GUI.Types.Renderer;

internal class SceneLightProbe : SceneNode
{
    public int HandShake { get; set; }

    /// <remarks>
    /// Used in lighting version 6 and 8.x
    /// </remarks>
    public RenderTexture Irradiance { get; set; }

    /// <remarks>
    /// Used in lighting version 8.1
    /// </remarks>
    public RenderTexture DirectLightIndices { get; set; }

    /// <remarks>
    /// Used in lighting version 8.1
    /// </remarks>
    public RenderTexture DirectLightScalars { get; set; }

    /// <remarks>
    /// Used in lighting version 8.2
    /// </remarks>
    public RenderTexture DirectLightShadows { get; set; }

    /// <remarks>
    /// Used in lighting version 8.2
    /// </remarks>
    public Vector3 AtlasArrayIndex { get; set; }

    public SceneLightProbe(Scene scene, AABB bounds) : base(scene)
    {
        LocalBoundingBox = bounds;
    }

    public override void Render(Scene.RenderContext context)
    {
    }

    public override void Update(Scene.UpdateContext context)
    {
    }
}
