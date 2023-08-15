using System;
using System.Numerics;

namespace GUI.Types.Renderer;
class SceneGradientFog : SceneNode
{
    public float StartDist { get; set; }
    public float EndDist { get; set; }
    public float FalloffExponent { get; set; }
    public float HeightStart { get; set; }
    public float HeightEnd { get; set; }
    public float VerticalExponent { get; set; }
    public Vector3 Color { get; set; }
    public float Strength { get; set; }
    public float MaxOpacity { get; set; }

    public Vector4 GetBiasAndScale(Vector3 mapOffset, float mapScale)
    {
        var startDist = StartDist;
        var endDist = EndDist;

        var distScale = mapScale / (endDist - startDist);
        var distBias = -(startDist * distScale) / mapScale;

        // this might be same as cubemap fog height calculations
        var startHeight = HeightStart - mapOffset.Z;
        var endHeight = HeightEnd - mapOffset.Z;

        var heightScale = mapScale / (startHeight - endHeight);
        var heightBias = -(endHeight * heightScale) / mapScale;

        return new Vector4(distBias, heightBias, distScale, heightScale);
    }
    public Vector2 Exponents => new(FalloffExponent, VerticalExponent);
    public Vector4 Color_Opacity => new(Color * Strength, MaxOpacity);
    public Vector2 CullingParams(Vector3 mapOffset, float mapScale)
    {
        return new Vector2((StartDist * StartDist) / (mapScale * mapScale), (HeightStart + mapOffset.Z) / mapScale);
    }

    public SceneGradientFog(Scene scene) : base(scene)
    {
    }
    public override void Render(Scene.RenderContext context)
    {
    }

    public override void Update(Scene.UpdateContext context)
    {
    }
}
