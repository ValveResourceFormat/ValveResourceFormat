using System;
using System.Numerics;

namespace GUI.Types.Renderer;
class SceneCubemapFog : SceneNode
{
    public float StartDist { get; set; }
    public float EndDist { get; set; }
    public float FalloffExponent { get; set; }
    public float HeightStart { get; set; }
    public float HeightEnd { get; set; }
    public float HeightExponent { get; set; }
    public float LodBias { get; set; }
    public float Opacity { get; set; }
    public bool UseHeightFog { get; set; }
    public RenderTexture CubemapFogTexture { get; set; }
    public float ExposureBias { get; set; }

    public Vector4 OffsetScaleBiasExponent(Vector3 mapOffset, float mapScale)
    {
        var scale = mapScale / (EndDist - StartDist);
        var offset = -(StartDist * scale) / mapScale;

        return new Vector4(offset, scale, LodBias, FalloffExponent);
    }
    // HeightWidth is equal to HeightEnd - HeightStart
    // Height width ADDS to heightStart
    public Vector4 Height_OffsetScaleExponentLog2Mip(Vector3 mapOffset, float mapScale)
    {
        float offset;
        float scale;
        if (UseHeightFog || ((HeightEnd - HeightStart) > 0)) // width = 0 is a substitution for UseHeightFog
        {
            var bias = (HeightStart - mapOffset.Z) / mapScale;

            scale = mapScale / (HeightEnd - HeightStart);
            offset = -(bias * scale);
            //Console.WriteLine($"height cubefog {new Vector4(offset, scale, HeightExponent, CubemapFogTexture.NumMipLevels)}");
        }
        else
        {
            offset = 0f;
            scale = 0f; // is this right?
        }

        return new Vector4(offset, scale, HeightExponent, Math.Min(7f, CubemapFogTexture.NumMipLevels)); // these latter two values are wrong on deskjob?
    }
    public Vector4 CullingParams_Opacity(Vector3 mapOffset, float mapScale)
    {
        var distCull = StartDist / mapScale;
        distCull *= distCull;
        var heightCull = (UseHeightFog || ((HeightEnd - HeightStart) > 0)) ? ((HeightStart - mapOffset.Z) / mapScale) : float.PositiveInfinity;

        return new Vector4(distCull, heightCull, MathF.Pow(2f, ExposureBias), Opacity);
    }

    public SceneCubemapFog(Scene scene) : base(scene)
    {
    }
    public override void Render(Scene.RenderContext context)
    {
    }

    public override void Update(Scene.UpdateContext context)
    {
    }
}
